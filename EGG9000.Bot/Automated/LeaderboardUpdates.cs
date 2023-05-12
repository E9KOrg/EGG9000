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
using System.Numerics;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using Humanizer;
using Microsoft.Extensions.Caching.Memory;
using static EGG9000.Common.Helpers.Prefarm;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using EGG9000.Common.Services;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Bot.Automated {
    public class LeaderboardUpdater : _UpdaterBase<LeaderboardUpdater> {
        public static TimeSpan UpdateTime = TimeSpan.FromMinutes(15);
        private APILink _apiLink;

        public static List<UserX> _users;


        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public LeaderboardUpdater(
            IServiceProvider provider,
            APILink apilink
        ) : base(UpdateTime, delayedStart: TimeSpan.FromMinutes(5), provider) {
            _apiLink = apilink;
        }


        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Console.WriteLine("Updating Leaderboard");
            var recentContracts = await _db.Contracts.AsQueryable().Where(x => x.Created < DateTime.Today.AddDays(-5)).Where(x => x.MaxUsers > 1).OrderByDescending(x => x.Created).Take(5).ToListAsync();
            var currentContracts = await _db.Contracts.AsQueryable().Where(x => x.Created >= DateTime.Today.AddDays(-5)).Where(x => x.MaxUsers > 1).OrderByDescending(x => x.Created).ToListAsync();
            var recentContractIDs = recentContracts.Select(x => x.ID);

            Console.WriteLine("Getting Xrefs for Leaderboard");
            try {
                var threeWeeksAgo = DateTimeOffset.Now.AddDays(-21);
//#if DEBUG
//                var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.Coop.GuildId == 770469712064151593 && x.JoinedCoop).Select(x => new { LastThreeWeeks = x.CreatedOn > threeWeeksAgo, UserId = x.GetID(), x.Coop.ContractID, x.EggIncId, x.RefEggIncId }).ToListAsync();
//#else
                var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.JoinedCoop).Select(x => new { LastThreeWeeks = x.CreatedOn > threeWeeksAgo, UserId = x.GetID(), x.Coop.ContractID, x.EggIncId, x.RefEggIncId }).ToListAsync();
//#endif
                var recentxrefs = xrefs.Where(x => recentContractIDs.Contains(x.ContractID)).ToList();

                Console.WriteLine("Getting Users for Leaderboard");
                //#if DEBUG
                //var dbusersWithGuildCoops = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == 770469712064151593 && !x.TempDisabled).Select(x => new {
                //    DBUser = x,
                //}).ToListAsync();
                //#else
                var dbusersWithGuildCoops = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0 && !x.TempDisabled).Select(x => new {
                    DBUser = x,
                }).ToListAsync();
                //#endif
                dbusersWithGuildCoops.ForEach(x => { //
                    x.DBUser.GuildCoops = (ushort)xrefs.Where(xref => xref.UserId == x.DBUser.Id).Select(xref => xref.ContractID).Distinct().Count();
                });
                var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

                var dbusers = dbusersWithGuildCoops.Select(x => x.DBUser).ToList();

//#if DEBUG
//                dbusers = dbusers.Where(x => x.GuildId == 770469712064151593).ToList();
//                dbguilds = dbguilds.Where(x => x.Id == 770469712064151593).ToList();
//#endif

                Console.WriteLine("Getting User Backups for Leaderboard");
                //var lUsers = (await _apiLink.GetUserBackups(dbusers, _db, true)).Where(x => x != null);

                var lUsers = dbusers.SelectMany(x => x.EggIncAccounts.Select(y => new LeaderboardUser {
                    User = x,
                    Backup = x.Backups.FirstOrDefault(b => b.EggIncId == y.Id)
                })).Where(x => x.Backup is not null).ToList();

                foreach(var lUser in lUsers) {
                    if(lUser.Backup == null)
                        return;
                    var userXrefs = xrefs.Where(x => lUser.User.Id == x.UserId).ToList();
                    lUser.TotalContracts = userXrefs.Where(x => (x.EggIncId == null || x.EggIncId == lUser.Backup.EggIncId || x.RefEggIncId == lUser.Backup.EggIncId)).GroupBy(x => x.ContractID).Count();
                    lUser.ActiveContracts = recentxrefs.Count(x => x.UserId == lUser.User.Id);
                    lUser.RecentContracts = Math.Max(recentContracts.Count(x => x.Created > lUser.User.Registered), lUser.ActiveContracts);
                    lUser.Last1 = recentxrefs.Any(x => x.UserId == lUser.User.Id && x.ContractID == recentContracts[0].ID);
                    lUser.Last2 = recentxrefs.Any(x => x.UserId == lUser.User.Id && x.ContractID == recentContracts[1].ID);
                    lUser.Last3 = recentxrefs.Any(x => x.UserId == lUser.User.Id && x.ContractID == recentContracts[2].ID);
                    lUser.Last4 = recentxrefs.Any(x => x.UserId == lUser.User.Id && x.ContractID == recentContracts[3].ID);
                    lUser.Last5 = recentxrefs.Any(x => x.UserId == lUser.User.Id && x.ContractID == recentContracts[4].ID);
                    lUser.Active = userXrefs.Any(x => x.UserId == lUser.User.Id && x.LastThreeWeeks) || lUser.User.Registered > DateTimeOffset.Now.AddDays(-14);
                }

                foreach(var dbguild in dbguilds) {
                    if(cancellationToken.IsCancellationRequested)
                        break;

                    Console.WriteLine($"Working on leaderboard for {dbguild.Name}");

                    var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                    await guild.DownloadUsersAsync();
                    var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);

                    List<SocketGuild> overflowGuilds = null;
                    if(dbguild.OverflowServers.Count > 0) {
                        overflowGuilds = dbguild.OverflowServers.Select(x => _client.Guilds.First(y => y.Id == x)).ToList();
                    }


                    var users = lUsers.Where(x => x.User.GuildId == guild.Id).ToList();

                    //Handle promotions
                    Console.WriteLine($"Handling promotions for {guild.Name}");


                    var grades = new List<(Ei.Contract.Types.PlayerGrade, SocketRole)> {
                        (Ei.Contract.Types.PlayerGrade.GradeAaa, await _client.GetRoleAsync(GuildChannelType.GradeAAA, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeAa, await _client.GetRoleAsync(GuildChannelType.GradeAA, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeA, await _client.GetRoleAsync(GuildChannelType.GradeA, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeB, await _client.GetRoleAsync(GuildChannelType.GradeB, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeC, await _client.GetRoleAsync(GuildChannelType.GradeC, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeUnset, null),
                    };

                    foreach(var userAccounts in users.GroupBy(x => x.User.Id)) {
                        if(cancellationToken.IsCancellationRequested)
                            break;

                        //Console.WriteLine($"Handling {userAccounts.First().User.DiscordUsername}");
                        var dbUser = userAccounts.First().User;
                        var discordUser = guild.GetUser(dbUser.DiscordId);
                        if(userAccounts.Count() > 1) {

                        }
                        if(discordUser == null || userAccounts.All(x => x.Backup?.Farms == null || x.Backup?.Farms.Count == 0))
                            continue;
                        userAccounts.ToList().ForEach(y => {
                            y.DiscordUser = discordUser;
                            y.User.DiscordUsername = discordUser.Nickname ?? discordUser.Username;
                        });


                        var registeredRole = discordUser.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
                        if(registeredRole == null) {
                            await discordUser.AddRoleAsync(guild.Roles.First(x => x.Name.ToLower().Contains("registered")));
                            Console.WriteLine("Adding registered role");
                            await Task.Delay(500);
                        }

                        if(unjoinedRole != null) {
                            var hasRole = discordUser.Roles.Any(x => x.Id == unjoinedRole.Id);
                            var needsRole = userAccounts.All(x => x.TotalContracts == 0);
                            if(hasRole && !needsRole) {
                                await discordUser.RemoveRoleAsync(unjoinedRole);
                                await Task.Delay(500);
                            } else if(!hasRole && needsRole) {
                                await discordUser.AddRoleAsync(unjoinedRole);
                                await Task.Delay(500);
                            }

                        }

                        var existingRole = discordUser.Roles.FirstOrDefault(x => x.Name.ToUpper().Contains("FARMER"));

                        var higherEB = userAccounts.Where(x => x.Backup?.Farms.Count != 0).OrderByDescending(x => x.Backup.EarningsBonus).First();
                        var role = await DiscordHelpers.SetRole(guild, discordUser, higherEB.Backup.EarningsBonus);

                        await DiscordHelpers.CheckSiloResearch(guild, discordUser, userAccounts.Select(y => y.Backup).ToList());
                        await DiscordHelpers.CheckHatchlingRole(guild, discordUser, dbUser);
                        await DiscordHelpers.CheckFreshEggsRole(guild, discordUser, dbUser);
                        await DiscordHelpers.CheckActive(_client, guild, discordUser, dbUser, userAccounts);
                        await DiscordHelpers.CheckBG(_client, guild, discordUser, dbUser, userAccounts);
                        await DiscordHelpers.CheckPermitRoles(guild, discordUser, userAccounts);
                        await DiscordHelpers.CheckGrades(guild, discordUser, userAccounts, grades);

                        if(higherEB.Backup.EggsOfProphecy > 1000) {
                            dbUser.showEB = false;
                        }
                        if(!dbUser.showEB && !string.IsNullOrEmpty(discordUser.Nickname) && discordUser.GetCleanName() != discordUser.Nickname && discordUser.Guild.OwnerId != discordUser.Id) {
                            try {
                                Console.WriteLine($"Updating {discordUser.Nickname} to {discordUser.GetCleanName()}");
                                await discordUser.ModifyAsync(x => x.Nickname = discordUser.GetCleanName());
                                await Task.Delay(250);
                            } catch(Exception) {
                                Console.WriteLine($"Unable to change name of {discordUser.GetName()}");
                            }
                        }


                        var unnessacryBackups = dbUser.Backups.Where(x => !dbUser.EggIncAccounts.Any(y => y.Id == x.EggIncId)).ToList();
                        unnessacryBackups.ForEach(x => dbUser.Backups.Remove(x));

                        if(dbUser.showEB) {
                            try {
                                var ebs = dbUser.Backups.Where(x => dbUser.EggIncAccounts.Any(y => y.Id == x.EggIncId)).OrderByDescending(x => x.EarningsBonus).Select(x => x.EarningsBonus.ToEggString());
                                var ebString = $" ({string.Join(",", values: ebs)})";
                                var newName = discordUser.GetCleanName().Truncate(32 - ebString.Length) + ebString;
                                if(newName != discordUser.Nickname && discordUser.Guild.OwnerId != discordUser.Id) {
                                    Console.WriteLine($"Updating {discordUser.Nickname} to {newName}");
                                    await discordUser.ModifyAsync(x => x.Nickname = newName);
                                    await Task.Delay(250);
                                }
                            } catch(Exception) {
                                Console.WriteLine($"Unable to change name of {discordUser.GetName()}");
                            }
                        }

                        if(role != null && existingRole != null && existingRole.Name != role.Name) {
                            Console.WriteLine($"{discordUser.Nickname} changing role from {existingRole.Name} to {role.Name})");
                        }
                        if(role != null && existingRole != null && existingRole.Name != role.Name && role.Position > existingRole.Position) {
                            var eb = higherEB.Backup.EarningsBonus.ToEggString();
                            var messages = new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! How do you like your eggs in the morning?",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! You should see your eggspression right now, lol",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! Eggstraordinary work!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} You made it this far. Looking forward to your next level-up!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Challenge is to never stop prestiging, keep it up!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Prestiging is like a reversed limbo, how high can you go?",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",                            };

                            switch(role.Name) {
                                case "Farmer":
                                    messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! Eggstraordinary work!",
                        });
                                    break;
                                case "Kilofarmer":
                                    messages.AddRange(new List<string> {
                        $"Wow, {discordUser.Mention}! A {role.Name} already? Your wonders never cease to amaze me! Congrats on the new rank and EB of {eb}%!.",
                        });
                                    break;
                                case "Megafarmer":
                                    messages.AddRange(new List<string> {
                        $"Now you are at least hundreds of millions times stronger than you were since your first chicken. Mega effort to become a {role.Name} with and EB of {eb}%! Congratulations on the new rank, {discordUser.Mention}!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",
                        });
                                    break;
                                case "Gigafarmer":
                                    messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! Gigafarmer, sweet! Your numbers are increasing along with your eggsperience!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} You made it this far. Looking forward to your next level-up!",
                        });
                                    break;
                                case "Terafarmer":
                                    messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! Keep going, next up: Petafarmer!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! Chickens won't hatch themselves, get back to farming!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Challenge is to never stop prestiging, keep it up!",
                        $"Choo Choo! All aboard the <:Egg_soul_SE:724341890794913964> train with our new {role.Name}. {discordUser.Mention} is driving the train with an EB of {eb}%, jump on now!",
                        });
                                    break;
                                case "Petafarmer":
                                    messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Prestiging is like a reversed limbo, how high can you go?",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! More chickens, more eggs, higher earnings means more <:Egg_soul_SE:724341890794913964>. Keep hatching!",
                        $"With great EB comes great responsibility. Congrats on hitting an EB of {eb}%, {discordUser.Mention}! This means you are officially a {role.Name}. Now get back out there - those wormholes aren’t going to dampen themselves!",
                                                        });
                                    break;
                                case "Exafarmer":
                                    messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%, {discordUser.Mention}! You really like eggs, eh? Eggciting hobby, isnt it?",
                        $"You’ve finally reached the rank of { role.Name}, { discordUser.Mention}! Wow. It seems like just yesterday you were running your first chickens. Celebrate!",
                        $"{ role.Name}: achieved. What’s next, { discordUser.Mention}? This calls for omelets. Anyone have eggs? Congrats on the impressive EB of { eb}%!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. {discordUser.Mention} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!",
                        $"Choo Choo!All aboard the <:Egg_soul_SE:724341890794913964> train with our new { role.Name }. { discordUser.Mention} is driving the train with an EB of { eb}%, jump on now!",
                        $"Congrats { discordUser.Mention}, you are a { role.Name} now with an EB of { eb}%! How eggciting!",
                        });
                                    break;
                                case "Zettafarmer":
                                    messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%. Afraid of heights, {discordUser.Mention}? I hope not, you're climbing higher and higher up the leaderboard!",
                        $"Did anyone else see that blur go by? I think it was {discordUser.Mention} on their way to LEVELING UP TO THE RANK OF {role.Name} with an EB of {eb}%! Awesome!",
                        $"Is it just me, or does this place smell like an EB of {eb}%? Congrats on achieving the level of {role.Name}, {discordUser.Mention}!",
                        $"Congrats on the new rank of {role.Name} with an EB of {eb}%! Eggstraordinary work, there’s no stopping you, {discordUser.Mention}!",
                        });
                                    break;
                                case "Yottafarmer":
                                    messages.AddRange(new List<string> {
                        $"What an effort! Make way for {discordUser.Mention} and their eggcellent EB of {eb}%! You are now a {role.Name}. Very impressive!",
                        $"We have a new {role.Name} among us! Congratulations on the rank, and the mighty EB of {eb}%, {discordUser.Mention}!",
                        $"{eb}% !That’s a milestone right there.You obviously know what you’re doing { discordUser.Mention}. Congratulations, you are now a {role.Name}!",
                        });
                                    break;
                                case "Xennafarmer":
                                case "Weccafarmer":
                                    messages.AddRange(new List<string> {
                        $"Speechless. Absolutely speechless. The grind is real, {discordUser.Mention}! Congratulations on the very impressive rank of {role.Name} with the incredible EB of {eb}%!",
                        });
                                    break;
                            }



                            var random = new Random();

                            var index = random.Next(messages.Count);
                            var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);

                            await generalChannel.SendMessageAsync(messages[index]);
                        }
                    }

                    await PostOverallLeaderboard(guild, users, recentContracts, _db);
                    Console.WriteLine("Finished updating Leaderboard");
                }
            } catch(Exception e) {
                _bugsnag.Notify(e);
                Console.WriteLine("**************ERROR in LeaderboardUpdater**********")
;
            }

        }

        private async Task PostOverallLeaderboard(SocketGuild guild, List<LeaderboardUser> lUsers, List<Contract> recentContracts, ApplicationDbContext _db) {
            var channel = await _client.GetChannelAsync(GuildChannelType.Leaderboard, guild);
            if(channel == null)
                return;

            lUsers = lUsers.Where(x => x.Backup != null).ToList();
            var activeUsers = lUsers.Where(x => x.Active && x.DiscordUser != null).ToList();
            var inactiveUsers = lUsers.Where(x => !x.Active && x.DiscordUser != null).ToList();

            var dbguild = _db.Guilds.FirstOrDefault(x => x.Id == guild.Id);
            if(dbguild == null) {
                dbguild = new Guild { Name = guild.Name, Id = guild.Id };
                _db.Add(dbguild);
            }


            await _db.SaveChangesAsync();

            var table1 = GetTables(activeUsers, guild);

            var str = "";
            str += $"Total Active Accounts: {activeUsers.Count()}\n";
            str += $"Last 5 Contracts: \n{recentContracts[0].ID}: {lUsers.Count(x => x.Last1)}\n{recentContracts[1].ID}: {lUsers.Count(x => x.Last2)}\n{recentContracts[2].ID}: {lUsers.Count(x => x.Last3)}\n{recentContracts[3].ID}: {lUsers.Count(x => x.Last4)}\n{recentContracts[4].ID}: {lUsers.Count(x => x.Last5)}";
            table1.Add(str);

            if(inactiveUsers.Count > 0 && inactiveUsers.Count < 20) {
                var table2 = GetTables(inactiveUsers, guild);
                table2.Prepend("Inactive Account: ");
                table1.AddRange(table2);
            }

            var msgs = (await channel.GetMessagesAsync().FlattenAsync()).ToList();

            msgs = msgs.OrderBy(x => x.CreatedAt).Where(x => x.Author.Id == 514257192803893272).ToList();

            table1.Add("@@@EMBED");


            IMessage standardMessage = null;

            for(int i = 0; i < Math.Max(msgs.Count, table1.Count); i++) {
                if(msgs.Count > i && table1.Count > i) {
                    if(table1[i] == "@@@EMBED") {
                        await ((RestUserMessage)msgs[i]).ModifyAsync(msg => { msg.Embed = GetEmbed(guild, channel, msgs[0], standardMessage, dbguild); msg.Content = ""; });
                    } else if(msgs[i].Content != table1[i]) {
                        await ((RestUserMessage)msgs[i]).ModifyAsync(msg => { msg.Embed = null; msg.Content = table1[i]; });
                        if(table1[i].Contains("**Standards**"))
                            standardMessage = msgs[i];
                    }
                } else if(msgs.Count <= i) {
                    if(table1[i] == "@@@EMBED") {
                        if(msgs.Count > 0) {
                            await channel.SendMessageAsync(embed: GetEmbed(guild, channel, msgs[0], standardMessage, dbguild));
                        }
                    } else {
                        var msg = await channel.SendMessageAsync(table1[i]);
                        if(table1[i].Contains("**Standards**"))
                            standardMessage = msg;
                    }
                } else {
                    await ((RestUserMessage)msgs[i]).ModifyAsync(msg => { msg.Content = "\u17B5"; msg.Embed = null; });
                    //await ((RestUserMessage)msgs[i]).DeleteAsync();
                }
            }
        }

        private Embed GetEmbed(SocketGuild guild, SocketTextChannel channel, IMessage FirstMessage, IMessage StandardMessage, Guild DbGuild) {
            var baselink = $"https://discord.com/channels/{guild.Id}/{channel.Id}/";
            var embedBuilder = new EmbedBuilder()
                .WithTitle("Leaderboard Shortcuts")
                .WithTimestamp(DateTimeOffset.Now)
                .WithFooter($"Updates Every {UpdateInterval.TotalMinutes} Minutes - Last Updated")
            ;

            if(!string.IsNullOrEmpty(DbGuild.LeaderboardImage))
                embedBuilder.WithImageUrl(DbGuild.LeaderboardImage);

            if(DbGuild.ChannelDetails.Any(y => y.ChannelType == GuildChannelType.StandardCategory))
                embedBuilder
                    .WithDescription($"[Top of Leaderboard/Elites]({baselink}{FirstMessage.Id})\n[Standards]({baselink}{StandardMessage?.Id})\n[egg9000.com Leaderboard](https://egg9000.com/home/leaderboard)");
            else
                embedBuilder
                .WithDescription($"[Top of Leaderboard]({baselink}{FirstMessage.Id})\n[egg9000.com Leaderboard](https://egg9000.com/home/leaderboard)");
            return embedBuilder.Build();
        }

        public List<string> GetTables(List<LeaderboardUser> users, SocketGuild guild) {
            users = users.OrderByDescending(x => x.Backup.EarningsBonus).Where(x => x.DiscordUser != null).ToList();

            var msgs = new List<string>();
            msgs.AddRange(GetTable("", users));

            return msgs;
        }

        private List<string> GetTable(string name, IEnumerable<LeaderboardUser> users) {
            var tableElite = new List<List<FixedWidthCell>>();
            tableElite.Add(new List<FixedWidthCell> {
                new FixedWidthCell("", CellAlignment.Center),
                new FixedWidthCell("Discord Name", CellAlignment.Center),
                new FixedWidthCell("SE", CellAlignment.Center),
                new FixedWidthCell("PE", CellAlignment.Center),
                new FixedWidthCell("EB", CellAlignment.Center),
            });
            var i = 1;
            tableElite.AddRange(users.Select(x => GetRow(x, i++)));
            var stringToSplit = $"{name}```\n{FixedWidthTable.GetTable(tableElite)}```";
            var msgs = new List<string>();
            while(stringToSplit.Length > 2000) {
                var index = stringToSplit.LastIndexOf('\n', 1996);
                msgs.Add(stringToSplit.Substring(0, index) + "```\n");
                stringToSplit = "```" + stringToSplit.Substring(index);
            }
            if(stringToSplit.Length > 0) {
                msgs.Add(stringToSplit);
            }
            return msgs;
        }

        private List<FixedWidthCell> GetRow(LeaderboardUser x, int i) {
            return new List<FixedWidthCell> {
                    new FixedWidthCell((i++).ToString()),
                    new FixedWidthCell(ContractUpdater.Truncate(Regex.Replace(x.DiscordUser.GetCleanName(), @"\p{Cs}", ""), 12)),
                    new FixedWidthCell(x.Backup.SoulEggs.ToEggString(), CellAlignment.Right),
                    new FixedWidthCell(x.Backup.EggsOfProphecy.ToString(), CellAlignment.Right),
                    new FixedWidthCell(x.Backup.EarningsBonus.ToEggString(), CellAlignment.Right),
                };
        }
    }
}