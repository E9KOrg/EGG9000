using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using Humanizer;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Helpers;
using Ei;
using EGG9000.Common.Migrations;
using Polly;
using Microsoft.Data.SqlClient;
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class ContractUpdater : _UpdaterBase<ContractUpdater> {
        public static TimeSpan _updateInterval = TimeSpan.FromMinutes(10);
        private APILink _apiLink;
        public static List<UserX> _users;
        private Words _words;

        private Polly.Retry.AsyncRetryPolicy sqlExcpetionPolicy = Policy.Handle<SqlException>().WaitAndRetryAsync(new[]{
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(3)
                });

        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public ContractUpdater(
            APILink apilink,
            Words words,
            IServiceProvider provider
        ) : base(_updateInterval, TimeSpan.Zero, provider) {
            _users = new List<UserX>();
            _apiLink = apilink;
            _words = words;
        }



        public override async Task Run(object state, CancellationToken cancellationToken) {
            var showTimings = false;

            var totalStopwatch = new Stopwatch();
            totalStopwatch.Start();

            Console.WriteLine("Starting Contract Update");
            var sw = new Stopwatch();
            sw.Start();

            var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            var guildContracts = await _db.GuildContracts.Include(x => x.Contract).Where(x => !x.DeletedChannel).ToListAsync();

            if(showTimings)
                Console.WriteLine($"Contracts: {sw.ElapsedMilliseconds}");
            sw.Restart();

            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

            if(showTimings)
                Console.WriteLine($"Guilds: {sw.ElapsedMilliseconds}");
            sw.Restart();

            var guildGroups = guildContracts.GroupBy(x => x.GuildID);

#if DEBUG
            //guildGroups = guildGroups.Where(x => x.Key == 770469712064151593);
#endif

            foreach(var dbguild in dbguilds) {
                //foreach(var groupGuildContracts in guildGroups.OrderBy(x => new Guid())) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild == null)
                    continue;

                Console.WriteLine($"Running Contracts for {guild.Name}");
                var dbusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync();

                if(showTimings)
                    Console.WriteLine($"DBUsers: {sw.ElapsedMilliseconds}");
                sw.Restart();

#if DEBUG
                var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                    User = y,
                    Backup = x
                })).ToList();
#else
                var backups = await _apiLink.GetUserBackups(dbusers, _db);
