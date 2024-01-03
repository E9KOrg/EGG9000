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
using Microsoft.Extensions.Logging;
using EGG9000.Common.Factories;
using EGG9000.Bot.Common.Helpers;

namespace EGG9000.Bot.Automated {
    public class LeaderboardUpdater : _UpdaterBase<LeaderboardUpdater> {
        public static TimeSpan UpdateTime = TimeSpan.FromMinutes(60);

        public static List<UserX> _users;


        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public LeaderboardUpdater(
            IServiceProvider provider
        ) : base(UpdateTime, delayedStart: TimeSpan.FromMinutes(5), provider) {
        }

        private class BreakCooper {
            public LeaderboardUser User { get; set; }
            public CustomFarm Farm { get; set; }
        }


        public override async Task Run(object state, CancellationToken cancellationToken) {
            var timings = new TimingsFactory(_logger);
            timings.Start();

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recentContracts = await _db.Contracts.AsQueryable().Where(x => x.MaxUsers > 1).OrderByDescending(x => x.Created).Take(5).ToListAsync();
            timings.Set("recentContracts");
            _logger.LogInformation("Getting Xrefs for Leaderboard");
            try {
                var threeWeeksAgo = DateTimeOffset.Now.AddDays(-21);

                var recentxrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.JoinedCoop && x.CreatedOn >= threeWeeksAgo).Select(x => new SimpleXref {
                    UserId = x.UserId, ContractID = x.Coop.ContractID, EggIncId = x.EggIncId, Joined = x.JoinedCoop
                }).ToListAsync(cancellationToken);
                timings.Set("recentxrefs");
                var oldXrefs = await _db.UserCoopXrefs.Where(x => x.JoinedCoop && x.CreatedOn < threeWeeksAgo).GroupBy(x => x.UserId).Select(x => x.Key).ToListAsync();
                timings.Set("oldXrefs");

                if(cancellationToken.IsCancellationRequested)
                    return;

                _logger.LogInformation("Getting Users for Leaderboard");

                var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
                timings.Set("dbguilds");


                var userQuery = _db.DBUsers.Where(x => x.GuildId > 0 && !x.TempDisabled);

#if DEBUG
                userQuery = userQuery.Where(x => x.DiscordId == 760856957011230760);
                //dbusers = dbusers.Where(x => x.GuildId == 770469712064151593).ToList();
                dbguilds = dbguilds.Where(x => x.Id == 770469712064151593).ToList();
#endif


                var dbusers = await userQuery.ToListAsync(cancellationToken);
                timings.Set("dbusers");
                if(cancellationToken.IsCancellationRequested)
                    return;




                _logger.LogInformation("Getting User Backups for Leaderboard");

                var lUsers = dbusers.SelectMany(x => x.EggIncAccounts.Select(y => new LeaderboardUser {
                    User = x,
                    Backup = y.Backup
                })).Where(x => x.Backup is not null).ToList();

                foreach(var lUser in lUsers) {
                    if(cancellationToken.IsCancellationRequested)
                        return;

                    if(lUser.Backup == null)
                        return;

                    var recentUserXrefs = recentxrefs.Where(x => x.UserId == lUser.User.Id).ToList();
                    lUser.Last1 = recentUserXrefs.Any(x => x.ContractID == recentContracts[0].ID);
                    lUser.Last2 = recentUserXrefs.Any(x => x.ContractID == recentContracts[1].ID);
                    lUser.Last3 = recentUserXrefs.Any(x => x.ContractID == recentContracts[2].ID);
                    lUser.Last4 = recentUserXrefs.Any(x => x.ContractID == recentContracts[3].ID);
                    lUser.Last5 = recentUserXrefs.Any(x => x.ContractID == recentContracts[4].ID);
                    lUser.RecentXrefs = recentUserXrefs;
                }
                timings.Set("Process Users");
                timings.Finished();

                foreach(var dbguild in dbguilds) {
                    if(cancellationToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation("Working on leaderboard for {guild}", dbguild.Name);

                    var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                    await guild.DownloadUsersAsync();

                    List<SocketGuild> overflowGuilds = null;
                    if(dbguild.OverflowServers.Count > 0) {
                        overflowGuilds = dbguild.OverflowServers.Select(x => _client.Guilds.First(y => y.Id == x)).ToList();
                    }


                    var users = lUsers.Where(x => x.User.GuildId == guild.Id).ToList();
                    var guildContracts = await _db.GuildContracts.Where(gc => gc.GuildID == dbguild.Id).ToListAsync();

                    var breakCoopsChannel = ChannelHelper.DetermineChannelType(dbguild, guild, GuildChannelType.BreakCoopLog);
                    if(breakCoopsChannel is not null) {
                        _logger.LogInformation("Handling on-break coop warnings for {guild}", guild.Name);
                        var joinedCoopOnBreak = users.Where(ua =>
                            !ua.Account.BreakCoopWarningSent
                            && ua.Backup.Farms != null
                            && ua.Account.OnBreakUntil != default
                            && ua.Account.OnBreakUntil > DateTimeOffset.Now
                            && ua.Backup.Farms.Any(f => f.FarmType == Ei.FarmType.Contract && f.Started > ua.Account.BreakSetTime)
                        ).ToList().Select(u => new BreakCooper() {
                            User = u,
                            Farm = u.Backup.Farms.First(f => f.FarmType == Ei.FarmType.Contract && f.Started > u.Account.BreakSetTime)
                        });

                        foreach(var breakCooper in joinedCoopOnBreak) {
                            if(_db.Coops.Any(c => c.Name.ToLower() == breakCooper.Farm.CoopId.ToLower() && dbguild.OverflowServersJson.Contains(c.GuildId.ToString()) || dbguild.Id == c.GuildId)) continue;
                            var guildContract = guildContracts.FirstOrDefault(gc => gc.GuildID == dbguild.Id && gc.ContractID.ToLower() == breakCooper.Farm.ContractId.ToLower());
                            var message = $"<@{breakCooper.User.User.DiscordId}>{(breakCooper.User.User.EggIncAccounts.Count > 1 ? $" ({breakCooper.User.Account.Name ?? breakCooper.User.Account.Backup.UserName ?? "Unknown"}) " : " ")}" +
                                $"is currently on break that ends {DiscordHelpers.TimeStamper(breakCooper.User.Account.OnBreakUntil)}, and joined a coop " +
                                $"(`{breakCooper.Farm.CoopId}`) for {(guildContract is not null ? $"<#{guildContract.DiscordChannelId}>" : $"`{breakCooper.Farm.ContractId ?? "???"}`")}";

                            var result = await ChannelHelper.DetermineAndSend(_db, _client, dbguild, guild, GuildChannelType.BreakCoopLog, new() { Text = message });

                            breakCooper.User.Account.BreakCoopWarningSent = true;
                            breakCooper.User.User.UpdateAccounts();
                        }
                        await _db.SaveChangesAsync();
                    }

                    //Handle promotions
                    _logger.LogInformation("Handling promotions for {guild}", guild.Name);

                    foreach(var userAccounts in users.GroupBy(x => x.User.Id)) {
                        if(cancellationToken.IsCancellationRequested)
                            break;

                        var dbUser = userAccounts.First().User;
                        var discordUser = guild.GetUser(dbUser.DiscordId);
                        if(discordUser == null || userAccounts.All(x => x.Backup?.Farms == null || x.Backup?.Farms.Count == 0))
                            continue;
                        userAccounts.ToList().ForEach(y => {
                            y.DiscordUser = discordUser;
                            y.User.DiscordUsername = discordUser.Nickname ?? discordUser.Username;
                        });

                        if(!dbUser.showEB && !string.IsNullOrEmpty(discordUser.Nickname) && discordUser.GetCleanName() != discordUser.Nickname && discordUser.Guild.OwnerId != discordUser.Id) {
                            try {
                                _logger.LogInformation("Updating {user} to {newname}", discordUser.Nickname, discordUser.GetCleanName());
                                await discordUser.ModifyAsync(x => x.Nickname = discordUser.GetCleanName());
                            } catch(Exception) {
                                _logger.LogWarning("Unable to change name of {user}", discordUser.GetName());
                            }
                        }
                        _ = await DiscordHelpers.CheckRoles(_db, guild, discordUser, dbUser, _client, await DiscordHelpers.GetGradeRoles(_client, guild), userAccounts.ToList());
                    }

                    await PostOverallLeaderboard(guild, users, recentContracts, _db);
                    _logger.LogInformation("Finished updating Leaderboard");
                }
            } catch(Exception e) {
                _bugsnag.Notify(e);
                _logger.LogError(e, "**************ERROR in LeaderboardUpdater**********")
;
            }


        }

