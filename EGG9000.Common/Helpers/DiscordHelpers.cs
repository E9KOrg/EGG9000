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

namespace EGG9000.Bot.Helpers {
    public static class DiscordHelpers {
        public static String GetName(this IGuildUser user) {
            return string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
        }

        public static String GetCleanName(this IGuildUser user) {
            //if(user == null)
            //{
            //    return "";
            //}
            var name = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
            var ebrgx = new Regex(@"\(.+?\)");
            name = ebrgx.Replace(name, "").Trim();

            return name;
        }

        public static SocketTextChannel GetLeaderboardChannel(this SocketGuild guild) {
            return guild.TextChannels.FirstOrDefault(x => x.Name.ToLower().Contains("leaderboard"));
        }

        public static SocketTextChannel GetWelcomeChannel(this SocketGuild guild) {
            return guild.TextChannels.FirstOrDefault(x => x.Name.ToLower().Contains("welcome"));
        }

        public static SocketTextChannel GetRulesChannel(this SocketGuild guild) {
            var channel = guild.TextChannels.OrderBy(x => x.Position).FirstOrDefault(x => x.Name.ToLower() == "rules");
            if (channel != null)
                return channel;
            return guild.TextChannels.FirstOrDefault(x => x.Name.ToLower().Contains("rules"));
        }

        public static SocketTextChannel GetFaqChannel(this SocketGuild guild) {
            return guild.TextChannels.OrderBy(x => x.Position).FirstOrDefault(x => x.Name.ToLower().Contains("faq"));
        }

        public static SocketTextChannel GetEventChannel(this SocketGuild guilde) {
            return guilde.TextChannels.FirstOrDefault(x => x.Name.ToLower().Contains("game-events"));
        }

        public static async Task SendToGeneralChannel(this SocketGuild guild, string msg) {
            var channel = guild.TextChannels.FirstOrDefault(x => x.Name.ToLower().Contains("general"));
            await channel.SendMessageAsync(msg);
        }

        public static async Task SendToGeneralChannel(this SocketGuild guild, Discord.Embed embed) {
            var channel = guild.TextChannels.FirstOrDefault(x => x.Name.ToLower().Contains("general"));
            await channel.SendMessageAsync(embed: embed);
        }

        //public static SocketChannel GetFinishedCoopCategory(this SocketGuild guild) {
        //    var channel = guild.Channels.FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && x.Name.ToLower().Contains("finished") && !x.Name.Contains("2") && !x.Name.Contains("3") && !x.Name.Contains("4") && !x.Name.Contains("5"));
        //    if (guild.TextChannels.Count(x => x.CategoryId == channel.Id) >= 50) {
        //        return guild.GetFinishedCoopCategory2();
        //    }
        //    return channel;
        //}

        public static SocketChannel GetFailedCoopCategory(this SocketGuild guild) {
            var channel = guild.Channels.FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && x.Name.ToLower().Contains("failed") && !x.Name.Contains("2") && !x.Name.Contains("3") && !x.Name.Contains("4"));
            return channel;
        }

        //public static SocketChannel GetFinishedCoopCategory2(this SocketGuild guild) {
        //    return guild.Channels.FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && x.Name.ToLower().Contains("finished") && x.Name.Contains("2"));
        //}

        //public static SocketChannel GetFinishedCoopCategory3(this SocketGuild guild) {
        //    return guild.Channels.FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && x.Name.ToLower().Contains("finished") && x.Name.Contains("3"));
        //}

        //public static SocketChannel GetFinishedCoopCategory4(this SocketGuild guild) {
        //    return guild.Channels.FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && x.Name.ToLower().Contains("finished") && x.Name.Contains("4"));
        //}

        //public static SocketChannel GetFinishedCoopCategory5(this SocketGuild guild) {
        //    return guild.Channels.FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && x.Name.ToLower().Contains("finished") && x.Name.Contains("5"));
        //}

