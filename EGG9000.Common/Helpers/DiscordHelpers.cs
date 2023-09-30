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
using Google.Protobuf.WellKnownTypes;
using MassTransit.Caching.Internals;
using EGG9000.Common.Helpers;

namespace EGG9000.Bot.Helpers {
    public static class DiscordHelpersExt {
        public static string GetName(this IGuildUser user) {
            return string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
        }

        public static string GetCleanName(this IGuildUser user) {
            var name = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
            var ebrgx = new Regex(@"\(.+?\)");
            name = ebrgx.Replace(name, "").Trim();

            return name;
        }

        public static async Task DeleteMessagesBatchAsync(this ITextChannel channel, IEnumerable<IMessage> messages) {
            if(!messages.Any()) return;
            var timeSplit = DateTimeOffset.Now.AddDays(-14).AddHours(1);
            var oldMessages = messages.Where(x => x.Timestamp <= timeSplit);
            var recentMessages = messages.Where((x) => x.Timestamp > timeSplit);
            await channel.DeleteMessagesAsync(recentMessages);
            foreach(var message in oldMessages) {
                await message.DeleteAsync();
            }
        }

        public static async Task<Exception> BoolSendDm(IDMChannel dmChannel, string message) {
            try {
                await dmChannel.SendMessageAsync(message);
                return null;
            } catch(Exception ex) {
                return ex;
            }
        }

