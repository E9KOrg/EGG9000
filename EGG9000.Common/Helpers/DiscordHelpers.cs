using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;

using EGG9000.Common.Database;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;
using Discord.Rest;
using System.Threading;
using EGG9000.Common.Services;
using System.Data;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Helpers {
    public static class DiscordHelpersExt {
        public static String GetName(this IGuildUser user) {
            return string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
        }

        public static String GetCleanName(this IGuildUser user) {
            var name = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
            var ebrgx = new Regex(@"\(.+?\)");
            name = ebrgx.Replace(name, "").Trim();

            return name;
        }

        public static async Task DeleteMessagesBatchAsync(this ITextChannel channel, IEnumerable<IMessage> messages) {
            if(messages.Count() == 0) return;
            var timeSplit = DateTimeOffset.Now.AddDays(-14).AddHours(1);
            var oldMessages = messages.Where(x => x.Timestamp <= timeSplit);
            var recentMessages = messages.Where((x) => x.Timestamp > timeSplit);
            await channel.DeleteMessagesAsync(recentMessages);
            foreach(var message in oldMessages) {
                await message.DeleteAsync();
            }
        }

        public static Task ModifyWithTimeoutAsync(this IUserMessage message, Action<MessageProperties> msgProperties, RequestOptions options = null) {
            CancellationTokenSource tokenSource2 = new CancellationTokenSource();
            CancellationToken token2 = tokenSource2.Token;
            if(options is null)
                options = new RequestOptions();
            options.CancelToken = token2;

            var thread = message.ModifyAsync(msgProperties, options);
            tokenSource2.CancelAfter(9000);
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;
            var timer = Task.Delay(10000, token);
            Task.WaitAny(thread, timer);
            if(timer.IsCompleted) {
                GetLogger<IUserMessage>().LogWarning($"Timer Expired");
            } else {
                tokenSource.Cancel();
                if(thread.IsCanceled) {
                    GetLogger<IUserMessage>().LogWarning($"Modify Task CANCELLED!");
                }
            }

            return Task.CompletedTask;
        }
    }
    public class DiscordHelpers {
        public static async Task CheckSiloResearch(SocketGuild Guild, IGuildUser DiscordUser, List<CustomBackup> backups) {
            if(Guild.Roles.Any(x => x.Name.ToLower() == "needssiloepicresearch")) {
                var needsResearch = false;

                var role = Guild.Roles.FirstOrDefault(x => x.Name.ToLower() == "needssiloepicresearch");
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                foreach(var backup in backups) {
                    var awayTime = Research.GetTotalSiloCapacity(backup);
                    var hasPermit = backup.PermitLevel > 0;
                    if(awayTime < 72 || (!hasPermit && awayTime < 120)) {
                        needsResearch = true;
                    }
                }
                if(!hasRole && needsResearch) {
                    await DiscordUser.AddRoleAsync(role);

                }
                if(hasRole && !needsResearch) {
                    await DiscordUser.RemoveRoleAsync(role);
                }

            }
        }


        public static ulong ProPermitRoleID = 966017147350446121;
        public static ulong StandardPermitRoleID = 966017278078517248;
        public static async Task CheckPermitRoles(SocketGuild Guild, IGuildUser DiscordUser, IGrouping<Guid, LeaderboardUser> accounts) {
            if(Guild.Roles.Any(x => x.Id == ProPermitRoleID)) {
                var hasPro = DiscordUser.RoleIds.Any(x => x == ProPermitRoleID);
                var hasStandard = DiscordUser.RoleIds.Any(x => x == StandardPermitRoleID); ;

                var needsPro = accounts.Any(x => x.Backup.PermitLevel == 1);
                var needsStandard = accounts.Any(x => x.Backup.PermitLevel == 0);


                if(!hasPro && needsPro) {
                    await DiscordUser.AddRoleAsync(Guild.Roles.First(x => x.Id == ProPermitRoleID));
                    GetLogger<DiscordHelpers>().LogInformation("Adding ProPermit role for {user}", DiscordUser.GetName());
                }
                if(hasPro && !needsPro) {
                    await DiscordUser.RemoveRoleAsync(Guild.Roles.First(x => x.Id == ProPermitRoleID));
                    GetLogger<DiscordHelpers>().LogInformation("Removing ProPermit role for {user}", DiscordUser.GetName());
                }
                if(!hasStandard && needsStandard) {
                    await DiscordUser.AddRoleAsync(Guild.Roles.First(x => x.Id == StandardPermitRoleID));
                    GetLogger<DiscordHelpers>().LogInformation("Adding StandardPermit role for {user}", DiscordUser.GetName());

                }
                if(hasStandard && !needsStandard) {
                    await DiscordUser.RemoveRoleAsync(Guild.Roles.First(x => x.Id == StandardPermitRoleID));
                    GetLogger<DiscordHelpers>().LogInformation("Removing StandardPermit role for {user}", DiscordUser.GetName());
                }

            }
        }
        public static async Task CheckGrades(SocketGuild Guild, IGuildUser DiscordUser, IGrouping<Guid, LeaderboardUser> accounts, List<(Ei.Contract.Types.PlayerGrade grade, SocketRole role)> grades) {
            var dbuser = accounts.First().User;
            var neededGrades = dbuser.EggIncAccounts.Select(x => x.GetGrade());

            var neededRoles = neededGrades.Select(x => grades.First(g => g.grade == x).role).Where(x => x is not null && !DiscordUser.RoleIds.Any(y => y == x.Id)).ToList();

            var extraRoles = grades.Where(x => x.role is not null)
                .Where(g => 
                    !accounts.Any(a =>  a.Account.GetGrade() == g.grade) && 
                    DiscordUser.RoleIds.Any(r => r == g.role.Id)
                ).Select(x => x.role).ToList();

            if(neededRoles.Count() > 0) {
                GetLogger<DiscordHelpers>().LogInformation("Adding grade roles {roles} for {user}", String.Join(",", neededRoles.Select(x => x.Name)), DiscordUser.GetName());
                await DiscordUser.AddRolesAsync(neededRoles);

            }


            if(extraRoles.Count() > 0) {
                GetLogger<DiscordHelpers>().LogInformation("Removing grade roles {roles} for {user}", String.Join(",", extraRoles.Select(x => x.Name)), DiscordUser.GetName());
                await DiscordUser.RemoveRolesAsync(extraRoles);
            }
        }

        public static async Task CheckHatchlingRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            if(Guild.Roles.Any(x => x.Name.ToLower().Contains("hatchling"))) {
                var needsRole = user.Registered > DateTimeOffset.Now.AddDays(-21);
                var role = Guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("hatchling"));
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);

                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                }

            }
        }
        public static async Task CheckOudatedGameRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var role = await _client.GetRoleAsync(GuildChannelType.GameVersionOutdated, Guild); ;
            if(role != null) {
                var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => x.Backup.ClientVersion > 0 && x.Backup.ClientVersion < ContractsAPI.ClientVersion);
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);
                    GetLogger<DiscordHelpers>().LogInformation("Adding outdated role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                    GetLogger<DiscordHelpers>().LogInformation("Removing outdated role for {user}", DiscordUser.GetName());
                }

            }
        }

        public static async Task CheckFreshEggsRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var role = Guild.Roles.FirstOrDefault(x => x.Id == 761005564615983152);
            if(role != null) {
                var needsRole = user.Registered > DateTimeOffset.Now.AddDays(-7);
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);

                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                }

            }
        }


        public static async Task CheckActive(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user, IGrouping<Guid, LeaderboardUser> userAccounts) {
            var role = await _client.GetRoleAsync(GuildChannelType.ActiveRole, Guild); ;
            if(role != null) {
                var needsRole = isActive(user, userAccounts);
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);
                    GetLogger<DiscordHelpers>().LogInformation("Adding active role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                    GetLogger<DiscordHelpers>().LogInformation("Removing active role for {user}", DiscordUser.GetName());
                }

            }
        }

        private static bool isActive(DBUser user, IGrouping<Guid, LeaderboardUser> userAccounts) {
            return user.Registered > DateTimeOffset.Now.AddDays(-14)
                    || userAccounts.Any(x => x.Last1 || x.Last2 || x.Last3 || x.Last4 || x.Last5)
                    || userAccounts.Any(ua => ua.Backup.Farms.Any(f => f.Started > DateTimeOffset.Now.AddDays(-7)));
        }


        public static async Task CheckBG(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user, IGrouping<Guid, LeaderboardUser> userAccounts) {
            var role = await _client.GetRoleAsync(GuildChannelType.MissingBoardingGroupRole, Guild);
            if(role != null) {
                var needsRole = isActive(user, userAccounts) && user.EggIncAccounts.Any(y => y.Group == 0);
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);
                    GetLogger<DiscordHelpers>().LogInformation("Adding missingbg role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                    GetLogger<DiscordHelpers>().LogInformation("Removing missingbg for {user}", DiscordUser.GetName());
                }

            }
        }



        public static async Task<SocketRole> SetRole(SocketGuild Guild, IGuildUser DiscordUser, Double EarningsBonus, Bugsnag.IClient client) {
            client.Breadcrumbs.Leave("SetRoleStart", Bugsnag.BreadcrumbType.State, new Dictionary<string, string> { { "guild", Guild.Name }, { "user", DiscordUser.GetName() } });
            
            var currentRole = DiscordUser.RoleIds.Select(y => Guild.Roles.FirstOrDefault(z => z.Id == y)).Where(x => x is not null).FirstOrDefault(x => x.Name.ToUpper().Contains("FARMER"));
            var rolename = currentRole?.Name;
            var prefix = SIPrefix.GetPrefixFromEB(EarningsBonus);
            var newRoleName = prefix.Rank;
            var newRoleNameWithSuffix = prefix.RankWithSubRank;

            var newRole = Guild.Roles.FirstOrDefault(x => x.Name.Equals(newRoleNameWithSuffix, StringComparison.OrdinalIgnoreCase));
            if(newRole is null)
                newRole = Guild.Roles.FirstOrDefault(x => x.Name.Equals(newRoleName, StringComparison.OrdinalIgnoreCase));
            else
                newRoleName = newRoleNameWithSuffix;

            client.Breadcrumbs.Leave("Current Role", Bugsnag.BreadcrumbType.State, new Dictionary<string, string> { { "currentRole", currentRole?.Name }, { "newRole", newRole?.Name }, { "roleName", rolename },{ "newRoleName", newRoleName } });

            if(!newRoleName.Equals(rolename, StringComparison.CurrentCultureIgnoreCase) && (currentRole is not null || newRole is not null)) {
                GetLogger<DiscordHelpers>().LogInformation("Updating roles from {exisitingrole} to {newrolename} ({current} -> {new})", rolename, newRoleName, currentRole?.Name, newRole?.Name);
                if(currentRole != null) {
                    await DiscordUser.RemoveRoleAsync(currentRole);
                }
                if(newRole != null) {

                    await DiscordUser.AddRoleAsync(newRole);
                }
            }

            return newRole;
        }


    }
}