        public static List<SocketGuildChannel> GetCoopCategories(this SocketGuild guild) {
            var categories = guild.Channels.Where(x => x.Name != null).Where(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed")).OrderBy(x => x.Position);
            return categories.ToList();
        }

        public static SocketChannel GetCoopCategory(this SocketGuild guild) {
            var coopCategory = guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed") && !x.Name.Contains("2") && !x.Name.Contains("3") && !x.Name.Contains("4"));
            if (guild.TextChannels.Count(x => x.CategoryId == coopCategory.Id) >= 50) {
                return guild.GetOverflowCoopCategory();
            }

            return coopCategory;
        }

        public static SocketChannel GetOverflowCoopCategory(this SocketGuild guild) {
            return guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed") && x.Name.Contains("2"));
        }

        public static SocketChannel GetOverflowCoopCategory2(this SocketGuild guild) {
            return guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed") && x.Name.Contains("3"));
        }

        public static SocketChannel GetOverflowCoopCategory3(this SocketGuild guild) {
            return guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed") && x.Name.Contains("4"));
        }

        public static SocketChannel GetContractsCategory(this SocketGuild guild, bool Elite) {
            SocketGuildChannel channel;
            if(Elite) {
                channel = guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => x.Name.ToLower().Contains("elite-contracts"));
                
            } else {
                return guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => x.Name.ToLower().Contains("standard-contracts"));
            }
            if(channel == null)
                channel = guild.Channels.Where(x => x.Name != null).FirstOrDefault(x => x.Name.ToLower().Contains("contracts"));
            return channel;
        }

        //public static async Task SentToContractsChannelAsync(this DiscordSocketClient client, string msg) {
        //    foreach (var channel in client.Guilds.SelectMany(x => x.TextChannels.Where(y => y.Name == "current-contract-discussion" || y.Name == "general-discussion"))) {
        //        await channel.SendMessageAsync(msg);
        //    }

        //    //foreach (var channel in client.GroupChannels.Where(x => x.Name == "current-contract-discussion")) {
        //    //    await channel.SendMessageAsync(msg);
        //    //}
        //}

        public class CheckEliteResposne {
            public bool Promoted { get; set; }
            public SocketRole Role { get; set; }
        }
        public static async Task<CheckEliteResposne> CheckElite(SocketGuild Guild, IGuildUser DiscordUser, List<Double> EarningsBonuses) {
            var response = new CheckEliteResposne();
            if (Guild.Roles.Any(x => x.Name.ToLower() == "elite contract")) {
                var elite = EarningsBonuses.Any(x => x >= 10000000000000);
                var standard = EarningsBonuses.Any(x => x < 10000000000000);

                var eliteRole = Guild.Roles.FirstOrDefault(x => x.Name.ToLower() == "elite contract");
                var standardRole = Guild.Roles.FirstOrDefault(x => x.Name.ToLower() == "standard contract");

                var hasElite = DiscordUser.RoleIds.Any(x => x == eliteRole.Id);
                var hasStandard = DiscordUser.RoleIds.Any(x => x == standardRole.Id);

                if(elite && !hasElite) {
                    response.Promoted = true;
                    response.Role = eliteRole;

                    await DiscordUser.AddRoleAsync(eliteRole);
                }
                if (standard && !hasStandard) {
                    if (response.Role == null)
                        response.Role = standardRole;
                    await DiscordUser.AddRoleAsync(standardRole);
                }

                if (!standard && hasStandard) {
                    await DiscordUser.RemoveRoleAsync(standardRole);
                }

                if (!elite && hasElite) {
                    await DiscordUser.RemoveRoleAsync(eliteRole);
                }
            }
            return response;
        }

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
        public static async Task CheckPermitRoles(SocketGuild Guild, IGuildUser DiscordUser, List<CustomBackup> backups) {
            Console.WriteLine($"Checking Permit for {DiscordUser.GetName()}");
            if(Guild.Roles.Any(x => x.Id == ProPermitRoleID)) {
                var hasPro = DiscordUser.RoleIds.Any(x => x == ProPermitRoleID);
                var hasStandard = DiscordUser.RoleIds.Any(x => x == StandardPermitRoleID); ;

                var needsPro = backups.Any(x => x.PermitLevel == 1);
                var needsStandard = backups.Any(x => x.PermitLevel == 0);


                if(!hasPro && needsPro) {
                    await DiscordUser.AddRoleAsync(Guild.Roles.First(x => x.Id == ProPermitRoleID));
                    Console.WriteLine($"Adding ProPermit role for {DiscordUser.GetName()}");
                }
                if(hasPro && !needsPro) {
                    await DiscordUser.RemoveRoleAsync(Guild.Roles.First(x => x.Id == ProPermitRoleID));
                    Console.WriteLine($"Removing ProPermit role for {DiscordUser.GetName()}");
                }
                if(!hasStandard && needsStandard) {
                    await DiscordUser.AddRoleAsync(Guild.Roles.First(x => x.Id == StandardPermitRoleID));
                    Console.WriteLine($"Adding StandardPermit role for {DiscordUser.GetName()}");

                }
                if(hasStandard && !needsStandard) {
                    await DiscordUser.RemoveRoleAsync(Guild.Roles.First(x => x.Id == StandardPermitRoleID));
                    Console.WriteLine($"Removing StandardPermit role for {DiscordUser.GetName()}");
                }

            }
        }

        public static async Task CheckHatchlingRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            if (Guild.Roles.Any(x => x.Name.ToLower().Contains("hatchling"))) {
                var needsRole = user.CreateOn > DateTimeOffset.Now.AddDays(-21);
                var role = Guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("hatchling"));
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if (!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);

                }
                if (hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                }

            }
        }

        public static async Task CheckFreshEggsRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var role = Guild.Roles.FirstOrDefault(x => x.Id == 761005564615983152);
            if(role != null) {
                var needsRole = user.CreateOn > DateTimeOffset.Now.AddDays(-7);
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);

                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                }

            }
        }


        public static async Task CheckActive(SocketGuild Guild, IGuildUser DiscordUser, DBUser user, IGrouping<Guid, LeaderboardUser> userAccounts) {
            var role = Guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
            if(role != null) {
                var needsRole = user.CreateOn > DateTimeOffset.Now.AddDays(-14) 
                    || userAccounts.Any(x => x.Last1 || x.Last2 || x.Last3 || x.Last4 || x.Last5) 
                    || userAccounts.Any(ua => ua.Backup.Farms.Any(f => f.Started > DateTimeOffset.Now.AddDays(-7)));
                var hasRole = DiscordUser.RoleIds.Any(x => x == role.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(role);
                    Console.WriteLine($"Adding active role for {DiscordUser.GetName()}");
                    await Task.Delay(500);
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(role);
                    Console.WriteLine($"Removing active role for {DiscordUser.GetName()}");
                    await Task.Delay(500);
                }

            }
        }



        public static async Task<SocketRole> SetRole(SocketGuild Guild, IGuildUser DiscordUser, Double EarningsBonus) {
            var currentRole = DiscordUser.RoleIds.Select(y => Guild.Roles.First(z => z.Id == y)).FirstOrDefault(x => x.Name.ToUpper().Contains("FARMER"));
            var rolename = currentRole?.Name;
            var newRoleName = (SIPrefix.GetPrefix(EarningsBonus / 100).Name + "farmer").FirstCharToUpper();
            var newRole = Guild.Roles.FirstOrDefault(x => x.Name.Contains(newRoleName));

            if (newRoleName != rolename) {
                if (currentRole != null) {
                    await DiscordUser.RemoveRoleAsync(currentRole);
                }
                if (newRole != null) {
                    
                    await DiscordUser.AddRoleAsync(newRole);
                }
            }

            return newRole;
        }
    }
}
