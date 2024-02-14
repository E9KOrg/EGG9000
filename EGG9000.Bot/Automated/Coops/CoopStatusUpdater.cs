using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using Discord.Rest;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using Humanizer;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EGG9000.Common.Helpers;
using Discord.Net;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Runtime.CompilerServices;
using EGG9000.Common.Services;
using static EGG9000.Common.Helpers.Prefarm;
using static EGG9000.Bot.Automated.Coops.CoopStatusUpdater;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Factories;
using static Ei.Backup.Types;
using Microsoft.AspNetCore.Http;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.Contracts;
using static EGG9000.Bot.Helpers.DiscordHelpersExt;

namespace EGG9000.Bot.Automated.Coops {
    public class CoopStatusUpdater : _UpdaterBase<CoopStatusUpdater> {
#if DEBUG
        private static TimeSpan delay = TimeSpan.FromMinutes(0);
        private static TimeSpan interval = TimeSpan.FromMinutes(20);
#else
        private static TimeSpan delay = TimeSpan.FromMinutes(2);
        private static TimeSpan interval = TimeSpan.FromMinutes(15);
#endif
        private Dictionary<ulong, SocketTextChannel> _demeritChannels = new Dictionary<ulong, SocketTextChannel>();


        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public CoopStatusUpdater(
            IServiceProvider provider
            ) : base(interval, delay, provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            using(var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>()) {
                var users = (await _db.DBUsers.Where(x => x.GuildId > 0).AsQueryable().ToListAsync()).SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
                var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId != 0 && !x.DeletedChannel).ToListAsync();
                var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

                var throttler = new SemaphoreSlim(3);

#if DEBUG
                //coops = coops.Where(x => x.GuildId == 770469712064151593).ToList();
                //coops = coops.Where(x => x.Name.Equals("gasbrink5", StringComparison.OrdinalIgnoreCase)).ToList();
                //coops = coops.Where(x => x._StatusCompressed is null).ToList();
#endif

                var guildCoopGroups = coops.GroupBy(x => x.OverflowGuildId > 0 ? x.OverflowGuildId : x.GuildId).OrderBy(x => x.Count());
                foreach(var guildCoops in guildCoopGroups) {
                    if(cancellationToken.IsCancellationRequested) break;
                    var dbguild = dbguilds.FirstOrDefault(x => x.DiscordSeverId == guildCoops.Key || x.OverflowServers.Any(y => y == guildCoops.Key));
                    var guild = _client.Guilds.FirstOrDefault(x => x.Id == guildCoops.Key);
                    if(guild == null)
                        continue;
                    await guild.DownloadUsersAsync();
                    _logger.LogInformation("Coops for guild: {guildName}, Count {count}", guild.Name, guildCoops.Count());

                    var tasks = new List<Task>();

                    var rng = new Random();
                    //foreach(var coop in guildCoops.OrderBy(a => rng.Next())) {
                    foreach(var coop in guildCoops) {
                        if(cancellationToken.IsCancellationRequested) break;

                        while(!await throttler.WaitAsync(5000)) {
                            _logger.LogInformation("Waiting on throttle");
                        }
                        tasks.Add(Task.Run(async () => {
                            try {
                                await ProcessCoop(coop.Id, guild, users, dbguild, cancellationToken, _db);
                            } finally {
                                throttler.Release();
                            }
                        }));
                    }

                    await Task.WhenAll(tasks);

                    _logger.LogInformation("Co-op Count: {count}, Successful: {successful}, Error: {errors}, Guild: {guild}", guildCoops.Count(), tasks.Count(x => !x.IsFaulted), tasks.Count(x => x.IsFaulted), guild.Name);
                }

            }
        }

        public class UserWithStatus {
            public CustomBackup Backup { get; set; }
            public Ei.ContractCoopStatusResponse.Types.ContributionInfo Status { get; set; }
            public DBUser User { get; set; }
            public TimeSpan? Sleeping { get; set; }
            public UserCoopXref Xref { get; set; }
            public SocketGuildUser DiscordUser { get; set; }
            public double SiloTime { get; set; }
            public CustomFarmStats FarmStats { get; set; }
        }

        public static string Truncate(string value, int maxLength) {
            if(string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }


        public static List<string> GetStatusStringAsync(CoopDetails coopDetails, Contract contract) {
            var table = new List<List<FixedWidthCell>> {
                new List<FixedWidthCell> {
                new FixedWidthCell($"{coopDetails.CoopParticipants.Count}/{contract.MaxUsers}"),
                new FixedWidthCell("Discord", CellAlignment.Center),
                new FixedWidthCell("EB", CellAlignment.Center),
                new FixedWidthCell("Total", CellAlignment.Center),
                new FixedWidthCell("Rate", CellAlignment.Center),
                new FixedWidthCell("📈", CellAlignment.Center),
                new FixedWidthCell("%", CellAlignment.Center),
                new FixedWidthCell("🟡", CellAlignment.Center, true),
                new FixedWidthCell("⏲️", CellAlignment.Center, true),
                new FixedWidthCell("Silo"),
                new FixedWidthCell(""),
            }
            };
            var everyoneJoined = coopDetails.CoopParticipants.All(x => x.CoopStatus is not null);

            table.AddRange(coopDetails.CoopParticipants.OrderByDescending(x => x.Projected).Select(x => {
                var sleeping = x.OfflineTime.TotalMinutes > x.SiloTimeMinutes ? "💤" : "";

                if(x.OfflineTime.TotalMinutes > x.SiloTimeMinutes) {
                    sleeping = $"💤 Empty Silos {x.OfflineTime.Add(TimeSpan.FromMinutes(0 - x.SiloTimeMinutes)).Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()}";
                }

                if(coopDetails.Coop.FinishedOrFailed())
                    sleeping = "";

                if(x.CoopStatus?.TimeCheatDetected ?? false)
                    sleeping += " ⏱️";


                //var eb = Math.Pow(10, x.Status.SoulPower) * 100;
                var percent = coopDetails.GetProjectedShare(x);

                if(x.DBUser is null) {

                }

                return new List<FixedWidthCell> {
                    new FixedWidthCell(Truncate((everyoneJoined || x.DBUser is null ? "" : x.CoopStatus is not null ? "✅" : "❌") + (x.DBUser is null ? "👽" : "") + Regex.Replace(x.CoopStatus?.UserName ?? x.Backup?.UserName, @"\p{Cs}", ""), 11)),
                    new FixedWidthCell(Truncate(Regex.Replace(x.DiscordUser?.GetCleanName() ?? "", @"\p{Cs}", ""), 11)),
                    //new FixedWidthCell(x.Backup?.EarningsBonus.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell(x.EarningsBonus.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell(x.EggsShipped.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell($"{(x.Rate * 3600).ToEggString()}/h", CellAlignment.Right),
                    new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell($"{Math.Round(percent)}%", CellAlignment.Right),
                    new FixedWidthCell(x.BoostTokens.ToString()),
                    new FixedWidthCell(x.OfflineTime.Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()),
                    new FixedWidthCell(TimeSpan.FromMinutes((double)x.SiloTimeMinutes).Humanize(2, maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()),
                    new FixedWidthCell(sleeping),
                };
            }));



            var lstr = new List<string>();



            var tableString = $"```{GetTable(table)}```";

            var msgs = new List<string>();

            while(tableString.Length > 2000) {
                var index = tableString.LastIndexOf('\n', 1997);

                msgs.Add(tableString.Substring(0, index) + "```");
                tableString = "```" + tableString.Substring(index);
            }

            msgs.Add(tableString);

            return msgs;
        }

        private async Task UpdateChannel(List<string> msgs, Embed embed, ITextChannel coopChannel, Coop coop, List<IMessage> existingMessages) {
            var sw = new Stopwatch();
            sw.Restart();
            var times = new List<long>();

            msgs = msgs.Where(x => x != "").ToList();

            msgs.Insert(0, "@@@EMBED");

            //Reserve up to 5 msgs
            for(var i = msgs.Count; i < (coop.MaxUsers > 40 ? 5 : 4); i++) {
                msgs.Add("\u17B5");
            }
            if(string.IsNullOrWhiteSpace(coop.UpdateMessagesId)) {
                var UpdateMessagesID = new List<ulong>();
                foreach(var msg in msgs) {
                    IUserMessage post;
                    if(msg == "@@@EMBED") {
                        post = await coopChannel.SendMessageAsync(embed: embed);
                    } else {
                        post = await coopChannel.SendMessageAsync(msg);
                    }
                    UpdateMessagesID.Add(post.Id);
                    await post.PinAsync();
                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(UpdateMessagesID);
                try {
                    var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                    await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                } catch(TimeoutException) {
                    var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                    await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                }
            } else {
                var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);
                var NewUpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);

                if(coopChannel != null) {

                    var pinnedMessages = false;
                    for(var i = 0; i < msgs.Count(); i++) {
                        if(UpdateMessageIDs.Count > i) {
                            try {
                                var post = (RestUserMessage)existingMessages.FirstOrDefault(x => x.Id == UpdateMessageIDs[i]);
                                if(post == null) {
                                    if(msgs[i] == "@@@EMBED") {
                                        post = (RestUserMessage)await coopChannel.SendMessageAsync(embed: embed);
                                    } else {
                                        post = (RestUserMessage)await coopChannel.SendMessageAsync(msgs[i]);
                                    }
                                    NewUpdateMessageIDs.Remove(UpdateMessageIDs[i]);
                                    NewUpdateMessageIDs.Add(post.Id);
                                } else {
                                    if(msgs[i] == "@@@EMBED") {
                                        await post.ModifyWithTimeoutAsync(msg => { msg.Embed = embed; msg.Content = null; });
                                    } else {
                                        var changes = post.Content.CompareChanges(msgs[i]);
                                        if(changes > 0) {
                                            await post.ModifyWithTimeoutAsync(msg => msg.Content = msgs[i]);
                                        } else {
                                        }
                                    }
                                }
                                if(!post.IsPinned) {
                                    pinnedMessages = true;
                                    await post.PinAsync();
                                }
                            } catch(Exception e) {
                                _logger.LogError(e, "Error updating messages");
                                _bugsnag.Notify(e);
                            }
                        } else {
                            if(msgs[i] == "@@@EMBED") {
                                var post = await coopChannel.SendMessageAsync(embed: embed);
                                NewUpdateMessageIDs.Add(post.Id);
                                pinnedMessages = true;
                                await post.PinAsync();
                            } else {
                                var post = await coopChannel.SendMessageAsync(msgs[i]);
                                NewUpdateMessageIDs.Add(post.Id);
                                pinnedMessages = true;
                                await post.PinAsync();
                            }
                        }

                    }
                    if(pinnedMessages) {
                        try {
                            var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                            await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                        } catch(TimeoutException) {
                            var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                            await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                        }
                    }

                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(NewUpdateMessageIDs);
            }
        }

        private class StatusResponse {
            public Ei.ContractCoopStatusResponse Status { get; set; }
            public List<IMessage> DiscordMessages { get; set; }
        }

        private async Task<List<IMessage>> GetDiscordMessages(ITextChannel coopChannel, Coop coop, CancellationToken cancellationToken) {
            var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId ?? "[]");

            IEnumerable<IMessage> discordMessages;
            try {

                discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : new List<IMessage>();
            } catch(Exception) {
                try {
                    await Task.Delay(100);
                    discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : new List<IMessage>();

                } catch(Exception) {
                    await Task.Delay(100);
                    discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : new List<IMessage>();
                }
            }

            var messages = new List<IMessage>();
            foreach(var id in UpdateMessageIDs) {
                var message = discordMessages.FirstOrDefault(x => x.Id == id);
                if(message == null) {
                    for(var i = 0; i < 10; i++) {
                        try {
                            message = await coopChannel.GetMessageAsync(id, options: new RequestOptions { CancelToken = cancellationToken });
                            break;
                        } catch(Exception) {
                            await Task.Delay(500);
                        }
                    }
                    if(message == null) {
                        message = await coopChannel.GetMessageAsync(id, options: new RequestOptions { CancelToken = cancellationToken });
                    }
                }
                if(message != null)
                    messages.Add(message);
            }

            return messages;
        }

        private async Task<StatusResponse> GetStatus(Coop coop, ITextChannel channel, CancellationToken cancellationToken) {
            var statusTask = ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name, cancellationToken);
            var messageTask = GetDiscordMessages(channel, coop, cancellationToken);


            await Task.WhenAll(statusTask, messageTask);
            if(statusTask.Result is null) {

            }
            return new StatusResponse {
                Status = statusTask.Result,
                DiscordMessages = messageTask.Result
            };
        }

