using Discord;
using Discord.Rest;
using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated {
    public class LeaderboardUpdater(IServiceProvider provider) : _UpdaterBase<LeaderboardUpdater>(UpdateTime, delayedStart: TimeSpan.FromMinutes(5), provider) {
        public static readonly TimeSpan UpdateTime = TimeSpan.FromMinutes(60);

        private class BreakCooper {
            public LeaderboardUser User { get; set; }
            public CustomFarm Farm { get; set; }
        }

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var timings = new TimingsFactory(_logger);
            timings.Start();

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recentContracts = await _db.Contracts.AsQueryable().Where(x => x.MaxUsers > 1).OrderByDescending(x => x.Created).Take(5).ToListAsync(CancellationToken.None);
            timings.Set("recentContracts");
            _logger.LogInformation("Getting Xrefs for Leaderboard");
            try {
                var threeWeeksAgo = DateTimeOffset.UtcNow.AddDays(-21);

                var recentxrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.JoinedCoop && x.CreatedOn >= threeWeeksAgo).Select(x => new SimpleXref {
                    UserId = x.UserId, ContractID = x.Coop.ContractID, EggIncId = x.EggIncId, Joined = x.JoinedCoop
                }).ToListAsync(CancellationToken.None);
                timings.Set("recentxrefs");
                var oldXrefs = await _db.UserCoopXrefs.Where(x => x.JoinedCoop && x.CreatedOn < threeWeeksAgo).GroupBy(x => x.UserId).Select(x => x.Key).ToListAsync(CancellationToken.None);
                timings.Set("oldXrefs");

                if(cancellationToken.IsCancellationRequested)
                    return;

                _logger.LogInformation("Getting Users for Leaderboard");

                var dbguilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);
                timings.Set("dbguilds");


                var userQuery = _db.DBUsers.Where(x => x.GuildId > 0 && !x.TempDisabled);

#if DEBUG
                //userQuery = userQuery.Where(x => x.DiscordId == 760856957011230760);
                //dbusers = dbusers.Where(x => x.GuildId == 770469712064151593).ToList();
                //dbguilds = dbguilds.Where(x => x.Id == 770469712064151593).ToList();
#endif


                var dbusers = await userQuery.ToListAsync(CancellationToken.None);
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
                    await WaitOnCoopsBeingCreated(cancellationToken);
                    if(cancellationToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation("Working on leaderboard for {guild}", dbguild.Name);

                    var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                    if(guild is null) {
                        _logger.LogWarning("Unable to find server {server}, {id}", dbguild.Name, dbguild.Id);
                        continue;
                    }
                    await guild.DownloadUsersAsync();

                    List<SocketGuild> overflowGuilds = null;
                    if(dbguild.OverflowServers.Count > 0) {
                        overflowGuilds = dbguild.OverflowServers.Select(x => _client.Guilds.First(y => y.Id == x)).ToList();
                    }


                    var users = lUsers.Where(x => x.User.GuildId == guild.Id).ToList();
                    var guildContracts = await _db.GuildContracts.Where(gc => gc.GuildID == dbguild.Id).ToListAsync(CancellationToken.None);

                    //Handle users who are on break, and doing coops
                    var breakCoopsChannel = ChannelHelper.DetermineChannelType(dbguild, guild, GuildChannelType.BreakCoopLog);
                    if(breakCoopsChannel is not null) {
                        _logger.LogInformation("Handling on-break coop warnings for {guild}", guild.Name);
                        var joinedCoopOnBreak = users.Where(ua =>
                            !ua.Account.BreakCoopWarningSent
                            && ua.Backup.Farms != null
                            && ua.Account.OnBreakUntil != default
                            && ua.Account.OnBreakUntil > DateTimeOffset.UtcNow
                            && ua.Backup.Farms.Any(f => f.FarmType == Ei.FarmType.Contract && f.Started > ua.Account.BreakSetTime)
                        ).ToList().Select(u => new BreakCooper() {
                            User = u,
                            Farm = u.Backup.Farms.First(f => f.FarmType == Ei.FarmType.Contract && f.Started > u.Account.BreakSetTime)
                        });

                        foreach(var breakCooper in joinedCoopOnBreak) {
                            await WaitOnCoopsBeingCreated(cancellationToken);
                            if(cancellationToken.IsCancellationRequested) {
                                continue;
                            }

                            var dbCoop = await _db.Coops.FirstOrDefaultAsync(c => c.Name.ToLower() == breakCooper.Farm.CoopId.ToLower() && (dbguild.OverflowServersJson.Contains(c.GuildId.ToString()) || dbguild.Id == c.GuildId), CancellationToken.None);
                            var guildContract = guildContracts.FirstOrDefault(gc => gc.GuildID == dbguild.Id && gc.ContractID.ToLower() == breakCooper.Farm.ContractId.ToLower());
                            var username = breakCooper.User.Account.Name ?? breakCooper.User.Account.Backup.UserName ?? "Unknown"; if(username == "") username = "Unknown";
                            var message = $"<@{breakCooper.User.User.DiscordId}>{(breakCooper.User.User.EggIncAccounts.Count > 1 ? $" ({username}) " : " ")}" +
                                $"is currently on break that ends {DiscordHelpers.TimeStamper(breakCooper.User.Account.OnBreakUntil)}, and joined a coop " +
                                $"({(dbCoop is not null? $"<#{dbCoop.ThreadID}> - `{dbCoop.Name}`" : $"`{breakCooper.Farm.CoopId}`")}) " +
                                $"for {(guildContract is not null ? $"<#{guildContract.DiscordChannelId}>" : $"`{breakCooper.Farm.ContractId ?? "???"}`")}";

                            var capturedMessage = message;
                            _queue.EnqueueLow(() => ChannelHelper.DetermineAndSend(_client.Gateway, dbguild, GuildChannelType.BreakCoopLog, new() { Text = capturedMessage }));

                            breakCooper.User.Account.BreakCoopWarningSent = true;
                            breakCooper.User.User.UpdateAccounts();
                        }

                        await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                    }

                    //Handle users with suspiciously high Mystical Egg Ratios
                    const int adjustedMerThreshold = 12; //Pre-determined to be a good threshold.
                    var cheaterChannel = ChannelHelper.DetermineChannelType(dbguild, guild, GuildChannelType.CheaterThread);
                    if(cheaterChannel is not null) {
                        _logger.LogInformation("Handling MER cheaters for {guild}", guild.Name);
                        var merCheaters = users.Where(ua => 
                        ua.Account != null && !ua.Account.MERWarningSent && !ua.Account.MERMarkedClean &&
                        ua.Backup != null && ua.Backup.MER / Math.Log10((int)ua.Backup.NumPrestiges) > adjustedMerThreshold
                        );
                        foreach(var merCheater in merCheaters) {
                            if(cancellationToken.IsCancellationRequested)
                                break;

                            var mer = merCheater.Backup.MER;;
                            var username = merCheater.Account.Name ?? merCheater.Account.Backup.UserName ?? "Unknown"; if(username == "") username = "Unknown";
                            var message = $"<@{merCheater.User.DiscordId}>{(merCheater.User.EggIncAccounts.Count > 1 ? $" ({username}) " : " ")} may be cheating. MER is higher than expected, at `{mer:n2}`, after `{(int)merCheater.Backup.NumPrestiges}` prestiges.";

                            var capturedMerMessage = message;
                            _queue.EnqueueLow(() => ChannelHelper.DetermineAndSend(_client.Gateway, dbguild, GuildChannelType.CheaterThread, new() { Text = capturedMerMessage }));

                            merCheater.Account.MERWarningSent = true;
                            merCheater.User.UpdateAccounts();
                        }
                        await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                    }

                    //Handle promotions
                    _logger.LogInformation("Handling promotions for {guild}", guild.Name);

                    foreach(var userAccounts in users.GroupBy(x => x.User.Id)) {
                        await WaitOnCoopsBeingCreated(cancellationToken);
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
                                var capturedUser = discordUser;
                                _logger.LogInformation("Updating {user} to {newname}", discordUser.Nickname, discordUser.GetCleanName());
                                await _queue.EnqueueLowAsync<bool>(async () => { await capturedUser.ModifyAsync(x => x.Nickname = capturedUser.GetCleanName()); return true; });
                            } catch(Exception) {
                                _logger.LogWarning("Unable to change name of {user}", discordUser.GetName());
                            }
                        }
                        _ = await DiscordHelpers.CheckRoles(_db, guild, discordUser, dbUser, _client, await DiscordHelpers.GetGradeRoles(_client, guild), [..userAccounts], _logger);
                    }

                    await PostOverallLeaderboard(guild, users, recentContracts, _db);
                    _logger.LogInformation("Finished updating Leaderboard");
                }
            } catch(Exception e) {
                _bugSnag.Notify(e);
                _logger.LogError(e, "**************ERROR in LeaderboardUpdater**********");
            }


        }

        private async Task PostOverallLeaderboard(SocketGuild guild, List<LeaderboardUser> lUsers, List<Contract> recentContracts, ApplicationDbContext _db) {
            var channel = await _client.GetChannelAsync(GuildChannelType.Leaderboard, guild);
            if(channel == null)
                return;

            lUsers = lUsers.Where(x => x.Backup != null).ToList();
            var activeUsers = lUsers.Where(x => x.Account.Active && x.DiscordUser != null).ToList();
            //var inactiveUsers = lUsers.Where(x => !x.Account.Active && x.DiscordUser != null).ToList();

            var dbguild = _db.Guilds.FirstOrDefault(x => x.Id == guild.Id);
            if(dbguild == null) {
                dbguild = new Guild { Name = guild.Name, Id = guild.Id };
                _db.Add(dbguild);
            }


            await _db.SaveChangesAsync();

            var table1 = GetTables(activeUsers, "");

            var str = "";
            str += $"Total Active Accounts: {activeUsers.Count}\n";
            str += $"Last 5 Contracts: \n{recentContracts[0].ID}: {lUsers.Count(x => x.Last1)}\n{recentContracts[1].ID}: {lUsers.Count(x => x.Last2)}\n{recentContracts[2].ID}: {lUsers.Count(x => x.Last3)}\n{recentContracts[3].ID}: {lUsers.Count(x => x.Last4)}\n{recentContracts[4].ID}: {lUsers.Count(x => x.Last5)}";
            table1.Add(str);

            var msgs = (await channel.GetMessagesAsync().FlattenAsync()).ToList();

            msgs = msgs.OrderBy(x => x.CreatedAt).Where(x => x.Author.Id == 514257192803893272).ToList();

            table1.Add("@@@EMBED");



            for(var i = 0; i < Math.Max(msgs.Count, table1.Count); i++) {
                if(msgs.Count > i && table1.Count > i) {
                    if(table1[i] == "@@@EMBED") {
                        var capturedMsg = (RestUserMessage)msgs[i];
                        var capturedEmbed = GetEmbed(guild, channel, msgs[0], dbguild);
                        _queue.EnqueueLow(() => capturedMsg.ModifyAsync(msg => { msg.Embed = capturedEmbed; msg.Content = ""; }));
                    } else if(msgs[i].Content != table1[i]) {
                        var capturedMsg = (RestUserMessage)msgs[i];
                        var capturedContent = table1[i];
                        _queue.EnqueueLow(() => capturedMsg.ModifyAsync(msg => { msg.Embed = null; msg.Content = capturedContent; }));
                    }
                } else if(msgs.Count <= i) {
                    if(table1[i] == "@@@EMBED") {
                        if(msgs.Count > 0) {
                            var capturedEmbed = GetEmbed(guild, channel, msgs[0], dbguild);
                            _queue.EnqueueLow(() => channel.SendMessageAsync(embed: capturedEmbed));
                        }
                    } else {
                        var capturedContent = table1[i];
                        _queue.EnqueueLow(() => channel.SendMessageAsync(capturedContent));
                    }
                } else {
                    var capturedMsg = (RestUserMessage)msgs[i];
                    _queue.EnqueueLow(() => capturedMsg.ModifyAsync(msg => { msg.Content = "\u17B5"; msg.Embed = null; }));
                    //await ((RestUserMessage)msgs[i]).DeleteAsync();
                }
            }
        }

        private Embed GetEmbed(SocketGuild guild, SocketTextChannel channel, IMessage FirstMessage, Guild DbGuild) {
            var baselink = $"https://discord.com/channels/{guild.Id}/{channel.Id}/";
            var embedBuilder = new EmbedBuilder()
                .WithTitle("Leaderboard Shortcuts")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter($"Updates Every {UpdateInterval.TotalMinutes} Minutes - Last Updated")
            ;

            if(!string.IsNullOrEmpty(DbGuild.LeaderboardImage))
                embedBuilder.WithImageUrl(DbGuild.LeaderboardImage);

            embedBuilder
                .WithDescription($"[Top of Leaderboard]({baselink}{FirstMessage.Id})\n[egg9000.com Leaderboard](https://egg9000.com/home/leaderboard)");
            return embedBuilder.Build();
        }

        public static List<string> GetTables(List<LeaderboardUser> users, string name) {
            users = users.OrderByDescending(x => x.Backup.EarningsBonus).Where(x => x.DiscordUser != null).ToList();
            return GetTable(name, users);
        }

        private static List<string> GetTable(string name, IEnumerable<LeaderboardUser> users) {
            var tableElite = new List<List<FixedWidthCell>> {new() {
                new("", CellAlignment.Center),
                new("Discord Name", CellAlignment.Center),
                new("SE", CellAlignment.Center),
                new("PE", CellAlignment.Center),
                new("EB", CellAlignment.Center),
            }};
            var i = 1;
            tableElite.AddRange(users.Select(x => GetRow(x, i++)));
            var stringToSplit = $"{name}```\n{FixedWidthTable.GetTable(tableElite)}```";
            var msgs = new List<string>();
            while(stringToSplit.Length > 2000) {
                var index = stringToSplit.LastIndexOf('\n', 1996);
                msgs.Add(stringToSplit[..index] + "```\n");
                stringToSplit = "```" + stringToSplit[index..];
            }
            if(stringToSplit.Length > 0) {
                msgs.Add(stringToSplit);
            }
            return msgs;
        }

        private static List<FixedWidthCell> GetRow(LeaderboardUser x, int i) {
            return [
                new(i++.ToString()),
                new(ContractUpdater.Truncate(Regex.Replace(x.DiscordUser.GetCleanName(), @"\p{Cs}", ""), 12)),
                new(x.Backup.SoulEggs.ToEggString(), CellAlignment.Right),
                new(x.Backup.EggsOfProphecy.ToString(), CellAlignment.Right),
                new(x.Backup.EarningsBonus.ToEggString(), CellAlignment.Right),
            ];
        }
    }
}