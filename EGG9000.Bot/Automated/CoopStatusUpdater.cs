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
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class CoopStatusUpdater : _UpdaterBase<CoopStatusUpdater> {
        private int _counter;
        private SocketTextChannel _demeritChannel;

        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public CoopStatusUpdater(
            IServiceProvider provider
            ) : base(TimeSpan.FromMinutes(5), TimeSpan.Zero, provider) {
            _counter = 59;
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var sw = new Stopwatch();
            sw.Restart();
            using(var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"])) {
                var checkFinished = _counter == 0;
                if(++_counter >= 60) {
                    _counter = 0;
                }
                Console.WriteLine("Getting Users For CoopStatusUpdater");
                var users = await _db.DBUsers.AsQueryable().ToListAsync();
                Console.WriteLine("Getting Coops For CoopStatusUpdater");
                var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId != 0 && !x.DeletedChannel && ((!x.Finished && x.Status != CoopStatusEnum.Failed) || checkFinished) && x.Status != CoopStatusEnum.WaitingOnStarter).ToListAsync();

                var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

                Console.WriteLine($"Users and Coops: {sw.ElapsedMilliseconds}ms");

                var throttler = new SemaphoreSlim(5);

//#if DEBUG
//                coops = coops.Where(x => x.Name == "BunchCase28").ToList();
//#endif

                var guildCoopGroups = coops.GroupBy(x => x.OverflowGuildId > 0 ? x.OverflowGuildId : x.GuildId).OrderBy(x => x.Count());
                foreach(var guildCoops in guildCoopGroups) {
                    if(cancellationToken.IsCancellationRequested) break;
                    var dbguild = dbguilds.FirstOrDefault(x => x.DiscordSeverId == guildCoops.Key || x.OverflowServers.Any(y => y == guildCoops.Key));
                    var guild = _client.Guilds.FirstOrDefault(x => x.Id == guildCoops.Key);
                    if(guild == null)
                        continue;
                    await guild.DownloadUsersAsync();
                    Console.WriteLine($"Coops for guild: {guild.Name}");

                    var tasks = new List<Task>();

                    var rng = new Random();
                    //foreach(var coop in guildCoops.OrderBy(a => rng.Next())) {
                    foreach(var coop in guildCoops) {
                        if(cancellationToken.IsCancellationRequested) break;
                        await throttler.WaitAsync();
                        tasks.Add(Task.Run(async () => {
                            try {
                                await SendUpdate(coop.Id, guild, users, dbguild, cancellationToken, _db);
                            } finally {
                                throttler.Release();
                            }
                        }));
                    }

                    await Task.WhenAll(tasks);

                    Console.WriteLine($"Co-op Count: {guildCoops.Count()}, Successful: {tasks.Count(x => !x.IsFaulted)}, Error: {tasks.Count(x => x.IsFaulted)}");
                }

            }
            sw.Stop();
            var time = sw.Elapsed.TotalSeconds;
            Console.WriteLine($"Finished Updating Co-ops in {Math.Round(time)} seconds");
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


        public static List<string> GetStatusStringAsync(Ei.ContractCoopStatusResponse status, List<UserWithStatus> usersWithStatus, int League, Contract contract) {
            var table = new List<List<FixedWidthCell>> {
                new List<FixedWidthCell> {
                new FixedWidthCell($"{status.Contributors.Count}/{contract.MaxUsers}"),
                new FixedWidthCell("Discord", CellAlignment.Center),
                new FixedWidthCell("EB", CellAlignment.Center),
                new FixedWidthCell("Total", CellAlignment.Center),
                new FixedWidthCell("Rate", CellAlignment.Center),
                new FixedWidthCell("📈", CellAlignment.Center),
                new FixedWidthCell("%", CellAlignment.Center),
                new FixedWidthCell("🟡", CellAlignment.Center, false, 1),
                new FixedWidthCell("⏲️", CellAlignment.Center, false, 1),
                new FixedWidthCell("Silo"),
                new FixedWidthCell(""),
            }
            };
            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            table.AddRange(usersWithStatus.OrderByDescending(x => x.Status.Projected).Select(x => {
                var siloTime = -1.0;
                if(x.SiloTime > 0) {
                    siloTime = x.SiloTime;
                }

                var sleeping = (!x.Status.Active ? "💤" : "");

                if(siloTime > -1 && x.Sleeping.HasValue && x.Sleeping.Value.TotalMinutes > siloTime) {
                    sleeping = $"💤 {x.Sleeping.Value.Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()} - Empty Silos {x.Sleeping.Value.Add(TimeSpan.FromMinutes(-siloTime)).Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()}";
                }

                if(x.Status.TimeCheatDetected)
                    sleeping += " ⏱️";

                var eb = Math.Pow(10, x.Status.SoulPower) * 100;

                return new List<FixedWidthCell> {
                    new FixedWidthCell(Truncate((x.User == null ? "👽" : "") + Regex.Replace(x.Status.UserName, @"\p{Cs}", ""), 11)),
                    new FixedWidthCell(Truncate(Regex.Replace(ebrgx.Replace(x.Status.DiscordName ?? "", "") , @"\p{Cs}", ""), 11)),
                    //new FixedWidthCell(x.Backup?.EarningsBonus.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell((Math.Pow(10,x.Status.SoulPower) * 100).ToEggString(), CellAlignment.Right),
                    new FixedWidthCell(x.Status.TotalString, CellAlignment.Right),
                    new FixedWidthCell(x.Status.RateString, CellAlignment.Right),
                    new FixedWidthCell(x.Status.Projected.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell((x.Status.Projected / status.Projected).ToString("P0").Replace(" ",""), CellAlignment.Right),
                    new FixedWidthCell(x.Status.BoostTokens.ToString()),
                    new FixedWidthCell(x.Status.LastActive.TotalSeconds < 1 ? "Now" : x.Status.LastActive.Humanize().ShortenTime()),
                    new FixedWidthCell(siloTime > -1 ? TimeSpan.FromMinutes((double)siloTime).Humanize(2, maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime() : ""),
                    new FixedWidthCell(sleeping),
                };
            }));



            var lstr = new List<string>();



            var tableString = $"```{FixedWidthTable.GetTable(table)}```";

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

            //Reserve up to 4 msgs
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
                    await coopChannel.DeleteMessagesAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                } catch(TimeoutException) {
                    var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                    await coopChannel.DeleteMessagesAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                }
            } else {
                var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);
                var NewUpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);

                if(coopChannel != null) {
                    //IEnumerable<IMessage> discordMessages;
                    //try {
                    //    discordMessages = await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12).FlattenAsync();
                    //} catch(Exception ex) when(ex is TimeoutException || ex is HttpException || ex is System.Net.Http.HttpRequestException) {
                    //    try {
                    //        discordMessages = await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12).FlattenAsync();
                    //    } catch(Exception) {
                    //        Console.WriteLine("***Failed to get Messages for CoopStatus");
                    //        return;
                    //    }
                    //}

                    var pinnedMessages = false;
                    for(int i = 0; i < msgs.Count(); i++) {
                        if(UpdateMessageIDs.Count > i) {
                            try {
                                var post = (RestUserMessage)existingMessages.FirstOrDefault(x => x.Id == UpdateMessageIDs[i]);
                                if(post == null) {
                                    if(msgs[i] == "@@@EMBED") {
                                        post = ((RestUserMessage)await coopChannel.SendMessageAsync(embed: embed));
                                    } else {
                                        post = ((RestUserMessage)await coopChannel.SendMessageAsync(msgs[i]));
                                    }
                                    NewUpdateMessageIDs.Remove(UpdateMessageIDs[i]);
                                    NewUpdateMessageIDs.Add(post.Id);
                                } else {
                                    if(msgs[i] == "@@@EMBED") {
                                        await post.ModifyAsync(msg => { msg.Embed = embed; msg.Content = null; });
                                    } else {
                                        var changes = post.Content.CompareChanges(msgs[i]);
                                        if(changes > 0) {
                                            await post.ModifyAsync(msg => msg.Content = msgs[i]);
                                        } else {
                                        }
                                    }
                                }
                                if(!post.IsPinned) {
                                    pinnedMessages = true;
                                    await post.PinAsync();
                                }
                            } catch(Exception e) {
                                Console.WriteLine($"Error updating messages: {e.Message}");
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
                            await coopChannel.DeleteMessagesAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                        } catch(TimeoutException) {
                            var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                            await coopChannel.DeleteMessagesAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
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
                    for(int i = 0; i < 10; i++) {
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
            return new StatusResponse {
                Status = statusTask.Result,
                DiscordMessages = messageTask.Result
            };
        }

        public async Task SendUpdate(Guid coopid, SocketGuild guild, List<DBUser> users, Guild dbguild, CancellationToken cancellationToken, ApplicationDbContext db) {
            using(var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"])) {
                var coop = await _db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).FirstOrDefaultAsync(x => x.Id == coopid);
                if(coop == null) {
                    return;
                }
                try {
                    //Console.WriteLine($"Calling update for {coop.Name}");
                    var times = new List<long>();
                    var sw = new Stopwatch();
                    sw.Reset();

                    ITextChannel coopChannel = guild.TextChannels.FirstOrDefault(x => x.Id == coop.DiscordChannelId);

                    if(coopChannel == null) {
                        var restguild = await _client.Rest.GetGuildAsync(guild.Id);
                        try {
                            coopChannel = await restguild.GetTextChannelAsync(coop.DiscordChannelId);
                        } catch(Exception) {
                        }
                    }

                    if(coopChannel == null) {
                        Console.WriteLine($"ERROR FINDING CHANNEL FOR CO-OP: {coop.Name}");
                        return;
                    }

                    List<IGuildUser> coopDiscordUsers = coopChannel is SocketTextChannel ? ((SocketTextChannel)coopChannel).Users.ToList().Select(x => (IGuildUser)x).ToList() : (await coopChannel.GetUsersAsync().FlattenAsync()).ToList();

                    var statusReponse = await GetStatus(coop, coopChannel, cancellationToken);
                    if(cancellationToken.IsCancellationRequested) return;
                    var status = statusReponse.Status;
                    if(status?.Success ?? false) {



                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();



                        var usersWithStatus = status.Participants.Select(participant => {
                            var xref = coop.UserCoopsXrefs.FirstOrDefault(xref => xref.EggIncId == participant.GetID());
                            //First try for FixedUserName
                            if(xref == null) {
                                xref = coop.UserCoopsXrefs.FirstOrDefault(x => !string.IsNullOrEmpty(x.FixedUserName) && x.FixedUserName == participant.UserName);
                            }
                            if(xref == null) {
                                //Now try to match a backup username
                                var coopBackups = users.Where(x => x.Backups is not null).SelectMany(x => x.Backups.Where(y =>
                                    coop.UserCoopsXrefs.Any(z => z.EggIncId == y.EggIncId || (!z.EggIncId.StartsWith("EI") && z.UserId == x.Id && x.Backups.Count == 1))
                                ));
                                var backup = coopBackups.FirstOrDefault(x => x.UserName == participant.UserName);
                                if(backup is null) {
                                    //Try to match to EB
                                    backup = coopBackups.FirstOrDefault(x => Math.Log10(x.EarningsBonus / 100) == participant.SoulPower);
                                }
                                if(backup is not null) {
                                    xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.EggIncId == backup.EggIncId);
                                    if(xref is null) {
                                        xref = coop.UserCoopsXrefs.FirstOrDefault(x => users.Where(x => x.Backups is not null).First(x => x.Backups.Any(y => y.EggIncId == backup.EggIncId)).Id == x.UserId);
                                    }
                                    if(xref is not null) {
                                        var isNameUnique = !users.Where(x => x.Backups is not null).Any(x => x.Backups.Any(b => b.UserName == participant.UserName && b.EggIncId != xref.EggIncId));
                                        if(isNameUnique && !string.IsNullOrEmpty(participant.UserName));
                                            xref.FixedUserName = participant.UserName;
                                    }
                                }
                            }

                            var userWithStatus = new UserWithStatus {
                                Status = participant,
                                Xref = xref
                            };
                            userWithStatus.User = users.FirstOrDefault(x => x.Id == userWithStatus.Xref?.GetID());
                            return userWithStatus;
                        }).ToList();

                        var usersNotJoined = coop.UserCoopsXrefs.Where(x => !usersWithStatus.Any(y => y.Xref?.EggIncId == x.EggIncId)).ToList();  // !status.Contributors.Any(y => y.GetID() == x.EggIncId)).ToList();

                        if(usersWithStatus.Any(y => y.Xref is null) && usersNotJoined.Any()) {
                            for(var i = 0; i < usersNotJoined.Count; i++) {
                                var user = usersNotJoined[i];
                                //var last5Coops = await _db.UserCoopXrefs.Where(x => x.EggIncId == user.EggIncId && x.JoinedCoop).Select(x => new { Xref = x, Coop = x.Coop }).Take(5).ToListAsync();
                                var lastCoopBeforeChange = await _db.UserCoopXrefs
                                    .Where(x => x.EggIncId == user.EggIncId && x.JoinedCoop && x.CreatedOn < new DateTimeOffset(2022, 07, 10, 0, 0, 0, TimeSpan.Zero))
                                    .Select(x => new { Xref = x, Coop = x.Coop })
                                    .FirstOrDefaultAsync();
                                if(lastCoopBeforeChange is not null) {
                                    var username = lastCoopBeforeChange.Coop.LastStatusUpdate.Participants.FirstOrDefault(x => x.UserId == user.EggIncId)?.UserName;
                                    if(!string.IsNullOrWhiteSpace(username)) {
                                        var userStatus = usersWithStatus.FirstOrDefault(x => x.Status.UserName == username);
                                        if(userStatus != null) {
                                            userStatus.Xref = user;
                                            user.FixedUserName = username;
                                            await _db.SaveChangesAsync();
                                            usersNotJoined.Remove(user);
                                            i--;
                                        }
                                    }
                                }

                            }
                        }


                        foreach(var x in usersWithStatus) {
                            x.Backup = x.User?.Backups?.FirstOrDefault(y => y.EggIncId == x.Xref?.EggIncId);
                            if(x.Backup != null) {
                                var awayTime = Research.GetTotalSiloCapacity(x.Backup);
                                var farm = x.Backup?.Farms?.FirstOrDefault(x => x.ContractId == status.ContractIdentifier);
                                if(farm != null) {
                                    x.FarmStats = farm.WithStats(x.Backup);
                                    x.SiloTime = awayTime * farm.SilosOwned;
                                    var siloTimeHours = x.SiloTime / 60;
                                    if(x.Xref.SiloTimeHours != siloTimeHours) {
                                        x.Xref.SiloTimeHours = (float)siloTimeHours;
                                        await _db.SaveChangesAsync();
                                    }
                                }
                            }
                        }


                        foreach(var xref in coop.UserCoopsXrefs) {

                            var user = users.First(x => x.Id == xref.GetID());
                            var discordUser = guild.GetUser(user.DiscordId);
                            if(discordUser == null) {
                                await _client.Rest.GetGuildUserAsync(guild.Id, user.DiscordId);
                            }
                            if(!coopDiscordUsers.Any(x => x.Id == user.DiscordId) && discordUser != null) {
                                await ((ITextChannel)coopChannel).AddPermissionOverwriteAsync(discordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
                                Console.WriteLine($"Added Permission for {user.DiscordUsername}");
                                coopDiscordUsers.Add(discordUser);
                            }


                        }

                        usersWithStatus.ForEach(userStatus => {
                            if(userStatus.User != null) {
                                userStatus.DiscordUser = guild.GetUser(userStatus.User.DiscordId);
                            }
                        });

                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();





                        foreach(var userCoopStatus in status.Contributors) {
                            var xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.EggIncId == userCoopStatus.GetID());

                            UserCoopStatus lastStatus = null;

                            if(xref == null) {
                                var coopBackups = users.Where(x => x.Backups is not null).SelectMany(x => x.Backups.Where(y => coop.UserCoopsXrefs.Any(z => z.EggIncId == y.EggIncId)));
                                var backup = coopBackups.FirstOrDefault(x => x.UserName == userCoopStatus.UserName);
                                if(backup is not null) {
                                    xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.EggIncId == backup.EggIncId);
                                }
                            }

                            if(xref == null || !xref.LastStatusTime.HasValue) {
                                lastStatus = await _db.UserCoopStatuses.AsQueryable().OrderByDescending(x => x.CreatedOn).FirstOrDefaultAsync(x => x.CoopId == coop.Id && x.EggIncId == userCoopStatus.GetID());
                            }


                            if(xref == null) {
                                var dbuser = users.FirstOrDefault(x => x.EggIncIds.Any(y => y.Id == userCoopStatus.GetID()));
                                if(dbuser != null) {
                                    xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.GetID() == dbuser.Id && x.EggIncId == userCoopStatus.GetID());
                                    if(xref != null) {
                                        _db.Remove(xref);
                                        await _db.SaveChangesAsync();
                                        var newXref = new UserCoopXref {
                                            EggIncId = userCoopStatus.GetID(),
                                            CreatedOn = xref.CreatedOn,
                                            AddedToChannel = xref.AddedToChannel,
                                            CoopId = xref.CoopId,
                                            JoinedCoop = true,
                                            LastStatusTime = xref.LastStatusTime,
                                            SleepingWarningTime = xref.SleepingWarningTime,
                                            Starter = xref.Starter,
                                            UserId = xref.GetID(),
                                            WaitingOnStarter = xref.WaitingOnStarter,
                                            Status = xref.Status,
                                            WasAssigned = false
                                        };
                                        _db.Add(newXref);
                                        xref = newXref;
                                        await coopChannel.SendMessageAsync($"Looks like {userCoopStatus.UserName} might have joined without being assigned. (This could be an error)");
                                    } else {
                                        var channeluser = coopDiscordUsers.FirstOrDefault(x => x.Id == dbuser.DiscordId); // await coopChannel.GetUserAsync(dbuser.DiscordId);
                                        if(channeluser == null) {
                                            try {
                                                var discorduser = guild.Users.FirstOrDefault(x => x.Id == dbuser.DiscordId);
                                                if(discorduser != null) {
                                                    await ((ITextChannel)coopChannel).AddPermissionOverwriteAsync(discorduser, new OverwritePermissions(viewChannel: PermValue.Allow));
                                                    channeluser = discorduser;
                                                }
                                            } catch {
                                                Console.WriteLine($"Error Adding Permission for {dbuser.DiscordUsername}");
                                            }
                                        }
                                        if(channeluser != null) {
                                            xref = new UserCoopXref {
                                                WaitingOnStarter = false,
                                                UserId = dbuser.Id,
                                                EggIncId = userCoopStatus.GetID(),
                                                AddedToChannel = true,
                                                CoopId = coop.Id,
                                                CreatedOn = DateTimeOffset.UtcNow,
                                                JoinedCoop = true,
                                                LastStatusTime = lastStatus?.CreatedOn ?? DateTimeOffset.UtcNow,
                                                Starter = false,
                                                Status = JsonConvert.SerializeObject(userCoopStatus),
                                                WasAssigned = false
                                            };
                                            _db.UserCoopXrefs.Add(xref);
                                            userCoopStatus.DiscordName = channeluser.GetCleanName();
                                            var userStatus = usersWithStatus.FirstOrDefault(x => x.Status.SoulPower == userCoopStatus.SoulPower && x.Status.UserName == userCoopStatus.UserName);
                                            if(userStatus != null) {
                                                userStatus.Xref = xref;
                                                userStatus.User = dbuser;
                                                userStatus.DiscordUser = (SocketGuildUser)channeluser;
                                            }
                                        }
                                        //await coopChannel.SendMessageAsync($"Looks like {userCoopStatus.UserName} might have joined without being assigned. (This could be an error)");
                                    }
                                }
                            }




                            if(xref != null) {
                                if(!xref.LastStatusTime.HasValue) {
                                    xref.LastStatusTime = lastStatus?.CreatedOn ?? DateTimeOffset.UtcNow;
                                    xref.Status = JsonConvert.SerializeObject(userCoopStatus);
                                    xref.SleepingWarningTime = lastStatus?.SleepingWarning;
                                } else {
                                    var lStatus = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse.Types.ContributionInfo>(xref.Status ?? "{}");
                                    if(lStatus.ContributionAmount != userCoopStatus.ContributionAmount) {
                                        xref.LastStatusTime = DateTimeOffset.UtcNow;
                                        xref.Status = JsonConvert.SerializeObject(userCoopStatus);
                                        var newStatus = new UserCoopStatus {
                                            CoopId = coop.Id,
                                            CreatedOn = DateTimeOffset.UtcNow,
                                            EggIncId = userCoopStatus.GetID(),
                                            EggIncName = userCoopStatus.UserName,
                                            Rate = userCoopStatus.ContributionRate,
                                            Total = userCoopStatus.ContributionAmount,
                                            UserId = xref.GetID()
                                        };

                                        _db.UserCoopStatuses.Add(newStatus);
                                    }
                                }

                                //Handle sleeping
                                var user = usersWithStatus.FirstOrDefault(x => x.Backup != null && x.Backup.EggIncId == userCoopStatus.GetID());
                                await HandleSleeping(user, status, xref, coopChannel, coop, userCoopStatus, _db, dbguild, guild);
                            }
                        }


                        var league = (int?)coop.League ?? 0;
                        var targetAmount = coop.Contract.Details.GoalSets.Count > 0 ? coop.Contract.Details.GoalSets[league].Goals.Last().TargetAmount : coop.Contract.Details.Goals.Last().TargetAmount;
                        var amountWithOffline = status.Participants.Sum(x => {
                            var userWithStatus = usersWithStatus.FirstOrDefault(y => y.Status.GetID() == x.GetID());
                            return x.AmountWithOffline((userWithStatus.Xref?.SiloTimeHours * 60) ?? 0, DateTimeOffset.UtcNow - (userWithStatus.Xref?.LastStatusTime ?? DateTimeOffset.Now));
                        });
                        var remainingAmount = targetAmount - amountWithOffline;
                        var totalRate = status.Participants.Sum(x => x.ContributionRate);

                        var timeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, totalRate, amountWithOffline);



                        var hasDuplicate = status.Contributors.Count > coop.Contract.MaxUsers;
                        if(!hasDuplicate && !coop.Finished) {
                            if(!coop.ProjectedToFinish && status.ProjectedToFinish(coop.Contract, (int?)coop.League ?? 0)) {
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

                            if(status.SecondsRemaining > 1 && coop.ProjectedToFinish && !status.ProjectedToFinish(coop.Contract, (int?)coop.League ?? 0)) {
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

                            if(coop.Finished && coop.Status != CoopStatusEnum.Completed) {
                                coop.Finished = true;
                                coop.Status = CoopStatusEnum.Completed;
                                Debug.WriteLine("*** Not Showing as completed");
                                try {
                                    await _db.SaveChangesAsync();
                                } catch(Exception) {
                                    await Task.Delay(100);
                                    try {
                                        await _db.SaveChangesAsync();
                                    } catch(Exception) { }
                                }
                            }

                            if(!coop.Finished && status.Finished(coop.Contract, (int?)coop.League ?? 0) && coop.Status != CoopStatusEnum.Failed) {
                                coop.Finished = true;
                                coop.CoopCompleted = DateTimeOffset.UtcNow;
                                coop.Status = CoopStatusEnum.Completed;

                                await _db.SaveChangesAsync();
                                await coopChannel.SendMessageAsync($"Coop {coop.Name} is finished!");

                                var finishedCoopCategories = await _client.GetAllFinishedCategories(guild);
                                foreach(var category in finishedCoopCategories) {
                                    var channelCount = guild.TextChannels.Count(x => x.CategoryId == category.Id);
                                    Console.WriteLine($"Finished Coop Category {category.Name} Count {channelCount}");
                                    if(channelCount < 50) {
                                        try {
                                            Console.WriteLine($"Trying to set category");
                                            await coopChannel.ModifyAsync(x => { x.CategoryId = category.Id; });
                                            Console.WriteLine($"Category set!");
                                            break;
                                        } catch(Exception) {
                                            Console.WriteLine($"Error setting category");
                                        }
                                    }
                                }

                                await HandleUnjoins(usersNotJoined, guild, users, dbguild, coop, _db, coopChannel);
                            }
                        }


                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();


                        if(coop.CurrentUsers != status.Contributors.Count) {
                            var hadDuplicate = coop.CurrentUsers > coop.MaxUsers;
                            coop.CurrentUsers = status.Contributors.Count;
                            coop.MaxUsers = coop.Contract.MaxUsers;
                        }


                        //Add Discord Name
                        foreach(var userStatus in usersWithStatus) {

                            if(userStatus.DiscordUser != null) {
                                userStatus.Status.DiscordName = userStatus.DiscordUser.Nickname ?? userStatus.DiscordUser.Username;

                                //Check if in channel
                                if(!coopDiscordUsers.Any(x => x.Id == userStatus.DiscordUser.Id)) {
                                    await coopChannel.AddPermissionOverwriteAsync(userStatus.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
                                    Console.WriteLine($"Added Permission for {userStatus.User.DiscordUsername}");
                                }
                            }
                        }

                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();




                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();

                        var msgs = GetStatusStringAsync(status, usersWithStatus, (int?)coop.League ?? 0, coop.Contract);
                        var lastMessage = "";
                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();


                        foreach(var userStatus in usersWithStatus.Where(x => x.Xref != null)) {
                            if(!userStatus.Xref.AddedToChannel && userStatus.DiscordUser != null) {
                                await coopChannel.AddPermissionOverwriteAsync(userStatus.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
                                userStatus.Xref.AddedToChannel = true;
                                Console.WriteLine("Adding user to channel");
                            }

                            if(!userStatus.Xref.JoinedCoop) {
                                userStatus.Xref.JoinedCoop = true;
                                Console.WriteLine("User Joined Co-op");
                                var unjoinedRole = userStatus.DiscordUser?.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
                                if(unjoinedRole != null) {
                                    await userStatus.DiscordUser.RemoveRoleAsync(unjoinedRole);
                                }
                                await _db.SaveChangesAsync();
                                var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                                var messagesToDelete = messages.Where(x => x.IsPinned == false && x.Author.IsBot && x.MentionedUserIds.Count == 1 && x.MentionedUserIds.Any(y => y == userStatus.DiscordUser.Id) && !x.Content.ToLower().Contains("demerit"));
                                await coopChannel.DeleteMessagesAsync(messagesToDelete);
                            }
                        }

                        //Handle waiting on assigned
                        var missingFromServer = false;



                        if(usersNotJoined.Count == 0 && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                            coop.Status = CoopStatusEnum.AllAssignedJoined;
                        } else {
                            var userList = new List<String>();
                            foreach(var xref in usersNotJoined) {
                                try {
                                    var user = users.First(x => x.Id == xref.GetID());

                                    var discordUser = guild.GetUser(user.DiscordId);
                                    if(discordUser == null) {
                                        userList.Add($"{user.DiscordUsername} (Missing from server)");
                                        missingFromServer = true;
                                    } else if(user.EggIncIds.Count > 1) {
                                        var eggaccount = user.EggIncIds.FirstOrDefault(x => x.Id == xref.EggIncId);
                                        if(eggaccount != null)
                                            userList.Add($"{discordUser.Mention} ({eggaccount.Name})");
                                    } else {
                                        userList.Add(discordUser?.Mention);
                                    }

                                    if(discordUser != null && !coop.Finished && coop.Status != CoopStatusEnum.Failed) {
                                        if(!xref.JoinWarning24TillFinish && timeRemaining.TotalHours < 24 && xref.CreatedOn < DateTimeOffset.Now.AddHours(-1)) {
                                            xref.JoinWarning24TillFinish = true;
                                            await _db.SaveChangesAsync();
                                            await SendDMWarning(discordUser, coopChannel, $"{discordUser.Mention} reminder to join - co-op will be finished in under {Math.Ceiling(timeRemaining.TotalHours)} hours", coop);
                                        } else if(!xref.JoinWarning24h && xref.CreatedOn < DateTimeOffset.Now.AddHours(-24)) {
                                            xref.JoinWarning24h = true;
                                            xref.JoinWarning12h = true;
                                            await _db.SaveChangesAsync();
                                            //await coopChannel.SendMessageAsync($"{discordUser.Mention} reminder to join - 24h since added to co-op");
                                            var dmChannel = await discordUser.CreateDMChannelAsync();
                                            await SendDMWarning(discordUser, coopChannel, $"{discordUser.Mention} reminder to join - 24h since added to co-op", coop);
                                        } else if(!xref.JoinWarning12h && xref.CreatedOn < DateTimeOffset.Now.AddHours(-12)) {
                                            xref.JoinWarning12h = true;
                                            await _db.SaveChangesAsync();
                                            await SendDMWarning(discordUser, coopChannel, $"{discordUser.Mention} reminder to join - 12h since added to co-op", coop);
                                        }

                                        if(xref.CreatedOn < DateTimeOffset.Now.AddHours(-48) && status.ProjectedPercent(coop.Contract, (int?)coop.League ?? 0) > 100) {
                                            await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name} within 48 hours", user, _db, xref, discordUser, coopChannel, dbguild, coop);
                                        }
                                    }

                                    if(!xref.OutsideCoop && coop.GuildId == _CPGuildId && !coop.Finished && coop.Status != CoopStatusEnum.Failed) {
                                        var backup = user.Backups.FirstOrDefault(x => x.EggIncId == xref.EggIncId);
                                        if(backup != null) {
                                            var farm = backup.Farms.FirstOrDefault(x => x.ContractId == coop.ContractID);
                                            if(farm == null || farm.Cancelled) {
                                                await coopChannel.SendMessageAsync($"It looks like {discordUser?.Mention ?? user.DiscordUsername} has exited their farm. Please use the command `/callstaff` to see what to do next");
                                                xref.OutsideCoop = true;
                                                await _db.SaveChangesAsync();
                                            } else if(!string.IsNullOrWhiteSpace(farm.CoopId) && !farm.CoopId.Equals(coop.Name, StringComparison.OrdinalIgnoreCase)) {
                                                await coopChannel.SendMessageAsync($"It looks like {discordUser?.Mention ?? user.DiscordUsername} has joined another co-op named {farm.CoopId}. Please use the command  `/callstaff` to see what to do next");
                                                xref.OutsideCoop = true;
                                                await _db.SaveChangesAsync();
                                            }
                                        }
                                    }
                                } catch(Exception) { }
                            }
                            lastMessage += $"Ready for the following to join the coop **{coop.Name}**: {string.Join(", ", userList)}\n";
                        }

                        if(status.Public) {
                            lastMessage += $"This co-op is public.\n";
                        }




                        var usersAssigned = coop.UserCoopsXrefs.Select(x => {
                            var User = users.FirstOrDefault(y => y.Id == x.GetID());
                            if(User == null)
                                return null;
                            var backup = User.Backups?.FirstOrDefault(y => y?.EggIncId == x.EggIncId);
                            if(backup == null)
                                return null;
                            return new {
                                User = User,
                                Backup = User.Backups?.First(y => y.EggIncId == x.EggIncId)
                            };
                        }).Where(x => x != null);
                        var highestEB = usersAssigned.OrderByDescending(x => x.Backup?.EarningsBonus ?? 0).FirstOrDefault();
                        if(highestEB != null)
                            lastMessage += $"Highest EB: {highestEB.User.DiscordUsername} at {highestEB.Backup.EarningsBonus.ToEggString()} {(usersNotJoined.Any(x => x?.EggIncId == highestEB.Backup.EggIncId) ? "has not joined yet." : "has joined!")}\n";
                        else
                            Console.WriteLine($"Missing UsersAssigned for {coop.Name}");

                        //lastMessage += $"Contract: {coop.Contract.Name} {coop.Contract.ID}\n";
                        lastMessage += "\n*Make sure and equip any Tachyon Deflectors you have!*\n";

                        var giftInfos = usersWithStatus.Where(x => x.Status.FarmInfo is not null && x.FarmStats is not null).Select(x => new {
                            Shipping = x.Status.ContributionRate / x.FarmStats.MaxShippingRate * 100,
                            Habs = x.Status.ProductionParams.FarmPopulation / x.Status.ProductionParams.FarmCapacity * 100,
                            x.Status.UserName,
                            x.Status.ProductionParams.FarmPopulation
                        });

                        var personToGiftTo = giftInfos
                            .Where(x =>
                                x.Shipping < 99.5 &&
                                x.Habs < 99.5
                            )
                            .OrderByDescending(x => x.FarmPopulation).Take(10);
                        if(personToGiftTo.Count() > 0) {
                            var table = personToGiftTo.Select(x => new List<FixedWidthCell> {
                                new FixedWidthCell(Truncate(x.UserName, 11)),
                                new FixedWidthCell($"{x.FarmPopulation.ToEggString()}🐔", CellAlignment.Right),
                                new FixedWidthCell($"{Math.Round(x.Habs, x.Habs > 97 ? 1 : 0)}%🏠", CellAlignment.Right),
                                new FixedWidthCell($"{Math.Round(x.Shipping, x.Shipping > 97 ? 1 : 0)}%🚚", CellAlignment.Right),
                            }).ToList();
                            lastMessage += $"\nFarms that would benefit from gifting chickens: \n```{String.Join("\n", FixedWidthTable.GetTable(table))}```\n";
                        } else {
                            lastMessage += "\nLooks like everyone's shipping and/or habs are full so gifting chickens isn't useful.";
                        }

                        lastMessage += "\nCo-op Commands:\n`/pingonfull` Receive DM ping when everyone has joined\n`/callstaff` Use this instead of pinging us for help with things like typing in the wrong code (don't restart until we tell you to)";
                        lastMessage += "\n`/fixjoinedwrongcoop` Use this command if you mistyped the co-op name, if you joined a co-op for the wrong contract use `/callstaff`";

                        var usersToCheckDeflector = usersWithStatus.Where(x => x.Backup is not null && x.Backup.ArtifactHall is not null && x.Status.Projected < usersWithStatus.Max(y => y.Status.Projected) / 2);
                        var usersNeedToAddDeflector = new List<UserWithStatus>();
                        foreach(var user in usersToCheckDeflector) {
                            var farm = user.Backup.Farms.First(x => x.ContractId == coop.ContractID);
                            if(!farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) && user.Backup.ArtifactHall.Any(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)) {
                                usersNeedToAddDeflector.Add(user);
                            }
                        }

                        var usersWithHall = usersWithStatus.Where(x => x.Backup is not null && x.Backup.ArtifactHall is not null);

                        if(usersWithHall.Any()) {

                        }

                        if(usersNeedToAddDeflector.Any()) {
                            lastMessage += $"\n\n**The following users have a Tachyon Deflector they should equip: {string.Join(", ", usersNeedToAddDeflector.Select(y => y.DiscordUser.Mention))}";
                        }


                        if(status.Contributors.Count == coop.MaxUsers && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                            coop.Status = CoopStatusEnum.Full;
                        }

                        if(!coop.Finished && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed && status.Failed(coop.Contract, (int?)coop.League ?? 0) && (status.AllMembersReporting || coop.CoopEnds < DateTime.Now.AddDays(2))) {
                            if(coop.Contract.GoodUntil > DateTimeOffset.UtcNow) {
                                await coopChannel.SendMessageAsync($"Co-op {coop.Name} failed to reach all the goals and the contract is still available for {(coop.Contract.GoodUntil - DateTimeOffset.UtcNow).Humanize()} if you want to restart and try again.");
                            } else {
                                await coopChannel.SendMessageAsync($"Co-op {coop.Name} failed to reach all the goals and the contract is no longer available.");
                            }
                            coop.Status = CoopStatusEnum.Failed;
                            await _db.SaveChangesAsync();

                            try {
                                var coopFailedCategory = await _client.GetCategoryAsync(GuildChannelType.FailedCategory, guild);
                                await coopChannel.ModifyAsync(x => { x.CategoryId = coopFailedCategory.Id; });
                            } catch(Exception) {

                            }

                            await HandleUnjoins(usersNotJoined, guild, users, dbguild, coop, _db, coopChannel);

                        }

                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();


                        var emojis = "";




                        var missingCount = coop.UserCoopsXrefs.Count(x => !x.JoinedCoop && users.FirstOrDefault(u => u.Id == x.GetID())?.GuildId == coop.GuildId);

                        if(missingCount == 0) {
                            await HandlePingOnFull(usersWithStatus, coopChannel);
                        }

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
                                    (timeRemaining.TotalHours < 24)
                                    || status.SecondsRemaining > 0 && status.SecondsRemaining < TimeSpan.FromHours(24).TotalSeconds
                                )
                            ) {
                                emojis += "🔺";
                            }
                        } else if(
                                !coop.Finished && (
                                    (timeRemaining.TotalHours < 3)
                                    || status.SecondsRemaining > 0 && status.SecondsRemaining < TimeSpan.FromHours(6).TotalSeconds
                                ) && (coop.LastStatusUpdate?.Participants.Count ?? 0) < coop.Contract.Details.MaxCoopSize && !status.Public
                            ) {
                            emojis += "🔘";
                        }

                        Color color = Color.DarkGrey;
                        if(coop.Status == CoopStatusEnum.Failed) {
                            emojis += "🚩";
                        } else if(coop.Finished) {
                            emojis += "🏁";
                        } else {

                            var percent = status.ProjectedPercent(coop.Contract, (int?)coop.League ?? 0);

                            if(percent < .6) {
                                color = Color.Red;
                                emojis += "🔴";
                            } else if(percent < .9) {
                                color = new Color(139, 69, 19);
                                emojis += "🤎";
                            } else if(percent < 1) {
                                color = Color.Orange;
                                emojis += "🧡";
                            } else if(percent < 1.05) {
                                color = new Color(255, 255, 0);
                                emojis += "💛";
                            } else {
                                color = Color.Green;
                                emojis += "💚";
                            }
                        }

                        if(missingFromServer) {
                            emojis += "👻";
                        }

                        if(usersWithStatus.Any(x => x.User == null) && !status.Public && !coop.Finished) {
                            emojis += "👽";
                        }

                        if(coop.UserCoopsXrefs.Count(x => users.Any(y => y.Id == x.GetID() && y.GuildId != 0)) + usersWithStatus.Count(u => u.Xref == null) > coop.Contract.MaxUsers) {
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


                        times.Add(sw.ElapsedMilliseconds);
                        sw.Restart();

                        coop.LastStatusUpdate = status;



                        var embedBuilder = new EmbedBuilder()
                            .WithDescription(
                                (status.Finished(coop.Contract, league)
                                ? "This co-op is finished!"
                                : status.ProjectedToFinish(coop.Contract, league)
                                ? "This co-op is projected to succeed without growth as long as there are no sleepers!"
                                : "") + $"\n[View on egg9000.com](https://egg9000.com/coop/{coop.ContractID}/{coop.Name})"
                            )
                            .WithColor(color)
                            .WithTimestamp(DateTimeOffset.UtcNow)
                            .WithAuthor(new EmbedAuthorBuilder().WithName($"{coop.Contract.Name} - Coop Code: {coop.Name}").WithIconUrl(EggIncEggs.GetEggById((int)coop.Contract.Details.Egg).Image))
                            ;


                        var updates = UpdateInterval.TotalMinutes;
                        if(coop.Finished)
                            updates *= 60;
                        embedBuilder.WithFooter($"Updates Every {updates} Minute{(updates > 1 ? "s" : "")} - Last Updated");




                        var ends = TimeSpan.FromSeconds(status.SecondsRemaining).Humanize(precision: 2).ShortenTime();

                        if(status.SecondsRemaining <= 0)
                            ends = $"Expired {ends} ago";



                        for(int i = 0; i < 3; i++) {
                            if(coop.Contract.Details.GoalSets[league].Goals.Count > i) {
                                var goal = coop.Contract.Details.GoalSets[league].Goals[i];
                                var title = $"Goal {i + 1} ";
                                var time = "";
                                var goalRemaingAmount = goal.TargetAmount - amountWithOffline;
                                var goalRemaingTime = goalRemaingAmount / totalRate;
                                time = $"\nTime: {Prefarm.GetTimeRemaining(goal.TargetAmount, totalRate, amountWithOffline)}";
                                if(status.TotalAmount > goal.TargetAmount) {
                                    title += "✅";
                                    time = "";
                                } else if(coop.Status == CoopStatusEnum.Failed) {
                                    title += "❌";
                                    time = "";
                                } else if(status.Projected > goal.TargetAmount) {
                                    title += "☑";
                                }
                                embedBuilder.AddField(title, $"Target: {goal.TargetAmount.ToEggString()}\nReward: {EggIncEggs.GetReward(goal)}{time}", true);
                            } else {
                                embedBuilder.AddField("\u17B5", "\u17B5", true);
                            }
                        }


                        var totalRatePerHour = totalRate * 60 * 60;
                        if(coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                            embedBuilder.AddField("Co-op Expires", ends, inline: true);

                            if(remainingAmount > 0) {
                                var remainingTime = remainingAmount / totalRate;
                                if(remainingTime < TimeSpan.MaxValue.TotalSeconds) {
                                    try {
                                        var timeSpan = TimeSpan.FromSeconds(remainingTime);
                                        embedBuilder.AddField("Time To Complete", Prefarm.GetTimeRemaining(targetAmount, totalRate, amountWithOffline), inline: true);
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
                            } else if(!status.Finished(coop.Contract, league)) {
                                embedBuilder.AddField("Time To Complete", "Once everyone checks in", inline: true);
                            }

                            embedBuilder.AddField("Projected Amount", $"{status.Projected.ToEggString()} of {targetAmount.ToEggString()} {Math.Round(100 * status.Projected / targetAmount)}%", inline: true);
                            embedBuilder.AddField("Current Amount", status.TotalAmount.ToEggString(), inline: true);
                            embedBuilder.AddField("Current With Offline", amountWithOffline.ToEggString(), inline: true);
                            embedBuilder.AddField("Egg Laying Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                        } else if(coop.Status == CoopStatusEnum.Completed) {
                            embedBuilder.AddField("Final Amount", status.TotalAmount.ToEggString(), inline: true);
                            embedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                        } else if(coop.Status == CoopStatusEnum.Failed) {
                            embedBuilder.AddField("Final Amount", status.TotalAmount.ToEggString(), inline: true);
                            embedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                        }


                        await UpdateChannel(msgs, embedBuilder.Build(), coopChannel, coop, statusReponse.DiscordMessages);
                    } else {
                        if(string.IsNullOrEmpty(coop.UpdateMessagesId)) {
                            await coopChannel.SendMessageAsync("Error getting status");
                        }
                        Console.WriteLine($"Error getting status {coop.Name}");
                    }

                    try {
                        await _db.SaveChangesAsync();
                    } catch(Exception) {
                        await _db.SaveChangesAsync();
                    }


                    times.Add(sw.ElapsedMilliseconds);
                    sw.Stop();


                } catch(Exception e) {
                    Console.WriteLine($"Error in co-op {coop.Name}: {e.Message}");
                }
            }

        }

        public static int GetDigit(int number, int digit) {
            for(var i = 0; i < digit - 1; i++)
                number /= 10;
            return number % 10;
        }

        public async Task HandleSleeping(UserWithStatus user, Ei.ContractCoopStatusResponse status, UserCoopXref xref, ITextChannel coopChannel, Coop coop, Ei.ContractCoopStatusResponse.Types.ContributionInfo userCoopStatus, ApplicationDbContext _db, Guild dbguild, SocketGuild guild) {
            userCoopStatus.LastActive = DateTimeOffset.UtcNow - xref.LastStatusTime.Value;
            if(user == null || status.SecondsRemaining <= 0 || !xref.LastStatusTime.HasValue || coop.Finished || coop.Status == CoopStatusEnum.Failed)
                return;

            user.Sleeping = DateTimeOffset.UtcNow - xref.LastStatusTime.Value;
            float hoursSleeping = (float)user.Sleeping.Value.TotalHours;
            float siloTimeHours = (float)(user.SiloTime / 60.0);
            var alertTime = (30.0 - siloTimeHours) / 2 + siloTimeHours;
            bool needsAlert = hoursSleeping >= alertTime;
            var timeEmpty = Math.Round(hoursSleeping - siloTimeHours, 2);
            var currentSleep = xref.SleepTracking.FirstOrDefault(x => !x.WokeUp);

            if(currentSleep == null && needsAlert) {
                currentSleep = new SleepTracking { SleepStart = xref.LastStatusTime.Value, LastChecked = DateTimeOffset.Now };

                var messages = BotText.SleepingMessages;
                var random = new Random();
                var index = random.Next(messages.Count);

                if(user.DiscordUser != null) {
                    var warningText = messages[index].Replace("@name", user.DiscordUser.Mention + (timeEmpty < 0 ? $" [Empty silos in {timeEmpty} hours {coopChannel.Mention}]" : $" [Silos have been empty for {timeEmpty} hours {coopChannel.Mention}]"));
                    var dmChannel = await user.DiscordUser.CreateDMChannelAsync();
                    try {
                        await dmChannel.SendMessageAsync(warningText);
                    } catch(Exception) {
                        await coopChannel.SendMessageAsync($"{warningText} (DMs are blocked)");
                        Console.WriteLine($"Unable to send DM to {user.DiscordUser.GetCleanName()}");
                    }

                }
                if(dbguild.DemeritLogChannel.HasValue) {
                    //if(_demeritChannel == null || _demeritChannel.Id != dbguild.DemeritLogChannel.Value) {
                    //    _demeritChannel = _client.GetGuild(coop.GuildId).GetTextChannel(dbguild.DemeritLogChannel.Value);
                    //}
                    //await _demeritChannel.SendMessageAsync(warningText + $" {coopChannel.Mention}");
                }
                xref.SleepTracking.Add(currentSleep);
            }

            if(currentSleep != null) {
                if(xref.LastStatusTime > currentSleep.SleepStart) {
                    //No longer sleeping
                    currentSleep.WokeUp = true;
                    currentSleep.TotalHoursEmpty = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours - siloTimeHours;
                    xref.TotalHoursSleeping = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours;
                    xref.HoursSleeping = 0;
                } else {
                    var nextDemeritAt = (currentSleep.DemeritsGiven + 1) * 24;
                    bool needsDemerit = timeEmpty > nextDemeritAt && dbguild.DemeritLogChannel.HasValue && !xref.NoDemerit;
                    if(needsDemerit) {
                        currentSleep.DemeritsGiven++;

                        var demerit = new Demerit {
                            When = DateTimeOffset.Now,
                            AdminUserId = Guid.Empty,
                            UserId = user.User.Id,
                            Id = Guid.NewGuid(),
                            Reason = $"Empty silos for {nextDemeritAt} hours in {coop.Contract.Name}"
                        };
                        _db.Demerit.Add(demerit);
                        await _db.SaveChangesAsync();

                        var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.User.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();
                        var demeritText = $"Demerit added to {user.DiscordUser?.Mention ?? user.User.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                        await coopChannel.SendMessageAsync(demeritText);
                        if(count >= 3)
                            demeritText = $"**{demeritText}**";
                        if(_demeritChannel == null || _demeritChannel.Id != dbguild.DemeritLogChannel.Value) {
                            _demeritChannel = _client.GetGuild(coop.GuildId).GetTextChannel(dbguild.DemeritLogChannel.Value);
                        }
                        await _demeritChannel.SendMessageAsync($"{demeritText} {coopChannel.Mention}");
                    }
                    xref.HoursSleeping = (int)Math.Floor((DateTimeOffset.Now - currentSleep.SleepStart).TotalHours);
                }

                if(!currentSleep.WokeUp) {
                    currentSleep.LastChecked = DateTimeOffset.Now;
                }
                xref.SleepTracking = xref.SleepTracking;
            }
        }

        public async Task HandleUnjoins(List<UserCoopXref> usersNotJoined, SocketGuild guild, List<DBUser> users, Guild dbguild, Coop coop, ApplicationDbContext _db, ITextChannel coopChannel) {
            if(!dbguild.DemeritLogChannel.HasValue)
                return;
            foreach(var xref in usersNotJoined) {
                var user = users.FirstOrDefault(x => x.Id == xref.GetID());
                if(user == null || xref.NoDemerit)
                    continue;
                var discordUser = guild.GetUser(user.DiscordId);
                //if(discordUser == null)
                    //continue;

                if(xref.CreatedOn > DateTimeOffset.Now.AddHours(-24)) {
                    _db.Remove(xref);
                    await _db.SaveChangesAsync();
                    await coopChannel.SendMessageAsync($"{discordUser?.GetCleanName() ?? user.DiscordUsername} returned to prefarming pool since they were added less than 24 hours before the co-op finished.");
                    continue;
                }

                if(user.Registered > DateTimeOffset.Now.AddDays(-7)) {
                    //_db.Remove(xref);
                    await coopChannel.SendMessageAsync($"{discordUser?.Mention ?? user.DiscordUsername}, you failed to join this co-op. After your first week in this server you will get a demerit for failing to join an assigned co-op. Ask staff if you have any questions.");
                    continue;
                }


                await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name}", user, _db, xref, discordUser, coopChannel, dbguild, coop);
            }
        }

        public async Task HandlePingOnFull(List<UserWithStatus> usersWithStatus, ITextChannel coopChannel) {
            foreach(var userStatus in usersWithStatus.Where(x => x.Xref?.PingOnFull ?? false)) {
                userStatus.Xref.PingOnFull = false;
                var dmChannel = await userStatus.DiscordUser.CreateDMChannelAsync();
                try {
                    await dmChannel.SendMessageAsync($"All users have joined the co-op {coopChannel.Mention}");
                } catch(Exception) {
                    Console.WriteLine($"Unable to send DM to {userStatus.DiscordUser.GetCleanName()}");
                }

            }
        }

        public async Task SendDMWarning(SocketGuildUser discordUser, ITextChannel coopChannel, string Message, Coop coop) {
            try {
                var dmChannel = await discordUser.CreateDMChannelAsync();
                await dmChannel.SendMessageAsync($"{Message} {coopChannel.Mention} for {EggIncEggs.GetEggById((int)coop.Contract.Details.Egg).Image} {coop.ContractID}");
            } catch(HttpException) {
                await coopChannel.SendMessageAsync($"{Message} (User has blocked DMs from bot)");
            }
        }

        public async Task AddDemeritAndRemoveFromCoop(string reason, DBUser user, ApplicationDbContext _db, UserCoopXref xref, SocketGuildUser discordUser, ITextChannel coopChannel, Guild dbguild, Coop coop) {
            var existingDemerit = await _db.Demerit.AnyAsync(x => x.ContractID == coop.ContractID && x.UserId == user.Id);
            if(existingDemerit) {
                await coopChannel.SendMessageAsync($"Removing {discordUser?.Mention ?? user.DiscordUsername} due to: {reason} (They have already received a demerit for this contract)");
                return;
            }
            var demerit = new Demerit {
                When = DateTimeOffset.Now,
                AdminUserId = Guid.Empty,
                UserId = user.Id,
                Id = Guid.NewGuid(),
                Reason = reason
            };
            _db.Demerit.Add(demerit);
            _db.Remove(xref);
            await _db.SaveChangesAsync();

            var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();
            var demeritText = $"Demerit added to {discordUser?.Mention ?? user.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
            await coopChannel.SendMessageAsync(demeritText);
            if(count >= 3)
                demeritText = $"**{demeritText}**";
            if(_demeritChannel == null || _demeritChannel.Id != dbguild.DemeritLogChannel.Value) {
                _demeritChannel = _client.GetGuild(dbguild.Id).GetTextChannel(dbguild.DemeritLogChannel.Value);
            }
            await _demeritChannel.SendMessageAsync(demeritText + $" {coopChannel.Mention}");

        }
    }
}