        public static Task ModifyWithTimeoutAsync(this IUserMessage message, Action<MessageProperties> msgProperties, RequestOptions options = null) {
            var tokenSource2 = new CancellationTokenSource();
            var token2 = tokenSource2.Token;
            if(options is null)
                options = new RequestOptions();
            options.CancelToken = token2;

            var thread = message.ModifyAsync(msgProperties, options);
            tokenSource2.CancelAfter(9000);
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;
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
        public static async Task<List<(Ei.Contract.Types.PlayerGrade, SocketRole)>> GetGradeRoles(DiscordHostedService _client, SocketGuild guild) {
            return new List<(Ei.Contract.Types.PlayerGrade, SocketRole)> {
                        (Ei.Contract.Types.PlayerGrade.GradeAaa, await _client.GetRoleAsync(GuildChannelType.GradeAAA, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeAa, await _client.GetRoleAsync(GuildChannelType.GradeAA, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeA, await _client.GetRoleAsync(GuildChannelType.GradeA, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeB, await _client.GetRoleAsync(GuildChannelType.GradeB, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeC, await _client.GetRoleAsync(GuildChannelType.GradeC, guild)),
                        (Ei.Contract.Types.PlayerGrade.GradeUnset, null),
                    };
        }

        public static async Task<SocketRole> CheckRoles(SocketGuild guild, SocketGuildUser discordUser, DBUser dbUser, DiscordHostedService _client, List<(Ei.Contract.Types.PlayerGrade, SocketRole)> grades, List<LeaderboardUser> leaderboardUsers) {
            if(grades is null) {
                grades = await GetGradeRoles(_client, guild);
            }

            var higherEB = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus ?? 0).FirstOrDefault();
            if(higherEB.Backup.EggsOfProphecy > 1000) {
                dbUser.showEB = false;
            }


            var registeredRole = (discordUser as SocketGuildUser).Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            var guildRegisteredRole = guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            if(registeredRole == null && guildRegisteredRole is not null) {
                await discordUser.AddRoleAsync(guildRegisteredRole);
            }

            var existingRole = (discordUser as SocketGuildUser).Roles.FirstOrDefault(x => x.Name.ToUpper().Contains("FARMER"));

            var role = await SetRole(guild, discordUser, higherEB.Backup.EarningsBonus, dbUser, _client);

            await CheckSiloResearch(guild, discordUser, dbUser.EggIncAccounts.Select(y => y.Backup).ToList());
            await CheckHatchlingRole(guild, discordUser, dbUser);
            await CheckFreshEggsRole(guild, discordUser, dbUser);
            await CheckBG(_client, guild, discordUser, dbUser);
            await CheckPermitRoles(guild, discordUser, dbUser);
            await CheckGrades(guild, discordUser, dbUser, grades);
            await CheckOudatedGameRole(_client, guild, discordUser, dbUser);
            await CheckUserOSRole(_client, guild, discordUser, dbUser);
            await CheckUnjoined(guild, discordUser, leaderboardUsers.FirstOrDefault(x => x.User.Id == dbUser.Id));
            await CheckEnDRole(_client, guild, discordUser, dbUser);
            await CheckNAHRole(_client, guild, discordUser, dbUser);
            await CheckASCRole(_client, guild, discordUser, dbUser);

            if(leaderboardUsers.Count > 0) {
                await CheckActive(_client, guild, discordUser, dbUser, leaderboardUsers);
            }

            var EarningsBonus = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus ?? 0).FirstOrDefault()?.Backup.EarningsBonus.ToEggString() ?? "";



            if(role != null && existingRole != null && existingRole.Name != role.Name && role.Position > existingRole.Position) {
                var messages = new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! How do you like your eggs in the morning?",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! You should see your eggspression right now, lol",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Eggstraordinary work!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} You made it this far. Looking forward to your next level-up!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Challenge is to never stop prestiging, keep it up!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Prestiging is like a reversed limbo, how high can you go?",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",                            };

                switch(role.Name.Split(" ").First()) {
                    case "Farmer":
                        messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Eggstraordinary work!",
                        });
                        break;
                    case "Kilofarmer":
                        messages.AddRange(new List<string> {
                        $"Wow, {discordUser.Mention}! A {role.Name} already? Your wonders never cease to amaze me! Congrats on the new rank and EB of {EarningsBonus}%!.",
                        });
                        break;
                    case "Megafarmer":
                        messages.AddRange(new List<string> {
                        $"Now you are at least hundreds of millions times stronger than you were since your first chicken. Mega effort to become a {role.Name} with and EB of {EarningsBonus}%! Congratulations on the new rank, {discordUser.Mention}!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",
                        });
                        break;
                    case "Gigafarmer":
                        messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Gigafarmer, sweet! Your numbers are increasing along with your eggsperience!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} You made it this far. Looking forward to your next level-up!",
                        });
                        break;
                    case "Terafarmer":
                        messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Keep going, next up: Petafarmer!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Chickens won't hatch themselves, get back to farming!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Challenge is to never stop prestiging, keep it up!",
                        $"Choo Choo! All aboard the <:Egg_soul_SE:724341890794913964> train with our new {role.Name}. {discordUser.Mention} is driving the train with an EB of {EarningsBonus}%, jump on now!",
                        });
                        break;
                    case "Petafarmer":
                        messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Prestiging is like a reversed limbo, how high can you go?",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! More chickens, more eggs, higher earnings means more <:Egg_soul_SE:724341890794913964>. Keep hatching!",
                        $"With great EB comes great responsibility. Congrats on hitting an EB of {EarningsBonus}%, {discordUser.Mention}! This means you are officially a {role.Name}. Now get back out there - those wormholes aren’t going to dampen themselves!",
                                                        });
                        break;
                    case "Exafarmer":
                        messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! You really like eggs, eh? Eggciting hobby, isnt it?",
                        $"You’ve finally reached the rank of { role.Name}, { discordUser.Mention}! Wow. It seems like just yesterday you were running your first chickens. Celebrate!",
                        $"{ role.Name}: achieved. What’s next, { discordUser.Mention}? This calls for omelets. Anyone have eggs? Congrats on the impressive EB of { EarningsBonus}%!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!",
                        $"Choo Choo!All aboard the <:Egg_soul_SE:724341890794913964> train with our new { role.Name }. { discordUser.Mention} is driving the train with an EB of { EarningsBonus}%, jump on now!",
                        $"Congrats { discordUser.Mention}, you are a { role.Name} now with an EB of { EarningsBonus}%! How eggciting!",
                        });
                        break;
                    case "Zettafarmer":
                        messages.AddRange(new List<string> {
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. Afraid of heights, {discordUser.Mention}? I hope not, you're climbing higher and higher up the leaderboard!",
                        $"Did anyone else see that blur go by? I think it was {discordUser.Mention} on their way to LEVELING UP TO THE RANK OF {role.Name} with an EB of {EarningsBonus}%! Awesome!",
                        $"Is it just me, or does this place smell like an EB of {EarningsBonus}%? Congrats on achieving the level of {role.Name}, {discordUser.Mention}!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%! Eggstraordinary work, there’s no stopping you, {discordUser.Mention}!",
                        });
                        break;
                    case "Yottafarmer":
                        messages.AddRange(new List<string> {
                        $"What an effort! Make way for {discordUser.Mention} and their eggcellent EB of {EarningsBonus}%! You are now a {role.Name}. Very impressive!",
                        $"We have a new {role.Name} among us! Congratulations on the rank, and the mighty EB of {EarningsBonus}%, {discordUser.Mention}!",
                        $"{EarningsBonus}% !That’s a milestone right there.You obviously know what you’re doing { discordUser.Mention}. Congratulations, you are now a {role.Name}!",
                        });
                        break;
                    case "Xennafarmer":
                    case "Weccafarmer":
                        messages.AddRange(new List<string> {
                        $"Speechless. Absolutely speechless. The grind is real, {discordUser.Mention}! Congratulations on the very impressive rank of {role.Name} with the incredible EB of {EarningsBonus}%!",
                        });
                        break;
                }



                var random = new Random();
                var index = random.Next(messages.Count);

                //Attempt to find the "separate channel for rankup messages" channel, if it's been set
                var altRankupChannel = await _client.GetChannelAsync(GuildChannelType.AltRankup, guild);

                //If it can't be found, use 'General' instead
                if(altRankupChannel == null) {
                    var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
                    await generalChannel.SendMessageAsync(messages[index]);
                } else {
                    await altRankupChannel.SendMessageAsync(messages[index]);
                }
            }

            return role;
        }

        private static async Task CheckSiloResearch(SocketGuild Guild, IGuildUser DiscordUser, List<CustomBackup> backups) {
            if(Guild.Roles.Any(x => x.Name.ToLower() == "needssiloepicresearch")) {
                var needsResearch = false;

                var needSiloERRole = Guild.Roles.FirstOrDefault(x => x.Name.ToLower() == "needssiloepicresearch");
                var hasRole = DiscordUser.RoleIds.Any(x => x == needSiloERRole.Id);

                foreach(var backup in backups) {
                    var awayTime = Research.GetTotalSiloCapacity(backup);
                    var hasPermit = backup.PermitLevel > 0;
                    if(awayTime < 72 || (!hasPermit && awayTime < 120)) {
                        needsResearch = true;
                    }
                }
                if(!hasRole && needsResearch) {
                    await DiscordUser.AddRoleAsync(needSiloERRole);

                }
                if(hasRole && !needsResearch) {
                    await DiscordUser.RemoveRoleAsync(needSiloERRole);
                }

            }
        }

        public enum DiscordTimestampFormat {
            ShortTime = 1,
            LongTime = 2,
            ShortDate = 3,
            LongDate = 4,
            LongDateWShortTime = 5,
            LongDateWDayWeekShortTime = 6,
            Relative = 7
        }
        public static string TimeStamper(TimeSpan time, DiscordTimestampFormat format = DiscordTimestampFormat.Relative) {
            if(time.TotalDays > 365) {
                return "\\> Year";
            }
            return TimeStamper(DateTimeOffset.Now.AddSeconds(time.TotalSeconds), format);
        }
        public static string TimeStamper(DateTimeOffset time, DiscordTimestampFormat format = DiscordTimestampFormat.Relative) {
            var ender = format switch {
                DiscordTimestampFormat.ShortTime => "t",
                DiscordTimestampFormat.LongTime => "T",
                DiscordTimestampFormat.ShortDate => "d",
                DiscordTimestampFormat.LongDate => "D",
                DiscordTimestampFormat.LongDateWShortTime => "f",
                DiscordTimestampFormat.LongDateWDayWeekShortTime => "F",
                DiscordTimestampFormat.Relative => "R",
                _ => "R"
            };

            return ($"<t:{time.ToUnixTimeSeconds()}:{ender}>");
        }


        public static ulong ProPermitRoleID = 966017147350446121;
        public static ulong StandardPermitRoleID = 966017278078517248;
        private static async Task CheckPermitRoles(SocketGuild Guild, IGuildUser DiscordUser, DBUser dbUser) {
            if(Guild.Roles.Any(x => x.Id == ProPermitRoleID)) {
                var hasPro = DiscordUser.RoleIds.Any(x => x == ProPermitRoleID);
                var hasStandard = DiscordUser.RoleIds.Any(x => x == StandardPermitRoleID); ;

                var needsPro = dbUser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 1);
                var needsStandard = dbUser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 0);


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
        private static async Task CheckGrades(SocketGuild Guild, IGuildUser DiscordUser, DBUser dbuser, List<(Ei.Contract.Types.PlayerGrade grade, SocketRole role)> grades) {
            var neededGrades = dbuser.EggIncAccounts.Select(x => x.GetGrade());

            var neededRoles = neededGrades.Select(x => grades.First(g => g.grade == x).role).Where(x => x is not null && !DiscordUser.RoleIds.Any(y => y == x.Id)).ToList();

            var extraRoles = grades.Where(x => x.role is not null)
                .Where(g =>
                    !dbuser.EggIncAccounts.Any(a => a.GetGrade() == g.grade) &&
                    DiscordUser.RoleIds.Any(r => r == g.role.Id)
                ).Select(x => x.role).ToList();

            if(neededRoles.Count > 0) {
                GetLogger<DiscordHelpers>().LogInformation("Adding grade roles {roles} for {user}", string.Join(",", neededRoles.Select(x => x.Name)), DiscordUser.GetName());
                await DiscordUser.AddRolesAsync(neededRoles);

            }

            if(extraRoles.Count > 0) {
                GetLogger<DiscordHelpers>().LogInformation("Removing grade roles {roles} for {user}", string.Join(",", extraRoles.Select(x => x.Name)), DiscordUser.GetName());
                await DiscordUser.RemoveRolesAsync(extraRoles);
            }
        }

        private static async Task CheckHatchlingRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            if(Guild.Roles.Any(x => x.Name.ToLower().Contains("hatchling"))) {
                var needsRole = user.Registered > DateTimeOffset.Now.AddDays(-21);
                var hatchlingRole = Guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("hatchling"));
                var hasRole = DiscordUser.RoleIds.Any(x => x == hatchlingRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(hatchlingRole);

                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(hatchlingRole);
                }

            }
        }
        private static async Task CheckOudatedGameRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var gameOutdatedRole = await _client.GetRoleAsync(GuildChannelType.GameVersionOutdated, Guild);
            if(gameOutdatedRole != null) {
                var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => x.Backup.ClientVersion > 0 && x.Backup.ClientVersion < ContractsAPI.ClientVersion);
                var hasRole = DiscordUser.RoleIds.Any(x => x == gameOutdatedRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(gameOutdatedRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding outdated role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(gameOutdatedRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing outdated role for {user}", DiscordUser.GetName());
                }

            }
        }

        private static async Task CheckEnDRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var endRole = await _client.GetRoleAsync(GuildChannelType.EnDRole, Guild);
            if(endRole is not null) {
                var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && (int)x.Backup.MaxEggReached >= 19 && x.Backup.MaxFarmSizeReached is not null && x.Backup.MaxFarmSizeReached.ContainsKey(Ei.Egg.Enlightenment)).Any(b => b.Backup.MaxFarmSizeReached[Ei.Egg.Enlightenment] >= 10000000000);
                var hasRole = DiscordUser.RoleIds.Any(x => x == endRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(endRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding EnD role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(endRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing EnD role for {user}", DiscordUser.GetName());
                }
            }
        }
        private static async Task CheckNAHRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var nahRole = await _client.GetRoleAsync(GuildChannelType.NAHRole, Guild);
            if(nahRole is not null) {
                var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && (int)x.Backup.MaxEggReached >= 19 && x.Backup.MaxFarmSizeReached is not null && x.Backup.MaxFarmSizeReached.ContainsKey(Ei.Egg.Enlightenment)).Any(b => b.Backup.MaxFarmSizeReached[Ei.Egg.Enlightenment] >= 19845000000);
                var hasRole = DiscordUser.RoleIds.Any(x => x == nahRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(nahRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding NAH role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(nahRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing NAH role for {user}", DiscordUser.GetName());
                }
            }
        }

        private static async Task CheckASCRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var ascRole = await _client.GetRoleAsync(GuildChannelType.ASCRole, Guild);
            if(ascRole is not null) {
                var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && x.Backup.ShipsSent is not null).Any(x => MissionHelpers.HasMaxedShips(x.Backup));
                var hasRole = DiscordUser.RoleIds.Any(x => x == ascRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(ascRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding ASC role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(ascRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing ASC role for {user}", DiscordUser.GetName());
                }
            }
        }

        private static async Task CheckUnjoined(SocketGuild Guild, IGuildUser DiscordUser, LeaderboardUser luser) {
            if(luser?.RecentXrefs is null)
                return;
            var unjoinedRole = Guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
            if(unjoinedRole != null) {
                var hasUnjoined = DiscordUser.RoleIds.Any(x => x == unjoinedRole.Id);
                var needsUnjoined = luser.RecentXrefs.Count == 0 || luser.RecentXrefs.All(x => !x.Joined);

                //if(!hasUnjoined && needsUnjoined) {
                //    await DiscordUser.AddRoleAsync(unjoinedRole);
                //    GetLogger<DiscordHelpers>().LogInformation("Adding unjoined Role for {user}", DiscordUser.GetName());
                //}
                if(hasUnjoined && !needsUnjoined) {
                    await DiscordUser.RemoveRoleAsync(unjoinedRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing outdated unjoined Role for {user}", DiscordUser.GetName());
                }
            }
        }

        private static async Task CheckUserOSRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var iOSRole = await _client.GetRoleAsync(GuildChannelType.IosRole, Guild);
            var droidRole = await _client.GetRoleAsync(GuildChannelType.AndroidRole, Guild);
            if(iOSRole != null) {
                var needsIosRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => !string.IsNullOrEmpty(x.DeviceID) && x.DeviceID.Length == 36);
                var hasIosRole = DiscordUser.RoleIds.Any(x => x == iOSRole.Id);

                if(!hasIosRole && needsIosRole) {
                    await DiscordUser.AddRoleAsync(iOSRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding iOS Role for {user}", DiscordUser.GetName());
                }
                if(hasIosRole && !needsIosRole) {
                    await DiscordUser.RemoveRoleAsync(iOSRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing outdated iOS Role for {user}", DiscordUser.GetName());
                }
            }
            if(droidRole != null) {
                var needsDroidRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => !string.IsNullOrEmpty(x.DeviceID) && x.DeviceID.Length == 16);
                var hasDroidRole = DiscordUser.RoleIds.Any(x => x == droidRole.Id);

                if(!hasDroidRole && needsDroidRole) {
                    await DiscordUser.AddRoleAsync(droidRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding Droid Role for {user}", DiscordUser.GetName());
                }
                if(hasDroidRole && !needsDroidRole) {
                    await DiscordUser.RemoveRoleAsync(droidRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing outdated Droid Role for {user}", DiscordUser.GetName());
                }
            }
        }

        private static async Task CheckFreshEggsRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var freshEggRole = Guild.Roles.FirstOrDefault(x => x.Id == 761005564615983152);
            if(freshEggRole != null) {
                var needsRole = user.Registered is not null && user.Registered > DateTimeOffset.Now.AddDays(-7);
                var hasRole = DiscordUser.RoleIds.Any(x => x == freshEggRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(freshEggRole);

                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(freshEggRole);
                }

            }
        }

        private static async Task CheckActive(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user, List<LeaderboardUser> userAccounts) {
            var activeRole = await _client.GetRoleAsync(GuildChannelType.ActiveRole, Guild);
            if(activeRole != null) {
                foreach(var account in userAccounts) {
                    var recentJoin = account.RecentXrefs.Any(x => x.Joined);
                    if(recentJoin != account.Account.Active) {
                        account.Account.Active = recentJoin;
                        account.User.UpdateAccounts();
                    }
                }

                var needsRole = userAccounts.Any(x => x.Account.Active);
                var hasRole = DiscordUser.RoleIds.Any(x => x == activeRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(activeRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding active role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(activeRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing active role for {user}", DiscordUser.GetName());
                }
            }
        }

        private static async Task CheckBG(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var missingBGRole = await _client.GetRoleAsync(GuildChannelType.MissingBoardingGroupRole, Guild);
            if(missingBGRole != null) {
                var needsRole = user.EggIncAccounts.Any(y => y.Group == 0);
                var hasRole = DiscordUser.RoleIds.Any(x => x == missingBGRole.Id);

                if(!hasRole && needsRole) {
                    await DiscordUser.AddRoleAsync(missingBGRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding missingbg role for {user}", DiscordUser.GetName());
                }
                if(hasRole && !needsRole) {
                    await DiscordUser.RemoveRoleAsync(missingBGRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing missingbg for {user}", DiscordUser.GetName());
                }

            }
        }

        private static async Task<SocketRole> SetRole(SocketGuild guild, IGuildUser DiscordUser, Double EarningsBonus, DBUser dbUser, DiscordHostedService _client) {
            var currentRole = DiscordUser.RoleIds.Select(y => guild.Roles.FirstOrDefault(z => z.Id == y)).Where(x => x is not null).FirstOrDefault(x => x.Name.ToUpper().Contains("FARMER"));
            var rolename = currentRole?.Name;
            var prefix = SIPrefix.GetPrefixFromEB(EarningsBonus);
            var newRoleName = prefix.Rank;
            var newRoleNameWithSuffix = prefix.RankWithSubRank;

            var newRole = guild.Roles.FirstOrDefault(x => x.Name.Equals(newRoleNameWithSuffix, StringComparison.OrdinalIgnoreCase));
            if(newRole is null)
                newRole = guild.Roles.FirstOrDefault(x => x.Name.Equals(newRoleName, StringComparison.OrdinalIgnoreCase));
            else
                newRoleName = newRoleNameWithSuffix;


            if(!newRoleName.Equals(rolename, StringComparison.CurrentCultureIgnoreCase) && (currentRole is not null || newRole is not null)) {
                GetLogger<DiscordHelpers>().LogInformation("Updating roles from {exisitingrole} to {newrolename} ({current} -> {new})", rolename, newRoleName, currentRole?.Name, newRole?.Name);
                if(currentRole != null) {
                    await DiscordUser.RemoveRoleAsync(currentRole);
                }
                if(newRole != null) {

                    await DiscordUser.AddRoleAsync(newRole);
                }
            }


            var role = newRole;

            if(dbUser.showEB) {
                try {
                    var ebs = dbUser.EggIncAccounts.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).Select(x => x.Backup.EarningsBonus.ToEggString());
                    var ebString = $" ({string.Join(",", values: ebs)})";
                    var newName = DiscordUser.GetCleanName().Truncate(32 - ebString.Length) + ebString;
                    if(newName != DiscordUser.Nickname && DiscordUser.Guild.OwnerId != DiscordUser.Id) {
                        GetLogger<DiscordHelpers>().LogInformation("Updating {user} to {newName}", DiscordUser.Nickname, newName);
                        await DiscordUser.ModifyAsync(x => x.Nickname = newName);
                    }
                } catch(Exception) {
                    GetLogger<DiscordHelpers>().LogWarning("Unable to change name of {user}", DiscordUser.GetName());
                }
            }



            return newRole;
        }


    }
}
