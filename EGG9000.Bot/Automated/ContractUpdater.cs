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
    public class ContractUpdater : _UpdaterBase {
        public static TimeSpan _updateInterval = TimeSpan.FromMinutes(6);
        private IConfiguration _configuration;
        private APILink _apiLink;
        public static List<UserX> _users;
        private Words _words;


        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public ContractUpdater(IConfiguration Configuration,
            DiscordSocketClient client,
            APILink apilink,
            Words words,
            Bugsnag.IClient bugsnag
        ) : base(_updateInterval, TimeSpan.Zero, client, bugsnag) {
            _users = new List<UserX>();
            _configuration = Configuration;
            _apiLink = apilink;
            _words = words;
        }



        public override async Task Run(object state) {
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
            //var leaderboardTask = _leaderboardUpdater.UpdateLeaderboard(_bugsnag);

            foreach(var groupGuildContracts in guildGroups.OrderBy(x => new Guid())) {
            //foreach(var groupGuildContracts in guildGroups.OrderByDescending(x => x.Key)) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == groupGuildContracts.Key);
                if(guild == null)
                    continue;

                Console.WriteLine($"Running Contracts for {guild.Name}");
                var dbusers = await _db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
                //var dbusers = await _db.Users.Where(x => x.GuildId == guild.Id).ToListAsync();

                if(showTimings)
                    Console.WriteLine($"DBUsers: {sw.ElapsedMilliseconds}");
                sw.Restart();
                //var backups = await ContractsAPI.GetUserBackups(_cache, dbusers);
                var backups = await _apiLink.GetUserBackups(dbusers, _db);


                if(showTimings)
                    Console.WriteLine($"Backups: {sw.ElapsedMilliseconds}");
                sw.Restart();


                var contractIds = groupGuildContracts.Select(x => x.ContractID);

                var sqlExcpetionPolicy = Policy.Handle<SqlException>().WaitAndRetryAsync(new[]{
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(3)
                });

                //List<Coop> allCoops;
                //try {
                //    allCoops = await sqlExcpetionPolicy.ExecuteAsync(async () => await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => contractIds.Contains(x.ContractID) && x.GuildId == guild.Id).ToListAsync());
                //} catch(Exception e) {
                //    Console.WriteLine($"*** Error getting coops in contract updated: {e.Message}");
                //    return;
                //}

                if(showTimings)
                    Console.WriteLine($"Get Coops: {sw.ElapsedMilliseconds}");
                sw.Restart();
                foreach(var guildContract in groupGuildContracts.OrderByDescending(x => x.Created)) {
                    try {
                        List<Coop> coops;
                        try {
                            coops = await sqlExcpetionPolicy.ExecuteAsync(async () => await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guild.Id && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync());
                        } catch(Exception e) {
                            _bugsnag.Notify(e);
                            Console.WriteLine($"*** Error getting coops in contract updated: {e.Message}");
                            return;
                        }
                        //var coops = allCoops.Where(x => x.ContractID == guildContract.ContractID && x.League == (guildContract.Elite ? 0 : 1)).ToList();
                        //await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();


                        var allPreFarms = await GetPrefarmers(backups, guildContract.Contract);

                        allPreFarms.ForEach(x => x.DiscordUser = guild.Users.FirstOrDefault(y => y.Id == x.DiscordId));

                        if(showTimings)
                            Console.WriteLine($"GetPrefarmers: {sw.ElapsedMilliseconds}");
                        sw.Restart();


                        Console.WriteLine($"Working on GuildContract for {guildContract.GuildID} - {guildContract.Contract.ID}");


                        var channel = guild.TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);
                        if(channel == null) {
                            Console.WriteLine($"Missing Channel for {guildContract.ContractID}");
                            continue;
                        }

                        var validFor = (DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now);
                        var league = guildContract.Elite ? 0 : 1;
                        var targetAmount = guildContract.Contract.Details.GoalSets[league].Goals.Last().TargetAmount;

                        var newMsgs = new List<string>();

                        var alienBackups = await GetBackupsForAliens(coops, allPreFarms, guildContract.Contract, _apiLink);
                        if(showTimings)
                            Console.WriteLine($"GetBackupsForAliens: {sw.ElapsedMilliseconds}");
                        sw.Restart();

                        var coopsDetails = new List<CoopDetails>();
                        foreach(var coop in coops) {
                            var coopDetails = new CoopDetails {
                                Users = GetPrefarmsForCoop(coop, allPreFarms, alienBackups, guildContract.Contract),
                                Coop = coop
                            };
                            coopDetails.Projected = coopDetails.Users.Sum(x => x.Projected) / targetAmount;
                            coopsDetails.Add(coopDetails);
                        }
                        if(showTimings)
                            Console.WriteLine($"GetPrefarmsForCoop: {sw.ElapsedMilliseconds}");
                        sw.Restart();

                        ShowCurrentCoops(guildContract, coopsDetails, channel, allPreFarms, newMsgs, targetAmount, false);
                        if(showTimings)
                            Console.WriteLine($"ShowCurrentCoops: {sw.ElapsedMilliseconds}");
                        sw.Restart();


                        var dbguild = dbguilds.First(x => x.Id == guild.Id);
                        var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>((guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards) ?? "[]");
                        var activeUsers = JsonConvert.DeserializeObject<List<GuildUser>>((guildContract.Elite ? dbguild.ActiveElites : dbguild.ActiveStandards) ?? "[]");
                        var prefarms = allPreFarms.Where(x => x.Elite == guildContract.Elite || x.Completed).ToList();
                        prefarms = prefarms.Where(x =>
                            !coops.Any(c => c.UserCoopsXrefs.Any(xr =>
                            (xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId)
                            && (
                                c.Status != CoopStatusEnum.Failed ||
                                x.CoopName == c.Name.ToLower()
                            )
                        ))).ToList();


                        var coopsBreakdown = GetBreakdown(prefarms, guildContract, guild);

                        var channelName = guildContract.Contract.Name.Split(":").Last().Trim().Replace(" ", "-");// + "_" +  guildContract.ContractID;


                        if(DateTimeOffset.Now >= DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime)) {
                            var emoji = "⛔";
                            if(guildContract.Contract.Details.MaxCoopSize <= 1) {
                                emoji += "👤";
                            } else if(coops.All(x => x.Finished)) {
                                emoji += "🏁";
                            } else if(coopsDetails.All(x => x.Coop.Finished || x.Users.Count >= guildContract.Contract.Details.MaxCoopSize || x.Coop.CoopEnds < DateTimeOffset.Now)) {
                                emoji += "✅";
                            } else {
                                var count = coopsBreakdown.Coops.Sum(x => x.Users.Count);
                                if(count > 0 && count <= 20) {
                                    emoji += Convert.ToChar(9311 + count);
                                }
                                if(count > 0) {
                                    emoji += "🐣";
                                }
                                emoji += "🟩"; //Emoji green box
                            }
                            channelName = emoji + channelName;
                        } else if(guildContract.Contract.Details.MaxCoopSize <= 1) {
                            channelName = "👤" + channelName;
                        } else if(coops.Count == 0) {
                            var emoji = "🐣";
                            if(coopsBreakdown.Coops.Any(x => x.Projected > 2)) {
                                emoji += "🔥";
                            }
                            channelName = emoji + channelName;
                        } else if(coopsBreakdown.Coops.Where(x => x.Users.Count > 0).Count() > 0) {
                            var count = coopsBreakdown.Coops.Sum(x => x.Users.Count);
                            var emoji = "";
                            emoji += "🐣";
                            if(validFor.TotalHours < 18) {
                                emoji += "🔺";
                            }
                            if(coops.All(x => x.Finished)) {
                                emoji += "🏁";
                            } else if(coopsDetails.All(x => x.Coop.Finished || x.Users.Count >= guildContract.Contract.Details.MaxCoopSize || x.Coop.CoopEnds < DateTimeOffset.Now)) {
                                emoji += "✅";
                            } else {
                                emoji += "🟩"; //Emoji green box
                                if(count <= 20) {
                                    emoji += Convert.ToChar(9311 + count);
                                } else if(count > 20 && count <= 50) {
                                    emoji += Convert.ToChar(12881 + (count - 21));
                                }
                            }
                            if(coopsBreakdown.Coops.Any(x => x.Projected > 2)) {
                                emoji += "🔥";
                            }
                            channelName = emoji + channelName;
                        } else if(coopsBreakdown.Coops.Where(x => x.Users.Count > 0).Count() == 0 && coops.All(x => x.Finished)) {
                            channelName = "🏁" + channelName;
                        } else if(coopsDetails.All(x => x.Coop.Finished || x.Users.Count >= guildContract.Contract.Details.MaxCoopSize || x.Coop.CoopEnds < DateTimeOffset.Now)) {
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

                        if(guildContract.NumberOfCoops < coopsBreakdown.Coops.Count) {
                            guildContract.NumberOfCoops = coopsBreakdown.Coops.Count;
                        }

                        if(!guildContract.Contract.Details.CoopAllowed) {
                            newMsgs.AddRange(ShowSoloStatus(coopsBreakdown, guildContract.Contract.Details, targetAmount));
                        } else {
                            for(int i = 1; i <= coopsBreakdown.Coops.Count; i++) {
                                var coop = coopsBreakdown.Coops[i - 1];

                                var name = $"Coop {i + coops.Count}";
                                if(coop.Users.Any(x => x.TimeLeft.HasValue)) {
                                    name += $" (Not Started) Farms expire: {coop.Users.Where(x => x.TimeLeft.HasValue).Min(x => x.TimeLeft).Value.Humanize(precision: 2).ShortenTime()}";
                                }

                                if(coop.Users.Sum(x => x.Projected) / targetAmount > 1) {
                                    var timeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, coop.Users.Sum(x => x.Rate), coop.Users.Sum(x => x.EggsPaidFor));
                                    name += $" Complete in: {timeRemaining.Humanize(2).ShortenTime()}";
                                }
                                if(coop.Users.Count > 0) {
                                    newMsgs.AddRange(ShowCoopStatus(coop, name, targetAmount, guildContract.Contract.Details.MaxCoopSize));
                                }
                            }
                        }

                        if(coopsBreakdown.ExpiredFarms.Count > 0 && guildContract.Contract.Details.CoopAllowed) {
                            newMsgs.AddRange(ShowCoopStatus(new CoopDetails { Users = coopsBreakdown.ExpiredFarms.OrderBy(x => x.Name).ToList() }, "Expired Farms", targetAmount, guildContract.Contract.Details.MaxCoopSize));
                        }

                        if(coopsBreakdown.AlreadyInCoop.Users.Count > 0) {
                            foreach(var u in coopsBreakdown.AlreadyInCoop.Users) {
                                if(u.Completed) {
                                    u.Coop = "Completed";
                                } else {
                                    u.Coop += $" Joined {(DateTimeOffset.Now - u.User.CreateOn).Humanize().ShortenTime()} ago";
                                }
                            }
                            coopsBreakdown.AlreadyInCoop.Users = coopsBreakdown.AlreadyInCoop.Users.Where(x => x.User.CreateOn < guildContract.Created).OrderBy(x => x.Completed).ToList();
                            newMsgs.AddRange(ShowCoopStatus(coopsBreakdown.AlreadyInCoop, $"Already in coop", targetAmount, 0));
                        }

                        if(coopsBreakdown.Completed.Users.Count > 0 && coopsBreakdown.Completed.Users.Count < 40) {
                            newMsgs.Add($"\nThe following users have already completed the contract.\n{String.Join(", ", coopsBreakdown.Completed.Users.Select(x => x.Name))}\n");
                        }


                        var finalMsg = "";



                        var activesNotParticipanting = activeUsers.Where(au =>
                            !coopsBreakdown.Coops.Any(c => c.Users.Any(u => u.EggIncId == au.EggIncId)) &&
                            !coopsBreakdown.AlreadyInCoop.Users.Any(u => u.EggIncId == au.EggIncId) &&
                            !coopsBreakdown.Completed.Users.Any(u => u.EggIncId == au.EggIncId)).OrderBy(x => x.DiscordName
                        ).ToList();

                        var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");

                        var skipping = activesNotParticipanting.Where(x => skipList.Any(y => x.DiscordId == y) && !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();

                        if(skipping.Count > 0) {
                            finalMsg += $"\nThe following people are set to skip this contract. {string.Join(", ", skipping.Select(x => x.DiscordName))}\n";
                        }

                        var skippingWhilePrefarming = activesNotParticipanting.Where(x => skipList.Any(y => x.DiscordId == y) && coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
                        if(skippingWhilePrefarming.Count > 0) {
                            finalMsg += $"\nThe following people are set to skip this contract **but are still prefarming**. {string.Join(", ", skippingWhilePrefarming.Select(x => x.DiscordName))}\n";
                        }

                        var usersNoLongerOnBreak = coopsBreakdown.Coops.SelectMany(x => x.Users.Where(y => y.User.OnBreakSince.HasValue && y.Started.HasValue && y.Started > y.User.OnBreakSince)).ToList();
                        if(usersNoLongerOnBreak.Count > 0) {
                            usersNoLongerOnBreak.ForEach(x => x.User.OnBreakSince = null);
                        }

                        activesNotParticipanting = activesNotParticipanting.Where(x => !x.OnBreakSince.HasValue && !skipList.Any(y => x.DiscordId == y) && !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();

                        activesNotParticipanting = activesNotParticipanting.Where(x =>
                          !allPreFarms.FirstOrDefault(y => y.DiscordId == x.DiscordId)?.Completed ?? true
                        ).ToList();

                        if(activesNotParticipanting.Count > 0) {
                            var PEAward = guildContract.Contract.Details.GoalSets.Any(x => x.Goals.Any(y => y.RewardType == RewardType.EggsOfProphecy));
                            if(!PEAward) {
                                var usersNotSkip = dbusers.Where(x => !x.SkipNoPE).Select(x => x.Id).ToList();
                                activesNotParticipanting = activesNotParticipanting.Where(x => usersNotSkip.Contains(x.DatabaseId)).ToList();
                            }
                            if(DateTimeOffset.Now >= DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime)) {
                                finalMsg += $"\nThe following people did not do the contract. {string.Join(", ", activesNotParticipanting.Select(x => x.DiscordName))}\n";
                            } else {
                                finalMsg += $"\nThe following people have not started prefarming yet. {string.Join(", ", activesNotParticipanting.Select(x => x.Mention))}\n";
                            }

                        }

                        if(!guildContract.Elite && guildContract.ContractID == "bluetooth-pigs") {
                            var dbuser = dbusers.FirstOrDefault(x => x.DiscordId == 675889421089898562);
                            var backup = backups.FirstOrDefault(x => x.DiscordUser?.Id == 675889421089898562);
                            var allUser = allPreFarms.FirstOrDefault(x => x.DiscordId == 675889421089898562);
                        }


                        var expectedCount = coopsBreakdown.Coops.Sum(x => x.Users.Count) + activesNotParticipanting.Count;

                        finalMsg += $"Expected Participants: {expectedCount}, Current Capacity: {coopsBreakdown.Coops.Count * guildContract.Contract.Details.MaxCoopSize}, Currently Pre-farming: {coopsBreakdown.Coops.Sum(x => x.Users.Count)}, Co-op Count: {coopsBreakdown.Coops.Count}\n";

                        var spotsInPotentialCoops = coopsBreakdown.Coops.Count * guildContract.Contract.Details.MaxCoopSize - coopsBreakdown.Coops.Sum(x => x.Users.Count);
                        var spotsInCurrentCoops = coopsDetails.Where(x => !x.Coop.Finished).Sum(x => guildContract.Contract.Details.MaxCoopSize - x.Users.Count);
                        finalMsg += $"Spots Available In Potential Co-ops: {spotsInPotentialCoops}, Spots Available In Active Co-ops: {spotsInCurrentCoops}\n\n";


                        //finalMsg += "**WARNING: Messages in this channel are automatically deleted**\n";
                        //finalMsg += $"The bot updates this channel every {_updateInterval.TotalMinutes} mins\n";
                        //finalMsg += "Current Status: **Prefarming**\n";
                        //finalMsg += $"View On Website <https://egg9000.com/Contract/Coop?GuildId={guild.Id}&ContractId={guildContract.ContractID}&Elite={guildContract.Elite.ToString()}>";


                        while(finalMsg.Length > 2000) {
                            var index = finalMsg.LastIndexOf(", ", 2000);
                            newMsgs.Add(finalMsg.Substring(0, index));
                            finalMsg = finalMsg.Substring(index);
                        }
                        newMsgs.Add(finalMsg);


                        var description = $"[View Co-ops on egg9000.com](https://egg9000.com/Contract/Coop?GuildId={guild.Id}&ContractId={guildContract.ContractID}&Elite={guildContract.Elite.ToString()})\n";
                        description += $"**Size** {guildContract.Contract.MaxUsers}, **<:Token_boost:724397091211968604>** {guildContract.Contract.Details.MinutesPerToken}mins,";
                        description += $"**Length** {guildContract.Contract.ContractTime.Humanize(precision: 2).ShortenTime()}, **{(validFor > TimeSpan.Zero ? "Expires" : "Expired")}** {validFor.Humanize(precision: 2).ShortenTime()}";

                        var embedBuilder = new EmbedBuilder()
                            .WithDescription(description)
                            //.AddField("Co-op Size", guildContract.Contract.MaxUsers, true)
                            //.AddField("<:Token_boost:724397091211968604> Interval", guildContract.Contract.Details.MinutesPerToken + "mins", true)
                            //.AddField("\u17B5", "\u17B5", true)

                            //.AddField("Length", guildContract.Contract.ContractTime.Humanize(precision: 2).ShortenTime(), true)
                            //.AddField(validFor > TimeSpan.Zero ? "Ends" : "Ended", validFor.Humanize(precision: 2).ShortenTime(), true)
                            //.AddField("\u17B5", "\u17B5", true)
                            //.AddField("Co-op Count", coopsBreakdown.Coops.Count, true)

                            //.AddField("Capactiy", coopsBreakdown.Coops.Count * guildContract.Contract.Details.MaxCoopSize, true)
                            //.AddField("Expected", expectedCount, true)
                            //.AddField("Prefarming", coopsBreakdown.Coops.Sum(x => x.Users.Count), true)
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
                        foreach(var msg in newMsgs) {
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


                        if(showTimings)
                            Console.WriteLine($"Misc Middle: {sw.ElapsedMilliseconds}");
                        sw.Restart();


                        var existingMessages = (await channel.GetMessagesAsync().FlattenAsync()).ToList();


                        var nonBotMessages = existingMessages.Where(x => !x.Author.IsBot).ToList();
                        if(nonBotMessages.Count > 0)
                            await channel.DeleteMessagesAsync(nonBotMessages);


                        existingMessages = existingMessages.Where(x => x.Author.IsBot).OrderBy(x => x.CreatedAt).ToList();

                        var condensedMsgCount = condensedMsgs.Count;

                        if(existingMessages.Count == 0) {
                            var reserveMessages = (int)((activeUsers.Count - coopsBreakdown.Completed.Users.Count) / 30);
                            Console.WriteLine($"Reserve: {reserveMessages} Actual: {condensedMsgCount}");
                            if(existingMessages.Count == reserveMessages + 1) {
                                reserveMessages = existingMessages.Count;
                            }
                            for(var i = condensedMsgs.Count; i < reserveMessages; i++) {
                                condensedMsgs.Add("\u17B5"); //Reserve extra messages
                            }
                        }
                        if(showTimings)
                            Console.WriteLine($"Get Messages: {sw.ElapsedMilliseconds}");
                        sw.Restart();

                        int modified1 = 0, modified2 = 0, skipped = 0, newMessage = 0, deleted = 0;

                        IMessage notStarted = null;

                        for(int i = 0; i < Math.Max(existingMessages.Count(), condensedMsgs.Count); i++) {
                            if(existingMessages.Count() > i && condensedMsgs.Count > i) {
                                if(i == condensedMsgCount - 1) {
                                    await ((RestUserMessage)existingMessages.ElementAt(i)).ModifyAsync(msg => { msg.Embed = embedBuilder.Build(); msg.Content = condensedMsgs[i]; });
                                    modified1++;
                                } else if(!existingMessages.ElementAt(i).Content.Replace(" ", "").Equals(condensedMsgs[i].Replace(" ", "")) || existingMessages.ElementAt(i).Embeds.Count > 0) {
                                    var changes = existingMessages.ElementAt(i).Content.CompareChanges(condensedMsgs[i]);
                                    if(existingMessages.ElementAt(i).Embeds.Count > 0 || changes > 20) {
                                        await ((RestUserMessage)existingMessages.ElementAt(i)).ModifyAsync(msg => { msg.Content = condensedMsgs[i]; msg.Embed = null; });
                                        modified2++;
                                    } else {
                                        skipped++;
                                    }
                                    if(notStarted == null && condensedMsgs[i].Contains("(Not Started)")) {
                                        notStarted = existingMessages.ElementAt(i);
                                        embedBuilder.Description = GetLinkToMessage(notStarted, guild, channel, "Jump to top not started co-op (for staff)") + embedBuilder.Description;
                                    }
                                } else {
                                    skipped++;
                                }
                            } else if(existingMessages.Count() <= i) {
                                if(i == condensedMsgCount - 1) {
                                    await channel.SendMessageAsync(condensedMsgs[i], embed: embedBuilder.Build());
                                } else {
                                    var message = await channel.SendMessageAsync(condensedMsgs[i]);
                                    newMessage++;
                                    if(notStarted == null && condensedMsgs[i].Contains("(Not Started)")) {
                                        notStarted = message;
                                        embedBuilder.Description = GetLinkToMessage(notStarted, guild, channel, "Jump to top not started co-op (for staff)") + embedBuilder.Description;
                                    }
                                }
                            } else {
                                //await ((RestUserMessage)existingMessages.ElementAt(i)).ModifyAsync(msg => msg.Content = "\u17B5");
                                try {
                                    await ((RestUserMessage)existingMessages.ElementAt(i)).DeleteAsync();
                                } catch(Exception) {
                                }
                                deleted++;
                            }
                        }
                        if(showTimings)
                            Console.WriteLine($"Message Counts: modified1 {modified1}, modified2 { modified2}, skipped {skipped}, new {newMessage}, deleted {deleted}");
                        if(showTimings)
                            Console.WriteLine($"Post Messages: {sw.ElapsedMilliseconds}");
                        sw.Restart();
                    } catch(Exception e) {
                        Console.WriteLine($"Error Updating Contracts Channel {e.Message}");
                        _bugsnag.Notify(e);
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

        public void ShowCurrentCoops(GuildContract guildContract, List<CoopDetails> coopsDetails, SocketTextChannel channel, List<UserPreFarm> allUsers, List<string> newMsgs, double targetAmount, bool allStarted) {
            if(guildContract.Status != ContractStatus.Completed && coopsDetails.All(x => x.Coop.Status == CoopStatusEnum.Completed || x.Coop.Finished) && allStarted) {
                guildContract.Status = ContractStatus.Completed;
            }

            for(int i = 1; i <= coopsDetails.Count; i++) {
                var coopDetails = coopsDetails[i - 1];

                if(coopDetails.Coop.DeletedChannel)
                    continue;

                if(coopDetails.Coop.Finished && coopDetails.Coop.Status != CoopStatusEnum.Completed) {
                    coopDetails.Coop.Status = CoopStatusEnum.Completed;
                }


                var name = coopDetails.Coop.Status switch {
                    CoopStatusEnum.WaitingOnStarter => $"Coop {i} - Waiting on bot to start",
                    CoopStatusEnum.WaitingOnAssigned => $"Coop {i} - Waiting on the below users to join",
                    CoopStatusEnum.AllAssignedJoined => $"Coop {i}",
                    CoopStatusEnum.Full => $"Coop {i} - Coop is Full",
                    CoopStatusEnum.Completed => $"Coop {i} has completed! 🎆",
                    _ => ""
                };

                if(!coopDetails.Coop.Finished) {
                    var timeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, coopDetails.Users.Sum(x => x.Rate), coopDetails.Users.Sum(x => x.EggsPaidFor));
                    if(timeRemaining > TimeSpan.Zero) {
                        name += $" Time to complete: {timeRemaining.Humanize(2).ShortenTime()}";
                    }
                    if(coopDetails.Coop.CoopEnds < DateTimeOffset.Now) {
                        name += $" Expired: {(coopDetails.Coop.CoopEnds - DateTimeOffset.Now).Value.Humanize(2).ShortenTime()} ago";
                    } else if((coopDetails.Coop.CoopEnds - DateTimeOffset.Now) < timeRemaining) {
                        name += $" Expires In: {(coopDetails.Coop.CoopEnds - DateTimeOffset.Now).Value.Humanize(2).ShortenTime()}";
                    }
                }

                var userIds = coopDetails.Coop.UserCoopsXrefs.Select(x => x.GetID());


                if(coopDetails.Coop.Status == CoopStatusEnum.Completed) {
                    coopDetails.Users.Where(x => coopDetails.Coop.LastStatusUpdate.Participants.Any(y => y.UserId == x.EggIncId)).ToList().ForEach(x => {
                        var details = coopDetails.Coop.LastStatusUpdate.Participants.First(y => y.UserId == x.EggIncId);
                        x.NumChickens = details.ProductionParams.FarmPopulation;
                        x.EggsPaidFor = details.ContributionAmount;
                        x.Rate = details.ContributionRate;
                        x.Projected = details.ContributionAmount;
                    });
                }

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

        private List<string> ShowCoopStatus(CoopDetails coop, string coopName, double target, uint size) {
            var table = new List<List<FixedWidthCell>>();
            table.Add(new List<FixedWidthCell> {
                            new FixedWidthCell("Name", CellAlignment.Center),
                            //new FixedWidthCell("Egg Lain", CellAlignment.Center),
                            new FixedWidthCell("🐔", CellAlignment.Center),
                            new FixedWidthCell("🥚", CellAlignment.Center),
                            new FixedWidthCell("📈", CellAlignment.Center, true),
                            //new FixedWidthCell("Tokens", CellAlignment.Center),
                            //new FixedWidthCell("Spent", CellAlignment.Center),
                            //new FixedWidthCell("Online", CellAlignment.Center),
                            new FixedWidthCell(coop.Users.All(x => string.IsNullOrEmpty(x.Coop)) ? "🟡" : "Join")
                        });
            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            table.AddRange(coop.Users.OrderByDescending(x => x.Projected).Select(x => {
                var timeleft = "";
                if(x.TimeLeft.HasValue) {
                    if(x.TimeLeft.Value.TotalSeconds > 0) {
                        timeleft = x.TimeLeft.Value.Humanize(precision: 2).ShortenTime();
                    } else {
                        timeleft = "Time has run out";
                    }
                }
                var emoji = "";
                if(x.DiscordUser != null && x.DiscordUser.Roles.Any(r => r.Id == 796512753241161748)) {
                    emoji = "🆕";
                }
                return new List<FixedWidthCell> {
                            new FixedWidthCell(Truncate($"{emoji}{ebrgx.Replace( x.Name, "")}", 12)),
                            //new FixedWidthCell(x.EggsPaidFor.ToEggString()),
                            new FixedWidthCell(x.NumChickens.ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(ArgumentsHelper.NumberToString(x.Rate * 60 * 60, false) + "/h", CellAlignment.Right),
                            new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
                            //new FixedWidthCell(x.Tokens.ToString()),
                            //new FixedWidthCell(x.BoostTokensSpent.ToString()),
                            //new FixedWidthCell(x.TimeSinceUpdate.Humanize(1, minUnit: Humanizer.Localisation.TimeUnit.Minute).ShortenTime()),
                            new FixedWidthCell(string.IsNullOrEmpty(x.Coop) ? x.Tokens.ToString() : x.Coop)
                    };
            }));

            if(coopName != "Expired Farms" && coopName != "Already in coop") {
                var percent = $"{coop.Projected:P0}".Replace(",",""); //$"{coop.Users.Sum(x => x.Projected) / target:P0}";

                table.Add(new List<FixedWidthCell> {
                            new FixedWidthCell($"{coop.Users.Count}/{size}"),
                            new FixedWidthCell(""),
                            new FixedWidthCell(ArgumentsHelper.NumberToString(coop.Users.Sum(x => x.Rate) * 60 * 60, false) + "/h", CellAlignment.Right),
//                            new FixedWidthCell(""),
                            new FixedWidthCell(coop.Users.Sum(x => x.Projected).ToEggString(), CellAlignment.Right),
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

            var participants = coopsBreakdown.Coops.SelectMany(x => x.Users).ToList();
            participants.AddRange(coopsBreakdown.ExpiredFarms);

            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");

            table.AddRange(participants.Where(x => x.NumChickens > 0).OrderBy(x => x.Name).Select(x => {
                return new List<FixedWidthCell> {
                            new FixedWidthCell(Truncate(ebrgx.Replace(x.Name, ""), 12)),
                            new FixedWidthCell(x.NumChickens.ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(ArgumentsHelper.NumberToString(x.Rate * 60 * 60, false) + "/h", CellAlignment.Right),
                            new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
                            new FixedWidthCell(String.Format("{0:0%}", x.Projected/target) , CellAlignment.Right),
                            new FixedWidthCell(x.EggsPaidFor < target ?  Prefarm.GetTimeRemainingValue(target, x.Rate, x.EggsPaidFor).Humanize(1, null, Humanizer.Localisation.TimeUnit.Year).ShortenTime() : "Finished", CellAlignment.Right)
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


    }
}