#endif


                if(showTimings)
                    Console.WriteLine($"Backups: {sw.ElapsedMilliseconds}");
                sw.Restart();

                var groupGuildContracts = guildGroups.FirstOrDefault(x => x.Key == dbguild.DiscordSeverId);
                //var contractIds = groupGuildContracts.Select(x => x.ContractID);




                if(showTimings)
                    Console.WriteLine($"Get Coops: {sw.ElapsedMilliseconds}");
                sw.Restart();
                //var dbguild = dbguilds.First(x => x.Id == guild.Id);
                if(groupGuildContracts is not null) {
                    foreach(var guildContract in groupGuildContracts.OrderByDescending(x => x.Created)) {
                        await UpdateContractChannel(_db, backups, guildContract, guild, dbguild, dbusers);
                    }
                }
                await ShipReturnDM.UpdateNextShipDM(dbusers, _db);
            }


            //await leaderboardTask;
            await _db.SaveChangesAsync();

            if(showTimings)
                Console.WriteLine($"Saving DB: {sw.ElapsedMilliseconds}");
            sw.Restart();



            Console.WriteLine($"Finished Contract Channels Update {Math.Round(totalStopwatch.ElapsedMilliseconds / 1000.0 / 60.0, 1)}mins");
        }

        public async Task UpdateContractChannel(ApplicationDbContext _db, List<LeaderboardUser> backups, GuildContract guildContract, SocketGuild guild, Guild dbguild, List<DBUser> dbusers, FauxCommand slashCommand = null) {
            try {
                List<Coop> coops;
                try {
                    coops = await sqlExcpetionPolicy.ExecuteAsync(async () => await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guild.Id && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync());
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    Console.WriteLine($"*** Error getting coops in contract updated: {e.Message}");
                    return;
                }
                //var coops = allCoops.Where(x => x.ContractID == guildContract.ContractID && x.League == (guildContract.Elite ? 0 : 1)).ToList();
                //await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();





                Console.WriteLine($"Working on GuildContract for {guildContract.GuildID} - {guildContract.Contract.ID}");


                var channel = guild.TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);
                if(channel == null) {
                    if(guildContract.Created < DateTimeOffset.Now.AddDays(-45)) {
                        guildContract.DeletedChannel = true;
                        await _db.SaveChangesAsync();
                    }
                    Console.WriteLine($"Missing Channel for {guildContract.ContractID}");
                    return;
                }

                var validFor = (DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now);
                var league = guildContract.Elite ? 0 : 1;
                var targetAmount = guildContract.Contract.Details.GoalSets[league].Goals.Last().TargetAmount;

                var newMsgs = new List<string>();

                ////var alienBackups = await GetBackupsForAliens(coops, allPreFarms, guildContract.Contract, _apiLink);
                //var alienBackups = new List<UserPreFarm>();

                var usersWithBackups = backups.Select(x => new UserWithBackup { Backup = x.Backup, User = x.User }).ToList();
                var coopsBreakdown = GetBreakdown(coops, usersWithBackups, guildContract, _client);

                ShowCurrentCoops(guildContract, coopsBreakdown.ExistingCoops, channel, newMsgs, targetAmount, false);


                var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>((guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards) ?? "[]");
                var activeUsers = JsonConvert.DeserializeObject<List<GuildUser>>((guildContract.Elite ? dbguild.ActiveElites : dbguild.ActiveStandards) ?? "[]");

                await UpdateContractChannelName(guildContract, coopsBreakdown, channel);

                if(!guildContract.Contract.Details.CoopAllowed) {
                    newMsgs.AddRange(ShowSoloStatus(coopsBreakdown, guildContract.Contract.Details, targetAmount));
                } else {
                    for(int i = 1; i <= coopsBreakdown.PotentialCoops.Count; i++) {
                        var coop = coopsBreakdown.PotentialCoops[i - 1];

                        var name = $"Coop {i + coops.Count}";
                        name += $" PreFarming, Expire:{coop.CoopParticipants.Min(x => x.TimeLeft).Humanize(precision: 2).ShortenTime()}";

                        if(coop.CoopParticipants.Sum(x => x.Projected) / targetAmount > 1) {
                            //var timeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, coop.Users.Sum(x => x.Rate), coop.Users.Sum(x => x.EggsPaidFor + x.OfflineEggs));
                            name += $" Completes: {coop.TimeRemaining.Humanize(2).ShortenTime()}";
                        }

                        if(coop.CoopParticipants.Count > 0) {
                            newMsgs.AddRange(ShowCoopStatus(coop, name, targetAmount, guildContract.Contract.Details.MaxCoopSize));
                        }
                    }
                }

                if(coopsBreakdown.ExpiredFarms.Count > 0 && guildContract.Contract.Details.CoopAllowed) {
                    newMsgs.AddRange(ShowCoopStatus(new CoopDetails(coopsBreakdown.ExpiredFarms.OrderBy(x => x.Name).ToList(), guildContract), "Expired Farms", targetAmount, guildContract.Contract.Details.MaxCoopSize));
                }

                if(coopsBreakdown.AlreadyInCoop.Count > 0) {
                    //foreach(var u in coopsBreakdown.AlreadyInCoop) {
                    //    if(u.Completed) {
                    //        u.Farm.CoopId = "Completed";
                    //    } else {
                    //        u.Farm.CoopId += $" Joined {(DateTimeOffset.Now - u.DBUser.Registered.Value).Humanize().ShortenTime()} ago";
                    //    }
                    //}
                    var alreadyInCoop = new CoopDetails(coopsBreakdown.AlreadyInCoop.Where(x => x.DBUser.Registered < guildContract.Created).OrderBy(x => x.Completed).ToList(), guildContract);
                    newMsgs.AddRange(ShowCoopStatus(alreadyInCoop, $"Already in coop", targetAmount, 0, true));
                }

                //if(coopsBreakdown.Completed.Users.Count > 0 && coopsBreakdown.Completed.Users.Count < 40) {
                //    newMsgs.Add($"\nThe following users have already completed the contract.\n{String.Join(", ", coopsBreakdown.Completed.Users.Select(x => x.Name))}\n");
                //}


                var finalMsg = "";



                var activesNotParticipanting = activeUsers.Where(au =>
                    !coopsBreakdown.PotentialCoops.Any(c => c.CoopParticipants.Any(u => u.EggIncId == au.EggIncId)) &&
                    !coopsBreakdown.AlreadyInCoop.Any(u => u.EggIncId == au.EggIncId) &&
                    !coopsBreakdown.Completed.Any(u => u.EggIncId == au.EggIncId)).OrderBy(x => x.DiscordName
                ).ToList();

                var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");

                //var skipping = activesNotParticipanting.Where(x => skipList.Any(y => x.DiscordId == y) && !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();

                //if(skipping.Count > 0) {
                //    finalMsg += $"\nThe following people are set to skip this contract. {string.Join(", ", skipping.Select(x => x.DiscordName))}\n";
                //}

                var skippingWhilePrefarming = activesNotParticipanting.Where(x => skipList.Any(y => x.DiscordId == y) && coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
                if(skippingWhilePrefarming.Count > 0) {
                    finalMsg += $"\nThe following people are set to skip this contract **but are still prefarming**. {string.Join(", ", skippingWhilePrefarming.Select(x => x.DiscordName))}\n";
                }

                var usersNoLongerOnBreak = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants.Where(y => y.DBUser.OnBreakSince.HasValue && y.Started.HasValue && y.Started > y.DBUser.OnBreakSince)).ToList();
                if(usersNoLongerOnBreak.Count > 0) {
                    usersNoLongerOnBreak.ForEach(x => x.DBUser.OnBreakSince = null);
                }

                activesNotParticipanting = activesNotParticipanting.Where(x => !x.OnBreakSince.HasValue && !skipList.Any(y => x.DiscordId == y) && !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();

                activesNotParticipanting = activesNotParticipanting.Where(x => {
                    var completed = usersWithBackups.Any(y => y.User.DiscordId == x.DiscordId &&
                     (y.Backup.Farms.Any(f => f.ContractId == guildContract.ContractID && f.Completed)
                     || y.Backup.ArchivedFarms.Any(f => f.ContractId == guildContract.ContractID && f.Completed)
                    ));
                    return !completed;
                }).ToList();

                if(activesNotParticipanting.Count > 0) {
                    var goals = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Select(x => x.RewardType);
                    //var PEAward = guildContract.Contract.Details.GoalSets.Any(x => x.Goals.Any(y => y.RewardType == RewardType.EggsOfProphecy));
                    //var ArtifactAward = guildContract.Contract.Details.GoalSets.Any(x => x.Goals.Any(y => y.RewardType == RewardType.Artifact || y.RewardType == RewardType.ArtifactCase));
                    //var PiggyAward = guildContract.Contract.Details.GoalSets.Any(x => x.Goals.Any(y => y.RewardType == RewardType.PiggyMultiplier));

                    var usersNotSkip = dbusers.Where(x =>
                        x.PingForRewards.Any(y => y == RewardType.UnknownReward || goals.Contains(y))
                    ).Select(x => x.Id).ToList();
                    activesNotParticipanting = activesNotParticipanting.Where(x => usersNotSkip.Contains(x.DatabaseId)).ToList();


                    if(DateTimeOffset.Now >= DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime)) {
                        //finalMsg += $"\nThe following people did not do the contract. {string.Join(", ", activesNotParticipanting.Select(x => x.DiscordName))}\n";
                    } else if(validFor.TotalDays >= 2) {
                        finalMsg += $"\nThe following people have not started prefarming yet. {string.Join(", ", activesNotParticipanting.Select(x => x.Mention))}\n";
                    }

                }


                var expectedCount = coopsBreakdown.PotentialCoops.Sum(x => x.CoopParticipants.Count) + activesNotParticipanting.Count;

                finalMsg += $"Expected Participants: {expectedCount}, Current Capacity: {coopsBreakdown.PotentialCoops.Count * guildContract.Contract.Details.MaxCoopSize}, Currently Prefarming: {coopsBreakdown.PotentialCoops.Sum(x => x.CoopParticipants.Count)}, Co-op Count: {coopsBreakdown.PotentialCoops.Count}\n";

                var spotsInPotentialCoops = coopsBreakdown.PotentialCoops.Count * guildContract.Contract.Details.MaxCoopSize - coopsBreakdown.PotentialCoops.Sum(x => x.CoopParticipants.Count);
                var spotsUnder24 = coopsBreakdown.ExistingCoops.Where(x => !x.Coop.FinishedOrFailed && x.TimeRemaining <= TimeSpan.FromHours(24)).Sum(x => guildContract.Contract.Details.MaxCoopSize - x.CoopParticipants.Count);
                var spotsOver24 = coopsBreakdown.ExistingCoops.Where(x => !x.Coop.FinishedOrFailed && x.TimeRemaining > TimeSpan.FromHours(24)).Sum(x => guildContract.Contract.Details.MaxCoopSize - x.CoopParticipants.Count);
                finalMsg += $"Spots Potential: {spotsInPotentialCoops}, {(spotsUnder24 > 0 ? "**" : "")}Spots <24h: {spotsUnder24}{(spotsUnder24 > 0 ? "**" : "")}, Spots >24H: {spotsOver24}\n\n";



                while(finalMsg.Length > 2000) {
                    var index = finalMsg.LastIndexOf(", ", 2000);
                    newMsgs.Add(finalMsg.Substring(0, index));
                    finalMsg = finalMsg.Substring(index);
                }
                newMsgs.Add(finalMsg);


                var description = $"[View Co-ops on egg9000.com](https://egg9000.com/Contract/Details?GuildId={guild.Id}&ContractId={guildContract.ContractID}&Elite={guildContract.Elite.ToString()})\n";
                description += $"**Size** {guildContract.Contract.MaxUsers}, **<:Token_boost:724397091211968604>** {guildContract.Contract.Details.MinutesPerToken}mins,";
                description += $"**Length** {guildContract.Contract.ContractTime.Humanize(precision: 2).ShortenTime()}, **{(validFor > TimeSpan.Zero ? "Expires" : "Expired")}** {validFor.Humanize(precision: 2).ShortenTime()}";

                var embedBuilder = new EmbedBuilder()
                    .WithDescription(description)
                    .WithTimestamp(DateTimeOffset.Now)
                    .WithFooter($"Updates Every {_updateInterval.TotalMinutes} Minutes - Last Updated")
                    .WithAuthor(
                        new EmbedAuthorBuilder().WithName($"{guildContract.Contract.Name} - {guildContract.Contract.ID}")
                        .WithIconUrl(EggIncEggs.GetEggById((int)guildContract.Contract.Details.Egg).Image));


                for(int i = 0; i < 3; i++) {
                    if(guildContract.Contract.Details.GoalSets[league].Goals.Count > i) {
                        var goal = guildContract.Contract.Details.GoalSets[league].Goals[i];
                        var title = $"Goal {i + 1} - {goal.TargetAmount.ToEggString()}";
                        embedBuilder.AddField(title, $"{EggIncEggs.GetReward(goal)}", true);
                    } else {
                        //embedBuilder.AddField("\u17B5", "\u17B5", true);
                    }
                }

                var condensedMsgs = new List<string>();

                var currentMsg = "";
                for(var i = 0; i < newMsgs.Count; i++) {
                    var msg = newMsgs[i];
                    var cmsg = msg;
                    if(currentMsg.Length + cmsg.Length > 2000) {
                        condensedMsgs.Add(currentMsg);
                        currentMsg = "";
                        while(cmsg.Length > 2000) {
                            var index = Math.Max(cmsg.LastIndexOf('\n', 2000), cmsg.LastIndexOf(',', 2000));
                            condensedMsgs.Add(cmsg.Substring(0, index));
                            cmsg = cmsg.Substring(index);

                        }
                    }
                    currentMsg += cmsg;
                }
                condensedMsgs.Add(currentMsg);




                var existingMessages = (await channel.GetMessagesAsync(limit: 1000).FlattenAsync()).ToList();


                var nonBotMessages = existingMessages.Where(x => !x.Author.IsBot || x.Interaction?.Type == InteractionType.ApplicationCommand).ToList();
                if(nonBotMessages.Count > 0)
                    await channel.DeleteMessagesAsync(nonBotMessages);


                existingMessages = existingMessages.Where(x => x.Author.IsBot).OrderBy(x => x.CreatedAt).ToList();

                var condensedMsgCount = condensedMsgs.Count;

                if(existingMessages.Count == 0) {
                    var reserveMessages = (int)((activeUsers.Count - coopsBreakdown.Completed.Count) / 30);
                    Console.WriteLine($"Reserve: {reserveMessages} Actual: {condensedMsgCount}");
                    if(existingMessages.Count == reserveMessages + 1) {
                        reserveMessages = existingMessages.Count;
                    }
                    for(var i = condensedMsgs.Count; i < reserveMessages; i++) {
                        condensedMsgs.Add("\u17B5"); //Reserve extra messages
                    }
                }

                IMessage notStarted = null;

                var times = new List<TimeSpan>();
                for(int i = 0; i < Math.Max(existingMessages.Count(), condensedMsgs.Count); i++) {
                    if((i + 1) % 10 == 0 && slashCommand is not null) {
                        await slashCommand.ModifyOriginalResponseAsync(x => x.Content = $"Updating contract message {i + 1} of {Math.Max(existingMessages.Count(), condensedMsgs.Count)} {string.Join(",", times.Select(x => x.Humanize()))}");
                        times = new List<TimeSpan>();
                    }
                    if(existingMessages.Count() > i && condensedMsgs.Count > i) {
                        var isLastMessage = i == condensedMsgCount - 1;

                        var sw = new Stopwatch();
                        sw.Start();
                        var success = false;
                        while(!success) {
                            var changes = existingMessages.ElementAt(i).Content.CompareChangesNew(condensedMsgs[i]);

                            if(existingMessages.ElementAt(i).Embeds.Count > 0 || changes > 20 || isLastMessage) {
                                try {
                                    await ((RestUserMessage)existingMessages.ElementAt(i)).ModifyAsync(msg => { msg.Content = condensedMsgs[i]; msg.Embed = isLastMessage ? embedBuilder.Build() : null; });
                                    await Task.Delay(505);
                                    success = true;
                                } catch(Discord.Net.HttpException e) {
                                    if(e.DiscordCode == DiscordErrorCode.UnknownMessage) {
                                        existingMessages.RemoveAt(i);
                                    } else {
                                        success = true;

                                    }
                                }
                            } else {
                                success = true;
                            }
                        }
                        if(notStarted == null && condensedMsgs[i].Contains("PreFarming")) {
                            notStarted = existingMessages.ElementAt(i);
                            embedBuilder.Description = GetLinkToMessage(notStarted, guild, channel, "Jump to top not started co-op (for staff)") + embedBuilder.Description;
                        }
                        sw.Stop();
                        times.Add(sw.Elapsed);
                    } else if(existingMessages.Count() <= i) {
                        if(i == condensedMsgCount - 1) {
                            await channel.SendMessageAsync(condensedMsgs[i], embed: embedBuilder.Build());
                            await Task.Delay(505);
                        } else {
                            var message = await channel.SendMessageAsync(condensedMsgs[i]);
                            await Task.Delay(505);
                            if(notStarted == null && condensedMsgs[i].Contains("PreFarming")) {
                                notStarted = message;
                                embedBuilder.Description = GetLinkToMessage(notStarted, guild, channel, "Jump to top not started co-op (for staff)") + embedBuilder.Description;
                            }
                        }
                    } else {
                        try {
                            await ((RestUserMessage)existingMessages.ElementAt(i)).DeleteAsync();
                            await Task.Delay(505);
                        } catch(Exception) {
                        }
                    }
                }
            } catch(Exception e) {
                Console.WriteLine($"Error Updating Contracts Channel {e.Message}");
                _bugsnag.Notify(e);
            }

        }

        public void ShowCurrentCoops(GuildContract guildContract, List<CoopDetails> coopsDetails, SocketTextChannel channel, List<string> newMsgs, double targetAmount, bool allStarted) {
            if(guildContract.Status != ContractStatus.Completed && coopsDetails.All(x => x.Coop.Status == CoopStatusEnum.Completed || x.Coop.Finished) && allStarted) {
                guildContract.Status = ContractStatus.Completed;
            }

            for(int i = 1; i <= coopsDetails.Count; i++) {
                var coopDetails = coopsDetails[i - 1];

                if(coopDetails.Coop.DeletedChannel || !coopDetails.HasSpots || coopDetails.Coop.Finished)
                    continue;

                if(coopDetails.Coop.Finished && coopDetails.Coop.Status != CoopStatusEnum.Completed) {
                    coopDetails.Coop.Status = CoopStatusEnum.Completed;
                }


                var name = coopDetails.Coop.Status switch {
                    CoopStatusEnum.WaitingOnStarter => $"Coop {i} - Waiting on bot to start",
                    CoopStatusEnum.WaitingOnAssigned => $"Coop {i} - Waiting on users",
                    CoopStatusEnum.AllAssignedJoined => $"Coop {i}",
                    CoopStatusEnum.Full => $"Coop {i}",
                    CoopStatusEnum.Completed => $"Coop {i} completed! 🎆",
                    _ => ""
                };

                if(!coopDetails.Coop.Finished) {
                    if(coopDetails.TimeRemaining > TimeSpan.Zero) {
                        name += $" Completes: {coopDetails.TimeRemaining.Humanize(2).ShortenTime()}";
                    }
                    if(coopDetails.Coop.CoopEnds < DateTimeOffset.Now) {
                        name += $" Expired: {(coopDetails.Coop.CoopEnds - DateTimeOffset.Now).Value.Humanize(2).ShortenTime()} ago";
                    } else if((coopDetails.Coop.CoopEnds - DateTimeOffset.Now) < coopDetails.TimeRemaining) {
                        name += $" Expires: {(coopDetails.Coop.CoopEnds - DateTimeOffset.Now).Value.Humanize(2).ShortenTime()}";
                    }
                }

                var userIds = coopDetails.Coop.UserCoopsXrefs.Select(x => x.GetID());



                newMsgs.AddRange(ShowCoopStatus(coopDetails, name, targetAmount, guildContract.Contract.Details.MaxCoopSize));
            }

        }

        public string GetLinkToMessage(IMessage message, IGuild guild, ITextChannel channel, string text) {
            if(message == null)
                return "";
            return $"[{text}](https://discord.com/channels/{guild.Id}/{channel.Id}/{message.Id})\n";
        }

        public static string Truncate(string value, int maxLength) {
            if(string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private List<string> ShowCoopStatus(CoopDetails coop, string coopName, double target, uint size, bool alreadyInCoop = false) {
            var table = new List<List<FixedWidthCell>>();
            table.Add(new List<FixedWidthCell> {
                            new FixedWidthCell("Name", CellAlignment.Center),
                            new FixedWidthCell("🐔", CellAlignment.Center),
                            new FixedWidthCell("🥚", CellAlignment.Center),
                            new FixedWidthCell("📈", CellAlignment.Center, true),
                            new FixedWidthCell(coop.CoopParticipants.All(x => string.IsNullOrEmpty(x.Farm?.CoopId)) ? "🟡" : "")
                        });
            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            table.AddRange(coop.CoopParticipants.OrderByDescending(x => x.Projected).Select(x => {
                var timeleft = "";
                if(x.TimeLeft.TotalSeconds > 0) {
                    timeleft = x.TimeLeft.Humanize(precision: 2).ShortenTime();
                } else {
                    timeleft = "Time has run out";
                }
                var emoji = "";
                if(x.DiscordUser != null && x.DiscordUser.Roles.Any(r => r.Id == 796512753241161748)) {
                    emoji = "🆕";
                }
                return new List<FixedWidthCell> {
                            new FixedWidthCell(Truncate($"{emoji}{ebrgx.Replace( x.Name, "")}", 12)),
                            //new FixedWidthCell(x.EggsPaidFor.ToEggString()),
                            new FixedWidthCell(x.NumChickens.ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(ArgumentsHelper.NumberToString(x.Rate * 60 * 60, false, -1) + "/h", CellAlignment.Right),
                            new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
                            //new FixedWidthCell(x.Tokens.ToString()),
                            //new FixedWidthCell(x.BoostTokensSpent.ToString()),
                            //new FixedWidthCell(x.TimeSinceUpdate.Humanize(1, minUnit: Humanizer.Localisation.TimeUnit.Minute).ShortenTime()),
                            new FixedWidthCell(GetCoopStatus(coop.Coop, x, alreadyInCoop))
                    };
            }));

            if(coopName != "Expired Farms" && coopName != "Already in coop") {
                var percent = $"{coop.PercentProjected / 100:P0}".Replace(",", ""); //$"{coop.Users.Sum(x => x.Projected) / target:P0}";

                table.Add(new List<FixedWidthCell> {
                            new FixedWidthCell($"{coop.CoopParticipants.Count}/{size}"),
                            new FixedWidthCell(""),
                            new FixedWidthCell(ArgumentsHelper.NumberToString(coop.CoopParticipants.Sum(x => x.Rate) * 60 * 60, false, -1) + "/h", CellAlignment.Right),
//                            new FixedWidthCell(""),
                            new FixedWidthCell(coop.CoopParticipants.Sum(x => x.Projected).ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(percent, CellAlignment.Right),
                            new FixedWidthCell(""),
                            new FixedWidthCell(""),
                            new FixedWidthCell("")
                        });
            }



            var tableString = $"{coopName}\n```{FixedWidthTable.GetTable(table)}```\n";
            //var startLength = tableString.Length;
            //tableString = tableString.Replace("  ", "\t");
            //Console.WriteLine($"Original Length: {startLength}, Final Length: {tableString.Length}, Save: {startLength - tableString.Length}");
            var msgs = new List<string>();
            while(tableString.Length > 2000) {
                var index = tableString.LastIndexOf('\n', 2000);
                msgs.Add(tableString.Substring(0, index) + "```");
                tableString = "```" + tableString.Substring(index);
            }
            msgs.Add(tableString);
            return msgs;
        }

        private string GetCoopStatus(Coop coop, UserFarmDetails user, bool alreadyInCoop) {
            if(alreadyInCoop)
                return $"[{user.Farm?.CoopId ?? user.ArchivedFarm?.CoopName}] Joined {(DateTimeOffset.Now - user.DBUser.Registered.Value).Humanize().ShortenTime()} ago";
            if(coop is not null && user.InCoop && user.DBUser is null)
                return "👽";
            if(coop is not null && user.InCoop)
                return "✔️";
            if(coop is not null && !user.InCoop)
                return $"❌ {user.FarmExpires.Humanize().ShortenTime()}";
            if(!string.IsNullOrEmpty(user.Farm?.CoopId))
                return user.Farm.CoopId;
            if(user.Farm is not null)
                return (user.Farm.BoostTokensReceived - user.Farm.BoostTokensGiven - user.Farm.BoostTokensSpent).ToString();
            return "?";
        }

        private List<string> ShowSoloStatus(CoopsBreakdown coopsBreakdown, Ei.Contract contract, double target) {
            var table = new List<List<FixedWidthCell>>();
            table.Add(new List<FixedWidthCell> {
                            new FixedWidthCell("Name", CellAlignment.Center),
                            new FixedWidthCell("🐔", CellAlignment.Center),
                            new FixedWidthCell("🥚", CellAlignment.Center),
                            new FixedWidthCell("📈", CellAlignment.Center, true),
                            new FixedWidthCell(""),
                            new FixedWidthCell("⏲️", CellAlignment.Center)
                        });

            var participants = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).ToList();
            participants.AddRange(coopsBreakdown.ExpiredFarms);

            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");

            table.AddRange(participants.Where(x => x.NumChickens > 0).OrderBy(x => x.Name).Select(x => {
                return new List<FixedWidthCell> {
                            new FixedWidthCell(Truncate(ebrgx.Replace(x.Name, ""), 12)),
                            new FixedWidthCell(x.NumChickens.ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(ArgumentsHelper.NumberToString(x.Rate * 60 * 60, false, -1) + "/h", CellAlignment.Right),
                            new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(String.Format("{0:0%}", x.Projected/target) , CellAlignment.Right),
                            new FixedWidthCell(x.EggsShipped < target ?  Prefarm.GetTimeRemainingValue(target, x.Rate, x.EggsShipped).Humanize(1, null, Humanizer.Localisation.TimeUnit.Year).ShortenTime() : "Finished", CellAlignment.Right)
                    };
            }));


            var tableString = $"```{FixedWidthTable.GetTable(table)}```";
            var msgs = new List<string>();
            while(tableString.Length > 2000) {
                var index = tableString.LastIndexOf('\n', 2000);
                msgs.Add(tableString.Substring(0, index) + "```");
                tableString = "```" + tableString.Substring(index);
            }
            msgs.Add(tableString);
            return msgs;
        }

        public static async Task UpdateContractChannelName(GuildContract guildContract, CoopsBreakdown coopsBreakdown, SocketTextChannel channel) {
            var channelName = guildContract.Contract.Name.Split(":").Last().Trim().Replace(" ", "-");// + "_" +  guildContract.ContractID;
            var validFor = (DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now);


            if(DateTimeOffset.Now >= DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime)) {
                var emoji = "⛔";
                if(guildContract.Contract.Details.MaxCoopSize <= 1) {
                    emoji += "👤";
                } else if(coopsBreakdown.ExistingCoops.All(x => x.Coop.FinishedOrFailed)) {
                    emoji += "🏁";
                } else if(coopsBreakdown.ExistingCoops.All(x => x.Coop.FinishedOrFailed || !x.HasSpots || x.Coop.CoopEnds < DateTimeOffset.Now)) {
                    emoji += "✅";
                } else {
                    var count = coopsBreakdown.PotentialCoops.Sum(x => x.CoopParticipants.Count);
                    if(count > 0 && count <= 20) {
                        emoji += Convert.ToChar(9311 + count);
                    }
                    if(count > 0) {
                        emoji += "🐣";
                    }
                    var openSpots = coopsBreakdown.ExistingCoops.Where(x => !x.Coop.FinishedOrFailed && x.HasSpots).Sum(x => guildContract.Contract.Details.MaxCoopSize - x.CoopParticipants.Count);
                    emoji += openSpots >= count ? "🟩" : "❎"; //Emoji green box
                }
                channelName = emoji + channelName;
            } else if(guildContract.Contract.Details.MaxCoopSize <= 1) {
                channelName = "👤" + channelName;
            } else if(coopsBreakdown.ExistingCoops.Count == 0) {
                var emoji = "🐣";
                if(coopsBreakdown.PotentialCoops.Any(x => x.IsFire)) {
                    emoji += "🔥";
                }
                if(coopsBreakdown.PotentialCoops.Any(x => x.IsDoubleFire)) {
                    emoji += "🔥";
                }
                channelName = emoji + channelName;
            } else if(coopsBreakdown.PotentialCoops.Where(x => x.CoopParticipants.Count > 0).Count() > 0) {
                var count = coopsBreakdown.PotentialCoops.Sum(x => x.CoopParticipants.Count);
                var emoji = "";
                emoji += "🐣";
                if(validFor.TotalHours < 18) {
                    emoji += "🔺";
                }
                if(coopsBreakdown.ExistingCoops.All(x => x.Coop.FinishedOrFailed)) {
                    emoji += "🏁";
                } else if(coopsBreakdown.ExistingCoops.All(x => x.Coop.FinishedOrFailed || !x.HasSpots || x.Coop.CoopEnds < DateTimeOffset.Now)) {
                    emoji += "✅";
                } else {
                    var openSpots = coopsBreakdown.ExistingCoops.Where(x => !x.Coop.FinishedOrFailed && x.HasSpots).Sum(x => guildContract.Contract.Details.MaxCoopSize - x.CoopParticipants.Count);
                    emoji += openSpots >= count ? "🟩" : "❎"; //Emoji green box
                    if(count <= 20) {
                        emoji += Convert.ToChar(9311 + count);
                    } else if(count <= 35) {
                        emoji += Convert.ToChar(12881 + (count - 21));
                    } else if(count <= 50) {
                        emoji += Convert.ToChar(12977 + (count - 36));
                    } else {
                        emoji += "㊿+";
                    }
                }
                if(coopsBreakdown.PotentialCoops.Any(x => x.IsFire)) {
                    emoji += "🔥";
                }
                if(coopsBreakdown.PotentialCoops.Any(x => x.IsDoubleFire)) {
                    emoji += "🔥";
                }
                channelName = emoji + channelName;
            } else if(coopsBreakdown.PotentialCoops.Where(x => x.CoopParticipants.Count > 0).Count() == 0 && coopsBreakdown.ExistingCoops.All(x => x.Coop.FinishedOrFailed)) {
                channelName = "🏁" + channelName;
            } else if(coopsBreakdown.ExistingCoops.All(x => x.Coop.FinishedOrFailed || !x.HasSpots || x.Coop.CoopEnds < DateTimeOffset.Now)) {
                channelName = "✅" + channelName;
            } else {
                channelName = "🟩" + channelName;
            }

            channelName += "-" + (guildContract.Elite ? "elite" : "standard");

            if(channelName != channel.Name) {
                try {
                    await channel.ModifyAsync(x => x.Name = channelName);
                } catch(Exception) {

                }
            }
        }

    }
}