        public async Task ProcessCoop(Guid coopid, SocketGuild guild, List<UserWithBackup> users, Guild dbguild, CancellationToken cancellationToken, ApplicationDbContext db) {
            var timings = new TimingsFactory(null);
            timings.Start();
            string coopName = null;
            try {
                using(var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>()) {

                    //** Get Coop
                    var coop = await _db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).FirstOrDefaultAsync(x => x.Id == coopid);
                    if(coop == null) {
                        _logger.LogWarning("Unable to find co-op with id {coopid}", coopid);
                        return;
                    }


                    //** Get Coop Thread
                    ITextChannel coopChannel = guild.TextChannels.FirstOrDefault(x => x.Id == coop.DiscordChannelId);

                    if(coopChannel == null) {
                        var restguild = await _client.Rest.GetGuildAsync(guild.Id);
                        try {
                            coopChannel = await restguild.GetTextChannelAsync(coop.DiscordChannelId);
                        } catch(Exception) {
                        }
                    }

                    if(coopChannel == null) {
                        _logger.LogWarning("ERROR FINDING CHANNEL FOR CO-OP: {coopName}", coop.Name);
                        return;
                    }


                    //** Send Co-op has been created DM
                    foreach(var xref in coop.UserCoopsXrefs) {
                        var user = users.FirstOrDefault(x => x.User.Id == xref.UserId);
                        if(xref.CoopSetting is null && user is not null) {
                            xref.CoopSetting = new CoopSetting(xref, user.User);
                            if(xref.CoopSetting.PingOnCoopCreated) {
                                await SendDMWarning(db, guild.GetUser(user.User.DiscordId), coopChannel, "Co-op has been created: ", coop);
                                xref.CoopSetting.PingOnCoopCreated = false;
                            }
                            xref.UpdateCoopSetting();
                        }
                    }
                    await _db.SaveChangesAsync();



                    List<IGuildUser> coopDiscordUsers = coopChannel is SocketTextChannel ? ((SocketTextChannel)coopChannel).Users.ToList().Select(x => (IGuildUser)x).ToList() : (await coopChannel.GetUsersAsync().FlattenAsync()).ToList();


                    timings.Set("Start");

                    var statusReponse = await GetStatus(coop, coopChannel, cancellationToken);

                    timings.Set("Got status");


                    //** Handle coop bot being started
                    if(statusReponse.Status is null || statusReponse.Status.ResponseStatus == Ei.ContractCoopStatusResponse.Types.ResponseStatus.CoopNotFound) {
                        var messages = await (coopChannel as SocketTextChannel).GetMessagesAsync().FlattenAsync();
                        if(messages.Where(x => x.Author.IsBot).Count() == 0) {
                            _logger.LogCritical("Status is null and there are no channel messages for co-op: {coopName}, attempting to start.", coop.Name);
                            string EIID = null;
                            var random = new Random();
                            foreach(var account in coop.UserCoopsXrefs.OrderBy(x => random.Next())) {
                                var r = await ContractsAPI.Post<Ei.ContractPlayerInfo, Ei.BasicRequestInfo>(new Ei.BasicRequestInfo(), account.EggIncId);
                                if(r.Grade == (Ei.Contract.Types.PlayerGrade)coop.League) {
                                    EIID = account.EggIncId;
                                    break;
                                }
                            }

                            var result = await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (Ei.Contract.Types.PlayerGrade)coop.League, coop, coop.Contract.Details.LengthSeconds, EIID, coop.AnyLeague);
                        } else {
                            _logger.LogWarning("Status is null for co-op: {coopName}", coop.Name);
                        }

                        return;
                    }

                    var status = statusReponse.Status;

                    if(coop.League != (uint)status.Grade) {
                        _logger.LogInformation("Updating co-op league: {coopName} from {oldLeague} to {newLeague}", coop.Name, (Ei.Contract.Types.PlayerGrade)coop.League, status.Grade);
                        coop.League = (uint)status.Grade;
                    }
                    if(coop.League == 0) {
                        _logger.LogWarning("{coopName} is returning Grade as 0", coopName);
                        return;
                    } else if(status.SecondsRemaining == coop.Contract.Details.GradeSpecs[(int)coop.League - 1].LengthSeconds) {
                        //Attempt to fix not started co-op
                        _logger.LogInformation("Attempting to start co-op: {coopName}", coop.Name);

                        var joinResponse = await ContractsAPI.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest {
                            ContractIdentifier = coop.ContractID,
                            CoopIdentifier = coop.Name.ToLower(),
                            UserId = coop.CreatorID, ClientVersion = ContractsAPI.ClientVersion, Eop = 1, SoulPower = 24, Grade = (Ei.Contract.Types.PlayerGrade)coop.League, Platform = Aux.Platform.Droid, SecondsRemaining = coop.Contract.Details.LengthSeconds, PointsReplay = false, UserName = "."
                        }, coop.CreatorID, false);


                        var statusUpdate = new Ei.ContractCoopStatusUpdateRequest {
                            ContractIdentifier = coop.ContractID,
                            CoopIdentifier = coop.Name.ToLower(),
                            Eop = 1, SoulPower = 24, UserId = coop.CreatorID, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = coop.CreatorID, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                            ProductionParams = new Ei.FarmProductionParams {
                                FarmPopulation = 1, Delivered = 1, Elr = 1, FarmCapacity = 1, Ihr = 1, Sr = 1
                            }
                        };

                        var response = await ContractsAPI.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(statusUpdate, statusUpdate.UserId, false);


                        await Task.Delay(1000);
                        var checkStatus = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name.ToLower(), cancellationToken, coop.CreatorID);