        private async Task PostOverallLeaderboard(SocketGuild guild, List<LeaderboardUser> lUsers, List<Contract> recentContracts, ApplicationDbContext _db) {
            var channel = await _client.GetChannelAsync(GuildChannelType.Leaderboard, guild);
            if(channel == null)
                return;

            lUsers = lUsers.Where(x => x.Backup != null).ToList();
            var activeUsers = lUsers.Where(x => x.Account.Active && x.DiscordUser != null).ToList();
            var inactiveUsers = lUsers.Where(x => !x.Account.Active && x.DiscordUser != null).ToList();

            var dbguild = _db.Guilds.FirstOrDefault(x => x.Id == guild.Id);
            if(dbguild == null) {
                dbguild = new Guild { Name = guild.Name, Id = guild.Id };
                _db.Add(dbguild);
            }


            await _db.SaveChangesAsync();

            var table1 = GetTables(activeUsers, "");

            var str = "";
            str += $"Total Active Accounts: {activeUsers.Count()}\n";
            str += $"Last 5 Contracts: \n{recentContracts[0].ID}: {lUsers.Count(x => x.Last1)}\n{recentContracts[1].ID}: {lUsers.Count(x => x.Last2)}\n{recentContracts[2].ID}: {lUsers.Count(x => x.Last3)}\n{recentContracts[3].ID}: {lUsers.Count(x => x.Last4)}\n{recentContracts[4].ID}: {lUsers.Count(x => x.Last5)}";
            table1.Add(str);

            if(inactiveUsers.Count > 0 && inactiveUsers.Count < 100) {
                var table2 = GetTables(inactiveUsers, "Inactive Users");
                //table2.Prepend("Inactive Account: ");
                table1.AddRange(table2);
            }

            var msgs = (await channel.GetMessagesAsync().FlattenAsync()).ToList();

            msgs = msgs.OrderBy(x => x.CreatedAt).Where(x => x.Author.Id == 514257192803893272).ToList();

            table1.Add("@@@EMBED");



            for(int i = 0; i < Math.Max(msgs.Count, table1.Count); i++) {
                if(msgs.Count > i && table1.Count > i) {
                    if(table1[i] == "@@@EMBED") {
                        await ((RestUserMessage)msgs[i]).ModifyAsync(msg => { msg.Embed = GetEmbed(guild, channel, msgs[0], dbguild); msg.Content = ""; });
                    } else if(msgs[i].Content != table1[i]) {
                        await ((RestUserMessage)msgs[i]).ModifyAsync(msg => { msg.Embed = null; msg.Content = table1[i]; });
                    }
                } else if(msgs.Count <= i) {
                    if(table1[i] == "@@@EMBED") {
                        if(msgs.Count > 0) {
                            await channel.SendMessageAsync(embed: GetEmbed(guild, channel, msgs[0], dbguild));
                        }
                    } else {
                        var msg = await channel.SendMessageAsync(table1[i]);
                    }
                } else {
                    await ((RestUserMessage)msgs[i]).ModifyAsync(msg => { msg.Content = "\u17B5"; msg.Embed = null; });
                    //await ((RestUserMessage)msgs[i]).DeleteAsync();
                }
            }
        }

        private Embed GetEmbed(SocketGuild guild, SocketTextChannel channel, IMessage FirstMessage, Guild DbGuild) {
            var baselink = $"https://discord.com/channels/{guild.Id}/{channel.Id}/";
            var embedBuilder = new EmbedBuilder()
                .WithTitle("Leaderboard Shortcuts")
                .WithTimestamp(DateTimeOffset.Now)
                .WithFooter($"Updates Every {UpdateInterval.TotalMinutes} Minutes - Last Updated")
            ;

            if(!string.IsNullOrEmpty(DbGuild.LeaderboardImage))
                embedBuilder.WithImageUrl(DbGuild.LeaderboardImage);

            embedBuilder
                .WithDescription($"[Top of Leaderboard]({baselink}{FirstMessage.Id})\n[egg9000.com Leaderboard](https://egg9000.com/home/leaderboard)");
            return embedBuilder.Build();
        }

        public List<string> GetTables(List<LeaderboardUser> users, string name) {
            users = users.OrderByDescending(x => x.Backup.EarningsBonus).Where(x => x.DiscordUser != null).ToList();

            var msgs = new List<string>();
            msgs.AddRange(GetTable(name, users));

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