                        var kickPlayer = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
                            ClientVersion = ContractsAPI.ClientVersion,
                            ContractIdentifier = coop.ContractID,
                            CoopIdentifier = coop.Name.ToLower(),
                            PlayerIdentifier = coop.CreatorID,
                            Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                            RequestingUserId = coop.CreatorID
                        }, coop.CreatorID);
                    }

                    var finalChannelUpdate = false;

                    if(cancellationToken.IsCancellationRequested) return;


                    if(coop.League == 0) {
                        //Fix if grade is set to 0
                        coop.League = (uint)status.Grade;
                    }

                    var coopDetails = new CoopDetails(coop, coop.Contract, coop.League, users, _client, statusReponse.Status);


                    var participantsInCoopButWithoutXref = coopDetails.CoopParticipants.Where(x =>
                        x.DBUser is not null &&
                        x.Xref is null &&
                        x.CoopStatus is not null &&
                        x.Backup.Farms.Any(f => f.CoopId is not null && f.CoopId.Equals(coop.Name, StringComparison.CurrentCultureIgnoreCase))
                    ).ToList();
                    foreach(var participant in participantsInCoopButWithoutXref) {
                        var xref = new UserCoopXref {
                            EggIncId = participant.Backup.EggIncId,
                            CreatedOn = DateTimeOffset.Now,
                            JoinedCoop = true,
                            UserId = participant.DBUser.Id,
                            WasAssigned = false,
                            CoopId = coop.Id
                        };
                        _db.Add(xref);
                        participant.AddXref(xref);
                        await _db.SaveChangesAsync();
                        await coopChannel.SendMessageAsync($"<@{participant.DBUser.DiscordId}> has joined the co-op");
                    }



                    timings.Set(1);


                    var usersWithStatus = coopDetails.CoopParticipants.Select(x => new UserWithStatus {
                        Status = x.CoopStatus,
                        Xref = x.Xref,
                        User = x.DBUser,
                        Backup = x.Backup,
                        DiscordUser = x.DBUser is not null ? guild.GetUser(x.DBUser.DiscordId) : null,
                    }).ToList();


                    await CheckDeflectorChange(coop.LastStatusUpdate, status, coop, usersWithStatus, coopChannel, db);
                    await CheckOnCoopFullError(usersWithStatus, coop, status, coop.Contract, coopChannel);

                    timings.Set("1.1");
                    var usersNotJoined = coopDetails.CoopParticipants.Where(x => x.CoopStatus is null).ToList();

                    foreach(var user in usersWithStatus) {
                        if(user.Backup != null) {
                            var awayTime = Research.GetTotalSiloCapacity(user.Backup);
                            var farm = user.Backup?.Farms?.FirstOrDefault(x => x.CoopId == coop.Name.ToLower());
                            if(farm != null) {
                                user.FarmStats = farm.WithStats(user.Backup, coop);
                                user.SiloTime = awayTime * farm.SilosOwned;
                                var siloTimeHours = user.SiloTime / 60;
                                if(user.Xref is not null && user.Xref.SiloTimeHours != siloTimeHours) {
                                    user.Xref.SiloTimeHours = (float)siloTimeHours;
                                    await _db.SaveChangesAsync();
                                }
                            }
                        }

                        if(user.Xref != null) {
                            user.Xref.LastStatus = user.Status is not null ? new ContributionInfoCompact(user.Status) : null;
                        }

                    }


                    timings.Set(2);


                    //Handle User Joining Without Xref
                    var usersWithoutXref = coopDetails.CoopParticipants.Where(x => x.DBUser is not null && x.Xref is null);
                    List<ulong> usersNeedingChannelPermissions = new();
                    foreach(var user in usersWithoutXref) {
                        var channeluser = coopDiscordUsers.FirstOrDefault(x => x.Id == user.DBUser.DiscordId); // await coopChannel.GetUserAsync(dbuser.DiscordId);
                        if(channeluser == null) {
                            usersNeedingChannelPermissions.Add(user.DBUser.DiscordId);
                            //try {
                            //    var discorduser = guild.Users.FirstOrDefault(x => x.Id == user.DBUser.DiscordId);
                            //    if(discorduser != null) {
                            //        await coopChannel.AddPermissionOverwriteAsync(discorduser, new OverwritePermissions(viewChannel: PermValue.Allow));
                            //        channeluser = discorduser;
                            //        _logger.LogInformation("Added Permission for {user} in {coop}", user.DBUser.DiscordUsername, coop.Name);
                            //    }
                            //} catch {
                            //    _logger.LogWarning("Error Adding Permission for {user} in {coop}", user.DBUser.DiscordUsername, coop.Name);
                            //}
                        }
                        if(channeluser != null) {
                            var xref = new UserCoopXref {
                                WaitingOnStarter = false,
                                UserId = user.DBUser.Id,
                                EggIncId = user.Backup.EggIncId,
                                AddedToChannel = true,
                                CoopId = coop.Id,
                                CreatedOn = DateTimeOffset.UtcNow,
                                JoinedCoop = true,
                                //LastStatusTime = lastStatus?.CreatedOn ?? DateTimeOffset.UtcNow,
                                Starter = false,
                                LastStatus = user.CoopStatus is not null ? new ContributionInfoCompact(user.CoopStatus) : null,
                                WasAssigned = false
                            };
                            _db.UserCoopXrefs.Add(xref);
                        }
                    }


                    timings.Set(3);

                    foreach(var participant in coopDetails.CoopParticipants) {
                        await HandleSleeping(participant, coopChannel, coop, _db, dbguild, guild);
                    }

                    var league = (int?)coop.League ?? 0;
                    var targetAmount = coop.Contract.Details.GetGoals(league).Max(x => x.TargetAmount);
                    var amountWithOffline = coopDetails.CoopParticipants.Where(x => x.CoopStatus is not null).Sum(x => x.EggsShipped + x.OfflineEggs);
                    var remainingAmount = targetAmount - amountWithOffline;
                    var totalRate = status.Participants.Sum(x => x.ContributionRate);

                    var timeRemaining = GetTimeRemainingValue(targetAmount, totalRate, amountWithOffline);



                    //var hasDuplicate = status.Contributors.Count > coop.Contract.MaxUsers;
                    if(!coop.FinishedOrFailed()) {
                        await CheckHighestEBJoined(coop, usersWithStatus, coopDetails, coopChannel, _db, usersNotJoined);

                        if(!coop.ProjectedToFinish && coopDetails.PercentProjectedForJoined >= 100 && coop.CoopEnds > DateTimeOffset.Now) {
                            coop.ProjectedToFinish = true;
                            await coopChannel.SendMessageAsync($"Coop {coop.Name} is now projected to finish!");
                            try {
                                await _db.SaveChangesAsync();
                            } catch(Exception) {
                                await Task.Delay(100);
                                try {
                                    await _db.SaveChangesAsync();
                                } catch(Exception) { }
                            }
                        }

                        if(status.SecondsRemaining > 1 && coop.ProjectedToFinish && coopDetails.PercentProjectedForJoined < 100 && coop.CoopEnds > DateTimeOffset.Now) {
                            coop.ProjectedToFinish = false;
                            await coopChannel.SendMessageAsync($"Coop {coop.Name} is **no longer** projected to finish.");
                            try {
                                await _db.SaveChangesAsync();
                            } catch(Exception) {
                                await Task.Delay(100);
                                try {
                                    await _db.SaveChangesAsync();
                                } catch(Exception) { }
                            }
                        }


                        if(!coop.Finished && status.Finished()) {
                            coop.Finished = true;
                            coop.CoopCompleted = DateTimeOffset.UtcNow;
                            coop.Status = CoopStatusEnum.Completed;
                            finalChannelUpdate = true;

                            await _db.SaveChangesAsync();
                            await coopChannel.SendMessageAsync($"Coop {coop.Name} is finished!");

                            var finishedCoopCategories = await _client.GetAllFinishedCategories(guild);
                            foreach(var category in finishedCoopCategories) {
                                var channelCount = guild.TextChannels.Count(x => x.CategoryId == category.Id);
                                if(channelCount < 50) {
                                    try {
                                        await coopChannel.ModifyAsync(x => { x.CategoryId = category.Id; });
                                        break;
                                    } catch(Exception) {
                                        _logger.LogWarning("Error setting category");
                                    }
                                }
                            }

                            await HandleUnjoins(usersNotJoined, guild, users, dbguild, coop, _db, coopChannel);
                        }

                        if(coop.Finished && coop.Status != CoopStatusEnum.Completed) {
                            coop.Finished = true;
                            coop.Status = CoopStatusEnum.Completed;
                            finalChannelUpdate = true;
                            try {
                                await _db.SaveChangesAsync();
                            } catch(Exception) {
                                await Task.Delay(100);
                                try {
                                    await _db.SaveChangesAsync();
                                } catch(Exception) { }
                            }
                        }
                    }


                    timings.Set(4);


                    if(coop.CurrentUsers != status.Contributors.Count) {
                        var hadDuplicate = coop.CurrentUsers > coop.MaxUsers;
                        coop.CurrentUsers = status.Contributors.Count;
                        coop.MaxUsers = coop.Contract.MaxUsers;
                    }


                    var msgs = GetStatusStringAsync(coopDetails, coop.Contract);
                    var lastMessage = "";

                    timings.Set(5);

                    foreach(var userStatus in coopDetails.CoopParticipants.Where(x => x.Xref != null)) {
                        if(userStatus.DiscordUser is not null && !coopChannel.PermissionOverwrites.Any(x => x.TargetId == userStatus.DiscordUser.Id)) {
                            usersNeedingChannelPermissions.Add(userStatus.DiscordUser.Id);
                            //try {
                            //    await coopChannel.AddPermissionOverwriteAsync(userStatus.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
                            //    userStatus.Xref.AddedToChannel = true;
                            //    _logger.LogInformation("Adding user to channel {user}", userStatus.DiscordUser.DisplayName);
                            //} catch(Exception e) {
                            //    _logger.LogWarning("Unable able to add {user} to {coop} in {server} ({error})", userStatus.DiscordUser.DisplayName, coop.Name, guild.Name, e.Message);
                            //}
                        }

                        if(!userStatus.Xref.JoinedCoop && userStatus.CoopStatus is not null) {
                            userStatus.Xref.JoinedCoop = true;
                            var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
                            if(unjoinedRole != null) {
                                await userStatus.DiscordUser.RemoveRoleAsync(unjoinedRole);
                            }
                            await _db.SaveChangesAsync();
                        }
                    }

                    var usersAdded = await _apiLink.AddUsersToChannel(
                        new EGG9000.Common.SharedModels.CoopPermissions {
                            ChannelId = coopChannel.Id, 
                            GuildId = coopChannel.GuildId, 
                            UserIds = usersNeedingChannelPermissions
                        }
                    );
                    foreach(var userAdded in usersAdded) {
                        var xref = coopDetails.CoopParticipants.FirstOrDefault(x => x.DiscordUser?.Id == userAdded);
                        if(xref != null) {
                            xref.Xref.AddedToChannel = true;
                        }
                    }
                    if(usersAdded.Count > 0) {
                        await _db.SaveChangesAsync();
                    }


                    //Handle waiting on assigned
                    var missingFromServer = false;



                    if(usersNotJoined.Count == 0 && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                        coop.Status = CoopStatusEnum.AllAssignedJoined;
                    } else {
                        var userList = new List<string>();
                        foreach(var userFarmDetails in usersNotJoined) {
                            var xref = userFarmDetails.Xref;
                            try {
                                var user = users.FirstOrDefault(x => x.User.Id == xref.GetID())?.User;

                                if(user == null) {
                                    user = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == xref.UserId);
                                }

                                var discordUser = user == null ? null : guild.GetUser(user.DiscordId);

                                var mention = "";

                                if(discordUser == null) {
                                    mention = $"{user.DiscordUsername} (Missing from server)";
                                    missingFromServer = true;
                                } else if(user.EggIncAccounts.Count > 1) {
                                    var eggaccount = user.EggIncAccounts.FirstOrDefault(x => x.Id == xref.EggIncId);
                                    if(eggaccount != null)
                                        mention = $"{discordUser.Mention} ({eggaccount.Backup?.UserName ?? "No Name"})";
                                } else {
                                    mention = discordUser?.Mention;
                                }

                                if(userFarmDetails.Account is not null || userFarmDetails.Backup is not null) {
                                    var grade = userFarmDetails.Account?.GetGrade() ?? userFarmDetails.Backup.Grade;
                                    if((uint)grade != coop.League && !(coop.Contract.cc_only || coop.AnyLeague)) {
                                        mention += $" (Wrong {grade})";
                                    }
                                }

                                userList.Add(mention);

                                if(discordUser != null && !coop.Finished && coop.Status != CoopStatusEnum.Failed && coop.CoopEnds > DateTimeOffset.Now) {
                                    if(!xref.JoinWarning24TillFinish && timeRemaining.TotalHours < 24 && xref.CreatedOn < DateTimeOffset.Now.AddHours(-1)) {
                                        xref.JoinWarning24TillFinish = true;
                                        await _db.SaveChangesAsync();
                                        await SendDMWarning(db, discordUser, coopChannel, $"reminder to join - co-op will be finished in under {Math.Ceiling(timeRemaining.TotalHours)} hours", coop);
                                    } else if(!xref.JoinWarning24h && xref.CreatedOn < DateTimeOffset.Now.AddHours(-24)) {
                                        xref.JoinWarning24h = true;
                                        xref.JoinWarning12h = true;
                                        await _db.SaveChangesAsync();
                                        await SendDMWarning(db, discordUser, coopChannel, $"reminder to join - 24h since added to co-op", coop);
                                    } else if(!xref.JoinWarning12h && xref.CreatedOn < DateTimeOffset.Now.AddHours(-12)) {
                                        xref.JoinWarning12h = true;
                                        await _db.SaveChangesAsync();
                                        await SendDMWarning(db, discordUser, coopChannel, $"reminder to join - 12h since added to co-op", coop);
                                    }


                                    var hoursToKick = coop.Contract.cc_only ? 24 : 18;
                                    if(xref.CreatedOn < DateTimeOffset.Now.AddHours(-hoursToKick)) {
                                        var accountName = userFarmDetails.DBUser.EggIncAccounts.Count > 1 ? $" ({userFarmDetails.DBUser.EggIncAccounts.Where(a => a.Id == xref.EggIncId).FirstOrDefault().Backup?.UserName})" : "";
                                        await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name} within {hoursToKick} hours{accountName}, you have been removed from the co-op and your space might be filled.", user, _db, xref, discordUser, coopChannel, dbguild, coop, false);
                                    }
                                }

                                if(!xref.OutsideCoop && coop.GuildId == _CPGuildId && !coop.FinishedOrFailedOrExpired() && userFarmDetails.Farm is not null) {
                                    var farm = userFarmDetails.Farm;
                                    if(farm.CoopId.Equals(coop.Name, StringComparison.OrdinalIgnoreCase)) {
                                        await coopChannel.SendMessageAsync($"{discordUser?.Mention ?? user.DiscordUsername}, it looks like your game thinks you have joined the co-op but the game's servers don't see you in the co-op. Please check with the other members of the co-op to verify they don't see you, if they don't then you will need to restart the contract and join again. After you do make sure the bot can see you in the co-op.");
                                        xref.OutsideCoop = true;
                                        await _db.SaveChangesAsync();
                                    } else if(farm.CoopId.Length > 0 && farm.FarmType == Ei.FarmType.Contract) {
                                        var message = $"It looks like {discordUser?.Mention ?? user.DiscordUsername} has joined another co-op named {farm.CoopId}.";
                                        await coopChannel.SendMessageAsync(message);
                                        xref.OutsideCoop = true;
                                        var logMessage = $"Outside co-op detected for {discordUser?.Mention ?? user.DiscordUsername} they joined *{farm.CoopId}*, but were assigned to <#{coopChannel.Id}>";
                                        var findGuild = await _db.Guilds.FirstOrDefaultAsync(g => g.Id == guild.Id || g.OverflowServersJson.Contains(guild.Id.ToString()));
                                        var findSocketGuild = _client.Guilds.FirstOrDefault(g => g.Id == findGuild.Id);
                                        var response = ChannelHelper.DetermineAndSend(db, _client, findGuild, findSocketGuild, GuildChannelType.OutsideCoopLog, new() { Text = logMessage });
                                        await _db.SaveChangesAsync();
                                    }
                                }
                            } catch(Exception) { }
                        }
                        lastMessage += $"Coop **{coop.Name}** is ready for the following to join: {string.Join(", ", userList)}\n";
                    }


                    //var usersAssigned = coop.UserCoopsXrefs.Select(x => {
                    //    var User = users.FirstOrDefault(y => y.Id == x.GetID());
                    //    if(User == null)
                    //        return null;
                    //    var backup = User.Backups?.FirstOrDefault(y => y?.EggIncId == x.EggIncId);
                    //    if(backup == null)
                    //        return null;
                    //    return new {
                    //        User = User,w
                    //        Backup = User.Backups?.First(y => y.EggIncId == x.EggIncId)
                    //    };
                    //}).Where(x => x != null);


                    var giftInfos = usersWithStatus.Where(x => x.Status is not null && x.Status.FarmInfo is not null && x.FarmStats is not null).Select(x => new {
                        Shipping = x.Status.ContributionRate / x.FarmStats.MaxShippingRate * 100,
                        Habs = x.Status.ProductionParams.FarmPopulation / x.Status.ProductionParams.FarmCapacity * 100,
                        x.Status.UserName,
                        x.Status.ProductionParams.FarmPopulation
                    });

                    var personToGiftTo = giftInfos
                        .Where(x =>
                            x.Shipping < 97 &&
                            x.Habs < 97
                        )
                        .OrderByDescending(x => x.FarmPopulation).Take(10);
                    if(personToGiftTo.Count() > 0) {
                        var table = new List<List<FixedWidthCell>>();
                        table.Add(new List<FixedWidthCell> {
                                new FixedWidthCell(""),
                                new FixedWidthCell($"🐔", CellAlignment.Center),
                                new FixedWidthCell($"🏠", CellAlignment.Center),
                                new FixedWidthCell($"🚚", CellAlignment.Center),
                            });
                        table.AddRange(personToGiftTo.Select(x => new List<FixedWidthCell> {
                                new FixedWidthCell(Truncate(x.UserName, 11)),
                                new FixedWidthCell($"{x.FarmPopulation.ToEggString()}", CellAlignment.Right),
                                new FixedWidthCell($"{Math.Round(x.Habs)}%", CellAlignment.Right),
                                new FixedWidthCell($"{Math.Round(x.Shipping)}%", CellAlignment.Right),
                            }).ToList());
                        lastMessage += $"\nFarms that would benefit from gifting chickens: \n```{string.Join("\n", GetTable(table))}```\n\n";
                    } else if(coopDetails.CoopParticipants.Any(y => y.CoopStatus is not null && y.FarmStats is not null)) {
                        lastMessage += "\nLooks like everyone's shipping and/or habs are full or they haven't joined yet, so gifting chickens isn't useful.\n\n";
                    }

                    //New commands list, each is a quick-link to start using the command
                    lastMessage += "__Co-op Commands (click to use):__\n";

                    var slashCommands = (await guild.GetApplicationCommandsAsync()).ToList().Where(c => c.Type == ApplicationCommandType.Slash).ToList();
                    if(_client.GetChannelAsync(GuildChannelType.CallStaffChannel, guild) != null) {
                        lastMessage += $"\n</callstaff:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "callstaff")?.Id ?? 0}> Use this command if you joined a co-op for the wrong contract, or have other questions or concerns";
                    }
                    lastMessage += $"\n</coopsettings:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "coopsettings")?.Id ?? 0}> Receive DM pings for various events in the co-op";
                    lastMessage += $"\n</fixfullcooperror:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "fixfullcooperror")?.Id ?? 0}> If you get the error co-op is full, try running this command to free up the space.";



                    var userWithDifferentGrade = usersWithStatus.FirstOrDefault(x => x.Backup is not null && x.Backup.Farms.Any(y => y.CoopId is not null && y.CoopId.Equals(coop.Name, StringComparison.CurrentCultureIgnoreCase) && (uint)y.Grade != coop.League));
                    if(!coop.FinishedOrFailed() && userWithDifferentGrade is not null) {
                        var farm = userWithDifferentGrade.Backup.Farms.FirstOrDefault(x => x.CoopId is not null && x.CoopId.ToLower() == coop.Name.ToLower());
                        lastMessage += $" Warning! Looks like this co-op is the wrong grade and is actually {farm.Grade}";
                    }

                    var waitingOn = usersWithStatus.Where(x => !x.Status?.Finalized ?? false);
                    if(status.AllGoalsAchieved && status.Participants.Any(y => !y.Finalized)) {
                        lastMessage += $"\n\nWaiting on the following users to check-in: {string.Join(", ", waitingOn.Select(x => x.DiscordUser?.Mention ?? x.Status.UserName))}";
                    }

                    //Checking if users are gusset glitching
                    var afCheaterChannel = ChannelHelper.DetermineChannelType(dbguild, guild, GuildChannelType.CheaterThread);
                    if(afCheaterChannel != null) {
                        var contractScalar = coop.Contract.Details?.GradeSpecs[((int)coop.League) - 1]?.Modifiers?.FirstOrDefault(m => m.Dimension == Ei.GameModifier.Types.GameDimension.HabCapacity)?.Value ?? 1;
                        foreach(var u in usersWithStatus.Where(x => x.Xref is not null && !x.Xref.GussetCheatDetected)) {
                            var farm = u.Backup.Farms.FirstOrDefault(x => x.CoopId is not null && x.CoopId.ToLower() == coop.Name.ToLower());
                            if(farm is null) continue;
                            /* AFFECT ALL HABS */
                            double allScalar = 1;
                            allScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "hab_capacity1")?.Level * 0.05 ?? 0); //5% per level
                            allScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "microlux")?.Level * 0.05 ?? 0); //5% per level
                            allScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "grav_plating")?.Level * 0.02 ?? 0); //2% per level
                            allScalar *= contractScalar; // Indeterminate before runtime

                            /* AFFECT PORTAL HABS */
                            double portalScalar = 1;
                            portalScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "wormhole_dampening")?.Level * 0.02 ?? 0); //2% per level

                            var currentChickens = farm.NumChickens;
                            var scaledMaxChickens = EggIncHabSpace.GetScaledHabSpace(farm, allScalar, portalScalar) + 0.01; //0.01 offset, again for rounding

                            //If they aren't surpassing the scaled limit, they aren't cheating
                            if(currentChickens <= (scaledMaxChickens * 1.01)) continue; //1% offset for rounding errors

                            var gusset = farm.Artifacts.FirstOrDefault(a => a.Artifact.ToLower().Contains("gusset"));
                            if(gusset is null) {
                                await ChannelHelper.DetermineAndSend(db, _client, dbguild, guild, GuildChannelType.CheaterThread, 
                                    new() { Text = $"User <@{u.User.DiscordId}> ({u.Backup?.UserName ?? "_No Username_"}) may have glitched to remove a gusset after boosting, in the coop <#{coop.DiscordChannelId}> (`{coop.Name}`):\n" +
                                    $"```\nMax hab space:\t   {(ulong)scaledMaxChickens:n0}\nCurrent chickens:\t{currentChickens:n0}\n```" });
                                u.Xref.GussetCheatDetected = true;
                            }
                        }
                        foreach(var u in usersWithStatus.Where(u => u.Status is not null && u.Status.TimeCheatDetected && u.Xref is not null && !u.Xref.TimeCheatReported).ToList()) {
                            await ChannelHelper.DetermineAndSend(db, _client, dbguild, guild, GuildChannelType.CheaterThread,
                                new() { Text = $"Time cheat detected for <@{u.User.DiscordId}> ({u.Backup?.UserName ?? "_No Username_"}) in the coop <#{coop.DiscordChannelId}> (`{coop.Name}`)"});
                            u.Xref.TimeCheatReported = true; //Set the flag to prevent repetition
                        }
                    }


                    foreach(var u in usersWithStatus.Where(x => x.Xref is not null)) {
                        u.Xref.HasTachyonDeflector = u.Xref.HasTachyonDeflector || (u.Backup?.GetAvailableArtifacts().Any(a => a.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) ?? false);
                        var farm = u.Backup?.Farms.FirstOrDefault(x => x.ContractId == coop.ContractID);
                        if(farm == null)
                            continue;
                        u.Xref.EquipedTachyonDeflector = u.Xref.EquipedTachyonDeflector || farm.Artifacts.Any(a => a.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates);
                    }

                    var usersToCheckDeflector = usersWithStatus.Where(x => x.Status is not null && !x.Status.BuffHistory.Any(y => y.EggLayingRate > 0) && x.Backup is not null && x.Backup.ArtifactHall is not null && x.Status.Projected < usersWithStatus.Where(y => y.Status is not null).Max(y => y.Status.Projected) / 2);
                    var usersNeedToAddDeflector = new List<UserWithStatus>();
                    if(!coop.FinishedOrFailed() && coop.CoopEnds > DateTimeOffset.Now) {
                        foreach(var user in usersToCheckDeflector) {
                            var farm = user.Backup.Farms.FirstOrDefault(x => x.ContractId == coop.ContractID);
                            if(farm is not null && !farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) && user.Backup.GetAvailableArtifacts().Any(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)) {
                                usersNeedToAddDeflector.Add(user);
                            }
                        }
                    }



                    if(usersNeedToAddDeflector.Any()) {
                        lastMessage += $"\n\n**The following users have a Tachyon Deflector they should equip:** {string.Join(", ", usersNeedToAddDeflector.Select(y => y.DiscordUser?.Mention ?? $"<@{y.User?.DiscordId}>"))}";
                    }


                    if(status.Contributors.Count == coop.MaxUsers && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                        coop.Status = CoopStatusEnum.Full;
                    }

                    if(coop.Status != CoopStatusEnum.Failed && status.Failed()) {
                        if(coop.Contract.GoodUntil > DateTimeOffset.UtcNow) {
                            await coopChannel.SendMessageAsync($"Co-op {coop.Name} failed to reach all the goals and the contract is still available for {(coop.Contract.GoodUntil - DateTimeOffset.UtcNow).Humanize()} if you want to restart and try again.");
                        } else {
                            await coopChannel.SendMessageAsync($"Co-op {coop.Name} failed to reach all the goals and the contract is no longer available.");
                        }
                        coop.Status = CoopStatusEnum.Failed;
                        finalChannelUpdate = true;
                        await _db.SaveChangesAsync();

                        try {
                            var coopFailedCategory = await _client.GetCategoryAsync(GuildChannelType.FailedCategory, guild);
                            if(coopFailedCategory is null)
                                coopFailedCategory = _client.GetGuild(coop.OverflowGuildId).CategoryChannels.Where(x => x.Name != null).FirstOrDefault(x => x.Name.ToLower().Contains("failed") && x.Name.ToLower().Contains("coops"));
                            if(coopFailedCategory is null)
                                coopFailedCategory = _client.GetGuild(coop.OverflowGuildId).CategoryChannels.Where(x => x.Name != null).FirstOrDefault(x => x.Name.ToLower().Contains("finished") && x.Name.ToLower().Contains("coops"));
                            await coopChannel.ModifyAsync(x => { x.CategoryId = coopFailedCategory.Id; });
                        } catch(Exception) {

                        }

                        await HandleUnjoins(usersNotJoined, guild, users, dbguild, coop, _db, coopChannel);

                    }

                    timings.Set(6);


                    var emojis = "";




                    var missingCount = coopDetails.CoopParticipants.Count(x => x.Xref is not null && x.CoopStatus is null);

                    if(missingCount == 0) {
                        await HandlePingOnFull(db, coopDetails.CoopParticipants, coopChannel);
                    }

                    if(status.ClearedForExit) {
                        await HandlePingOnCheckedIn(db, coopDetails.CoopParticipants, coopChannel);
                    }

                    if(coop.FinishedOrFailed()) {
                        await HandleFinished(db, coopDetails.CoopParticipants, coopChannel);
                    }




                    timings.Set(6);




                    coop.LastStatusUpdate = status;


                    if(!coop.FinishedOrFailed() || finalChannelUpdate) {
                        if(missingCount > 0) {
                            if(missingCount <= 20) {
                                emojis += Convert.ToChar(9311 + missingCount);
                            } else if(missingCount <= 35) {
                                emojis += Convert.ToChar(12881 + (missingCount - 21));
                            } else if(missingCount <= 50) {
                                emojis += Convert.ToChar(12977 + (missingCount - 36));
                            } else {
                                emojis += "❌";
                            }


                            if(
                                !coop.Finished && (
                                    timeRemaining.TotalHours < 24
                                    || status.SecondsRemaining > 0 && status.SecondsRemaining < TimeSpan.FromHours(24).TotalSeconds
                                )
                            ) {
                                emojis += "🔺";
                            }
                        } else if(
                                !coop.FinishedOrFailed() && (
                                    timeRemaining.TotalHours < 3
                                    || status.SecondsRemaining > 0 && status.SecondsRemaining < TimeSpan.FromHours(6).TotalSeconds
                                ) && (coop.LastStatusUpdate?.Participants.Count ?? 0) < coop.Contract.Details.MaxCoopSize && !status.Public
                            ) {
                            emojis += "🔘";
                        }

                        var color = Color.DarkGrey;
                        if(coop.Status == CoopStatusEnum.Failed) {
                            emojis += "🚩";
                        } else if(coop.Finished) {
                            emojis += "🏁";
                        } else {

                            var percent = coopDetails.PercentProjectedForJoined;

                            if(percent < 60) {
                                color = Color.Red;
                                emojis += "🔴";
                            } else if(percent < 90) {
                                color = new Color(139, 69, 19);
                                emojis += "🤎";
                            } else if(percent < 100) {
                                color = Color.Orange;
                                emojis += "🧡";
                            } else if(percent < 105) {
                                color = new Color(255, 255, 0);
                                emojis += "💛";
                            } else {
                                color = Color.Green;
                                emojis += "💚";
                            }

                            if(percent < 100 && coopDetails.PercentProjected >= 100) {
                                emojis += "💹";
                            }
                        }

                        if(missingFromServer) {
                            emojis += "👻";
                        }

                        if(coopDetails.CoopParticipants.Any(x => x.Xref is null) && !status.Public && !coop.Finished) {
                            emojis += "👽";
                        }

                        if(coopDetails.CoopParticipants.Count > coop.Contract.MaxUsers) {
                            emojis += "🤢";
                        }

                        var coopname = emojis + coop.Name.ToLower();
                        if(coopChannel.Name != coopname) {
                            for(var i = 0; i < 5; i++) {
                                try {
                                    await coopChannel.ModifyAsync(x => x.Name = coopname);
                                    break;
                                } catch(Exception) {
                                    await Task.Delay(new Random().Next(500));
                                }
                            }
                        }



                        if(lastMessage != "")
                            msgs.AddRange(DiscordMessageSplitter.SplitMessage(lastMessage, "\n"));

                        var gradeMessage = $"**Co-op Grade**: {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)(int)coop.League)}{(coop.AnyLeague ? " (<:ultra:1131045418319495369> **Any-Grade**)" : "")}";

                        var highestEB = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                        var highestEBMessage = "";
                        if(highestEB != null)
                            highestEBMessage = $"**\nHighest EB**: {highestEB.DBUser.DiscordUsername} at {highestEB.Backup.EarningsBonus.ToEggString()} {(usersNotJoined.Any(x => x?.EggIncId == highestEB.Backup.EggIncId) ? "has not joined yet." : "**has joined!**")}";

                        var createdByMessage = "";
                        if(!string.IsNullOrEmpty(coop.CreatorID)) {
                            var creator = users.FirstOrDefault(x => x.Backup?.EggIncId == coop.CreatorID);
                            if(creator != null) {
                                var account = creator.User.EggIncAccounts.First(x => x.Id == coop.CreatorID);
                                createdByMessage += $"\n**Created By**: {creator.User.DiscordUsername} {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)(int)account.LastGrade)}";
                            }
                        }

                        var publicMessage = status.Public ? $"\n**This co-op is public**." : "";

                        var embedBuilder = new EmbedBuilder()
                        .WithDescription($"{gradeMessage}{highestEBMessage}{createdByMessage}{publicMessage}\n" + 
                        (
                            (status.Finished()
                            ? "\nThis co-op is finished!"
                            : coopDetails.PercentProjectedForJoined >= 100 && !coop.FinishedOrFailed()
                            ? "\nThis co-op is projected to succeed without growth as long as there are no sleepers!"
                            : "") + $"\n[View on egg9000.com](https://egg9000.com/coop/{coop.ContractID}/{coop.Name})"
                        ))
                        .WithColor(color)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .WithAuthor(new EmbedAuthorBuilder().WithName($"{coop.Contract.Name} - Coop Code: {coop.Name}").WithIconUrl(EggIncEggs.GetEggById((int)coop.Contract.Details.Egg).Image))
                        ;


                        var updates = UpdateInterval.TotalMinutes;
                        if(finalChannelUpdate) {
                            embedBuilder.WithFooter($"Final Update");
                        } else {
                            embedBuilder.WithFooter($"Updates Every {updates} Minute{(updates > 1 ? "s" : "")} - Last Updated");
                        }



                        var ends = DiscordHelpers.TimeStamper(TimeSpan.FromSeconds(status.SecondsRemaining));
                        if(status.SecondsRemaining <= 0) {
                            ends = $"Expired {ends}";
                            if(!coop.PseudoExpired) coop.PseudoExpired = true;
                        }

                        for(var i = 0; i < 3; i++) {
                            if(coop.Contract.Details.GetGoals(league).Count > i) {
                                var goal = coop.Contract.Details.GetGoals(league)[i];
                                var title = $"Goal {i + 1} ";
                                var time = "";
                                var goalRemaingAmount = goal.TargetAmount - amountWithOffline;
                                var goalRemaingTime = goalRemaingAmount / totalRate;
                                time = $"\nTime: {GetTimeRemaining(goal.TargetAmount, totalRate, amountWithOffline)}";
                                if(status.TotalAmount > goal.TargetAmount) {
                                    title += "✅";
                                    time = "";
                                } else if(coop.Status == CoopStatusEnum.Failed) {
                                    title += "❌";
                                    time = "";
                                } else if(coopDetails.PercentProjectedForJoined > goal.TargetAmount) {
                                    title += "☑";
                                }
                                embedBuilder.AddField(title, $"Target: {goal.TargetAmount.ToEggString()}\nReward: {EggIncEggs.GetReward(goal)}{time}", true);
                            } else {
                                embedBuilder.AddField("\u17B5", "\u17B5", true);
                            }
                        }

                        //Estimate the time the coop is projected to finish
                        coop.ProjectedFinish = DateTimeOffset.Now.AddSeconds(Math.Min(TimeSpan.FromDays(365).TotalSeconds, GetTimeRemainingValue(targetAmount, totalRate, amountWithOffline).TotalSeconds));

                        var totalRatePerHour = totalRate * 60 * 60;
                        if(coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                            embedBuilder.AddField("Co-op Expires", ends, inline: true);

                            if(remainingAmount > 0) {
                                var remainingTime = remainingAmount / totalRate;
                                if(remainingTime < TimeSpan.MaxValue.TotalSeconds) {
                                    try {
                                        embedBuilder.AddField("Time To Complete", GetTimeRemaining(targetAmount, totalRate, amountWithOffline), inline: true);
                                        if(status.SecondsRemaining > remainingTime) {
                                            embedBuilder.AddField("Ahead By", TimeSpan.FromSeconds(status.SecondsRemaining - remainingTime).Humanize(2).ShortenTime(), inline: true);
                                        } else {
                                            embedBuilder.AddField("Behind By", TimeSpan.FromSeconds(status.SecondsRemaining - remainingTime).Humanize(2).ShortenTime(), inline: true);
                                        }
                                    } catch(OverflowException) {

                                    }
                                } else {
                                    embedBuilder.AddField("Time To Complete", "**\u221E**", inline: true);
                                    embedBuilder.AddField("\u17B5", "\u17B5");
                                }
                            } else if(!status.Finished()) {
                                await CheckCompleteOnCheckIn(coop, usersWithStatus, coopChannel, _db);
                                embedBuilder.AddField("Time To Complete", "Once everyone checks in", inline: true);
                            }

                            embedBuilder.AddField("Projected Amount", $"{coopDetails.Projected.ToEggString()} of {targetAmount.ToEggString()} {Math.Round(coopDetails.PercentProjectedForJoined)}%", inline: true);
                            embedBuilder.AddField("Current Amount", status.TotalAmount.ToEggString(), inline: true);
                            embedBuilder.AddField("Current With Offline", amountWithOffline.ToEggString(), inline: true);
                        } else if(coop.Status == CoopStatusEnum.Completed) {
                            embedBuilder.AddField("Final Amount", status.TotalAmount.ToEggString(), inline: true);
                            embedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                        } else if(coop.Status == CoopStatusEnum.Failed) {
                            embedBuilder.AddField("Final Amount", status.TotalAmount.ToEggString(), inline: true);
                            embedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                        }

                        await UpdateChannel(msgs, embedBuilder.Build(), coopChannel, coop, statusReponse.DiscordMessages);
                    }


                    try {
                        await _db.SaveChangesAsync();
                    } catch(Exception) {
                        await _db.SaveChangesAsync();
                    }


                    var times = timings.Finished();

                    //_logger.LogInformation("Co-op timings {timings} - {coop}", String.Join(",", times.Select(x => $"{x.name}:{x.time.Humanize().ShortenTime()}")), coop.Name);
                }
            } catch(Exception e) {
                _logger.LogError(e, "Error in co-op {coopid}", coopName ?? coopid.ToString());
                _bugsnag.Notify(e);
            }
        }

        public static int GetDigit(int number, int digit) {
            for(var i = 0; i < digit - 1; i++)
                number /= 10;
            return number % 10;
        }

        public async Task HandleSleeping(UserFarmDetails user, ITextChannel coopChannel, Coop coop, ApplicationDbContext _db, Guild dbguild, SocketGuild guild) {
            if(user.Xref is null || coop.CoopEnds < DateTimeOffset.Now || coop.FinishedOrFailed() || user.CoopStatus is null)
                return;

            var currentSleepStart = user.Joined ? DateTimeOffset.Now.Subtract(user.OfflineTime) : coop.Created;
            var hoursSleeping = (double)user.OfflineTime.TotalMinutes / 60.0;
            var siloTimeHours = (float)(user.SiloTimeMinutes / 60.0);
            var alertTime = (30.0 - siloTimeHours) / 2 + siloTimeHours;
            var needsAlert = hoursSleeping >= alertTime;
            var timeEmpty = Math.Round(hoursSleeping - siloTimeHours, 2);

            var sleepTracking = user.Xref.SleepTracking.ToList();

            var currentSleep = sleepTracking.FirstOrDefault(x => !x.WokeUp);

            if(currentSleep == null && needsAlert) {
                currentSleep = new SleepTracking { SleepStart = currentSleepStart, LastChecked = DateTimeOffset.Now, Silos = siloTimeHours, EggsShipped = user.EggsShipped, Rate = user.Rate };

                var messages = BotText.SleepingMessages;
                var random = new Random();
                var index = random.Next(messages.Count);

                if(user.DiscordUser != null) {
                    var warningText = messages[index].Replace("@name", user.DiscordUser.Mention + (timeEmpty < 0 ? $" [Empty silos in {timeEmpty} hours {coopChannel.Mention}]" : $" [Silos have been empty for {timeEmpty} hours {coopChannel.Mention}]"));
                    var dmResult = await BoolSendDm(user.DiscordUser, warningText, _db);
                    if(dmResult != DMResult.Success) {
                        await coopChannel.SendMessageAsync($"{warningText} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                    }
                }
                sleepTracking.Add(currentSleep);
            }

            if(currentSleep != null) {
                if(currentSleepStart > currentSleep.SleepStart.AddMinutes(10)) { //Adding 10 mins to account for weird time stuff
                    //No longer sleeping
                    currentSleep.WokeUp = true;
                    currentSleep.TotalHoursEmpty = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours - (currentSleep.Silos > 0 ? currentSleep.Silos : siloTimeHours);
                    currentSleep.Expected = currentSleep.EggsShipped + currentSleep.Silos * currentSleep.Rate;
                    currentSleep.Actual = user.EggsShipped;
                    user.Xref.TotalHoursSleeping = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours;
                    user.Xref.HoursSleeping = 0;
                } else {
                    var nextDemeritAt = (currentSleep.DemeritsGiven + 1) * 18;
                    var demeritChannel = await GetDemeritChannel(dbguild);
                    var needsDemerit = timeEmpty > nextDemeritAt && demeritChannel is not null && !user.Xref.NoDemerit;
                    if(needsDemerit && user.DBUser is not null) {
                        currentSleep.DemeritsGiven++;
                        if(user.DBUser.IsFreshEgg()) {
                            await coopChannel.SendMessageAsync($"{user.DiscordUser?.Mention ?? user.DBUser.DiscordUsername}: You will start receiving demerits for this 7 days after joining the server. Your silos have been empty for {nextDemeritAt} hours.");
                        } else {
                            var demerit = new Demerit {
                                When = DateTimeOffset.Now,
                                AdminUserId = Guid.Empty,
                                UserId = user.DBUser.Id,
                                Id = Guid.NewGuid(),
                                Reason = $"Empty silos for {nextDemeritAt} hours in {coop.Contract.Name}",
                                Details = JsonConvert.SerializeObject(new { FarmTimestemp = user.CoopStatus?.FarmInfo?.Timestamp, Silos = siloTimeHours })
                            };
                            _db.Demerit.Add(demerit);
                            await _db.SaveChangesAsync();
                            var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.DBUser.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();
                            var demeritText = $"Demerit added to {user.DiscordUser?.Mention ?? user.DBUser.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                            if(count >= 3) {
                                demeritText = $"**{demeritText}**";
                            }
                            await coopChannel.SendMessageAsync(demeritText);
                            await demeritChannel.SendMessageAsync($"{demeritText} {coopChannel.Mention}");
                        }
                    }
                    user.Xref.HoursSleeping = (int)Math.Floor((DateTimeOffset.Now - currentSleep.SleepStart).TotalHours);
                }

                if(!currentSleep.WokeUp) {
                    currentSleep.LastChecked = DateTimeOffset.Now;
                }
            }
            user.Xref.SleepTracking = sleepTracking;
        }

        public async Task HandleUnjoins(List<UserFarmDetails> usersNotJoined, SocketGuild guild, List<UserWithBackup> users, Guild dbguild, Coop coop, ApplicationDbContext _db, ITextChannel coopChannel) {
            var demeritChannel = await GetDemeritChannel(dbguild);
            if(demeritChannel is null) {
                return;
            }
            foreach(var userFarmDetail in usersNotJoined) {
                var user = users.FirstOrDefault(x => x.User.Id == userFarmDetail.Xref.GetID()).User;
                if(user == null || userFarmDetail.Xref.NoDemerit)
                    continue;

                if(userFarmDetail.Xref.CreatedOn > DateTimeOffset.Now.AddHours(-18)) {
                    _db.Remove(userFarmDetail.Xref);
                    await _db.SaveChangesAsync();
                    await coopChannel.SendMessageAsync($"Removed {userFarmDetail.DiscordUser?.GetCleanName() ?? user.DiscordUsername} without a demerit since they were added less than 18 hours before the co-op finished.");
                    continue;
                }

                if(user.Registered > DateTimeOffset.Now.AddDays(-7)) {
                    await coopChannel.SendMessageAsync($"{userFarmDetail.DiscordUser?.Mention ?? user.DiscordUsername}, you failed to join this co-op. After your first week in this server you will get a demerit for failing to join an assigned co-op. Ask staff if you have any questions.");
                    continue;
                }


                await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name}", user, _db, userFarmDetail.Xref, userFarmDetail.DiscordUser, coopChannel, dbguild, coop, true);
            }
        }

        public async Task HandlePingOnFull(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, ITextChannel coopChannel) {
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnFull ?? false)) {
                userStatus.Xref.CoopSetting.PingOnFull = false;
                userStatus.Xref.UpdateCoopSetting();

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"All users have joined the co-op {coopChannel.Mention}", db);
                if(dmResult != DMResult.Success) {
                    await coopChannel.SendMessageAsync($"{userStatus.DiscordUser.Mention} All users have joined the co-op {coopChannel.Mention} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }
        public async Task HandlePingOnCheckedIn(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, ITextChannel coopChannel) {
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnEveryoneCheckedIn ?? false)) {
                userStatus.Xref.CoopSetting.PingOnEveryoneCheckedIn = false;
                userStatus.Xref.UpdateCoopSetting();

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"The co-op {coopChannel.Mention} has finished and you are able to exit the co-op.", db);
                if(dmResult != DMResult.Success) {
                    await coopChannel.SendMessageAsync($"{userStatus.DiscordUser.Mention} The co-op {coopChannel.Mention} has finished and everyone is checked in. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }

        public async Task HandleFinished(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, ITextChannel coopChannel) {
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnFinished ?? false)) {
                userStatus.Xref.CoopSetting.PingOnFinished = false;
                userStatus.Xref.UpdateCoopSetting();

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"The co-op {coopChannel.Mention} has finished.", db);
                if(dmResult != DMResult.Success) {
                    await coopChannel.SendMessageAsync($"{userStatus.DiscordUser.Mention} The co-op {coopChannel.Mention} has finished. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }

        public async Task SendDMWarning(ApplicationDbContext db, SocketGuildUser discordUser, ITextChannel coopChannel, string Message, Coop coop) {
            if(discordUser is null)
                return;

            var dmChannel = await discordUser.CreateDMChannelAsync();
            var dmResult = await BoolSendDm(discordUser, $"{Message}: {coop.Name} for {EggIncEggs.GetEggById((int)coop.Contract.Details.Egg).Emoji} {coop.Contract.Name} - {coopChannel.Mention}", db);
            if(dmResult != DMResult.Success) {
                await coopChannel.SendMessageAsync($"{discordUser.Mention} {Message}: {coop.Name} for {EggIncEggs.GetEggById((int)coop.Contract.Details.Egg).Emoji} {coop.Contract.Name} - {coopChannel.Mention} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
            }
        }

        public async Task AddDemeritAndRemoveFromCoop(string reason, DBUser user, ApplicationDbContext _db, UserCoopXref xref, SocketGuildUser discordUser, ITextChannel coopChannel, Guild dbguild, Coop coop, bool alwaysRemove) {
            var demeritChannel = await GetDemeritChannel(dbguild);
            if(demeritChannel is null) {
                if(alwaysRemove) {
                    _db.Remove(xref);
                }
                return;
            }
            var existingDemerit = await _db.Demerit.AnyAsync(x => x.ContractID == coop.ContractID && x.UserId == user.Id);
            if(existingDemerit || xref.JoinedCoop) {
                await coopChannel.SendMessageAsync($"Removing {discordUser?.Mention ?? user.DiscordUsername} due to: {reason}");
                _db.Remove(xref);
                await _db.SaveChangesAsync();
            } else {
                _db.Remove(xref);
                if(user.IsFreshEgg()) {
                    await coopChannel.SendMessageAsync($"{discordUser.Mention ?? user.DiscordUsername}: You will start receiving demerits for this 7 days after joining the server. {reason} ");
                } else {
                    var demerit = new Demerit {
                        When = DateTimeOffset.Now,
                        AdminUserId = Guid.Empty,
                        UserId = user.Id,
                        Id = Guid.NewGuid(),
                        Reason = reason
                    };
                    _db.Demerit.Add(demerit);
                    await _db.SaveChangesAsync();
                    var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();
                    var demeritText = $"Demerit added to {discordUser?.Mention ?? user.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                    await coopChannel.SendMessageAsync(demeritText);
                    if(count >= 3)
                        demeritText = $"**{demeritText}**";
                    await demeritChannel.SendMessageAsync(demeritText + $" {coopChannel.Mention}");
                }
            }

        }

        public async Task CheckHighestEBJoined(Coop coop, List<UserWithStatus> usersWithStatus, CoopDetails coopDetails, ITextChannel coopChannel, ApplicationDbContext _db, List<UserFarmDetails> usersNotJoined) {
            if(usersWithStatus.Any(x => x.Xref?.CoopSetting?.PingOnHighestEB ?? false)) {
                var highestEB2 = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                if(highestEB2 != null && !usersNotJoined.Any(x => x?.EggIncId == highestEB2.Backup.EggIncId)) {
                    foreach(var user in usersWithStatus.Where(x => x.Xref?.CoopSetting?.PingOnHighestEB ?? false)) {
                        if(user.User.DiscordId == highestEB2.DBUser.DiscordId) continue; //Don't ping them if they are the highest EB
                        user.Xref.CoopSetting.PingOnHighestEB = false;
                        user.Xref.UpdateCoopSetting();
                        await _db.SaveChangesAsync();
                        await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Highest EB ({highestEB2.DiscordUser?.GetCleanName()} at {highestEB2.Backup.EarningsBonus.ToEggString()}) has joined", coop);
                    }
                }
            }
        }

        public async Task CheckCompleteOnCheckIn(Coop coop, List<UserWithStatus> usersWithStatus, ITextChannel coopChannel, ApplicationDbContext _db) {
            var anybodyWithPingSetting = usersWithStatus.Where(x => x.Xref?.CoopSetting?.PingOnCompleteOnCheckIn ?? false);

            if(anybodyWithPingSetting.Any()) {
                foreach(var user in anybodyWithPingSetting) {
                    user.Xref.CoopSetting.PingOnCompleteOnCheckIn = false;
                    user.Xref.UpdateCoopSetting();
                    await _db.SaveChangesAsync();
                    await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Your co-op will complete once everyone checks in.", coop);
                }
            }
        }

        public async Task CheckDeflectorChange(Ei.ContractCoopStatusResponse prevStatus, Ei.ContractCoopStatusResponse newStatus, Coop coop, List<UserWithStatus> usersWithStatus, ITextChannel coopChannel, ApplicationDbContext _db) {
            if(prevStatus == null || coop.FinishedOrFailed() || coop.CoopEnds < DateTimeOffset.Now) {
                return;
            }
            foreach(var user in usersWithStatus.Where(x => x.Status is not null && (x.Xref?.CoopSetting?.PingOnTachyonChange ?? false))) {
                var oldTachyon = GetTachyonAmount(prevStatus.Contributors, user.Status.Uuid);
                var newTachyon = GetTachyonAmount(newStatus.Contributors, user.Status.Uuid);
                if(oldTachyon != newTachyon) {
                    var oldVal = oldTachyon * 100;
                    var newVal = newTachyon * 100;
                    await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Tachyon Deflector amount changed from {oldVal:F0}% to {newVal:F0}%", coop);
                }
            }
        }

        private decimal GetTachyonAmount(IEnumerable<Ei.ContractCoopStatusResponse.Types.ContributionInfo> contributions, string currentUserUuid) {
            var matches = contributions.Where(x => x.Uuid != currentUserUuid && x.BuffHistory.Count > 0);
            var histories = matches.Select(x => x.BuffHistory.Last());
            return histories.Sum(x => (decimal)x.EggLayingRate - 1);
        }

        public async Task CheckOnCoopFullError(List<UserWithStatus> usersWithStatus, Coop coop, Ei.ContractCoopStatusResponse status, Contract contract, ITextChannel coopChannel) {
            //if(coop.FinishedOrFailedOrExpired || status.Contributors.Count < contract.MaxUsers)
            //    return;
            //foreach(var user in usersWithStatus.Where(x => x.Xref is not null && x.Status is not null && x.Status.ContributionAmount == 0 && x.Status.ContributionRate == 0 && !x.Xref.CoopFullWarning)) {
            //    user.Xref.CoopFullWarning = true;
            //    await coopChannel.SendMessageAsync($"<@{user.User.DiscordId}>, It looks like you attempted to join the co-op but might have gotten an error about the co-op being full. If you got the error please try using </fixfullcooperror:1111043604178276463>, wait a few minutes, and try joining again.\n\nIf this does not work, please use </callstaff:1095116354169864210>.");
            //}
        }

        public async Task<SocketTextChannel> GetDemeritChannel(Guild dbguild) {
            if(_demeritChannels.ContainsKey(dbguild.Id)) return _demeritChannels[dbguild.Id];

            var channel = await _client.GetChannelAsync(GuildChannelType.DemeritLogChannel, dbguild);
            if(channel is not null) {
                try {
                    _demeritChannels.Add(dbguild.Id, channel);
                } catch(ArgumentException) {

                }
                return channel;
            }

            return null;
        }
    }
}