using Discord;
using Discord.Net;
using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;

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

        public enum DMResult {
            Success = 0,
            CannotSendToUser = 1,
            DiscordError = 2,
        };

        public static async Task<DMResult> BoolSendDm(IUser dmUser, string message, ApplicationDbContext db) {
            if(dmUser is null || dmUser?.Id is null) return DMResult.CannotSendToUser;
            DBUser dbUser = null;
            var result = DMResult.Success;
            try {
                dbUser = await db.DBUsers.FirstOrDefaultAsync(u => u.DiscordId == dmUser.Id);
                var dmChannel = await dmUser.CreateDMChannelAsync();
                if(dmChannel is null) return DMResult.DiscordError;
                await dmChannel.SendMessageAsync(message);
            } catch(HttpException ex) {
                result = ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser ? DMResult.CannotSendToUser : DMResult.DiscordError;
            } catch (Exception) {
                return DMResult.CannotSendToUser;
            }
            if(dbUser is not null && dbUser.UpdateDMStatus(result)) await db.SaveChangesAsync();
            return result;
        }

        public static Task ModifyWithTimeoutAsync(this IUserMessage message, Action<MessageProperties> msgProperties, RequestOptions options = null) {
            var tokenSource2 = new CancellationTokenSource();
            var token2 = tokenSource2.Token;
            options ??= new RequestOptions();
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
            return [
                (Ei.Contract.Types.PlayerGrade.GradeAaa, await _client.GetRoleAsync(GuildChannelType.GradeAAA, guild)),
                (Ei.Contract.Types.PlayerGrade.GradeAa, await _client.GetRoleAsync(GuildChannelType.GradeAA, guild)),
                (Ei.Contract.Types.PlayerGrade.GradeA, await _client.GetRoleAsync(GuildChannelType.GradeA, guild)),
                (Ei.Contract.Types.PlayerGrade.GradeB, await _client.GetRoleAsync(GuildChannelType.GradeB, guild)),
                (Ei.Contract.Types.PlayerGrade.GradeC, await _client.GetRoleAsync(GuildChannelType.GradeC, guild)),
                (Ei.Contract.Types.PlayerGrade.GradeUnset, null),
            ];
        }

        public static async Task<SocketRole> CheckRoles(ApplicationDbContext db, SocketGuild guild, SocketGuildUser discordUser, DBUser dbUser, DiscordHostedService _client, List<(Ei.Contract.Types.PlayerGrade, SocketRole)> grades, List<LeaderboardUser> leaderboardUsers) {
            grades ??= await GetGradeRoles(_client, guild);

            var higherEB = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus ?? 0).FirstOrDefault();
            if(higherEB.Backup.EggsOfProphecy > 1000) {
                dbUser.showEB = false;
            }

            var registeredRole = discordUser.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            var guildRegisteredRole = guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            if(registeredRole == null && guildRegisteredRole is not null) {
                await discordUser.AddRoleAsync(guildRegisteredRole);
            }

            var existingRole = discordUser.Roles.FirstOrDefault(x => x.Name.ToUpper().Contains("FARMER"));

            var role = await SetRole(guild, discordUser, higherEB.Backup.EarningsBonus, dbUser);

            await CheckSiloResearch(guild, discordUser, dbUser.EggIncAccounts.Select(y => y.Backup).ToList());
            await CheckHatchlingRole(guild, discordUser, dbUser);
            await CheckFreshEggsRole(guild, discordUser, dbUser);
            await CheckBG(_client, guild, discordUser, dbUser);
            await CheckPermitRoles(_client, guild, discordUser, dbUser);
            await CheckGrades(db, _client, discordUser, dbUser, grades);
            await CheckOudatedGameRole(_client, guild, discordUser, dbUser);
            await CheckUserOSRole(_client, guild, discordUser, dbUser);
            await CheckUnjoined(guild, discordUser, leaderboardUsers.FirstOrDefault(x => x.User.Id == dbUser.Id));
            await CheckEnDRole(_client, guild, discordUser, dbUser);
            await CheckNAHRole(_client, guild, discordUser, dbUser);
            await CheckASCRole(_client, guild, discordUser, dbUser);

            if(leaderboardUsers.Count > 0) {
                await CheckActive(_client, guild, discordUser, leaderboardUsers);
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
                    $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!"
                };

                switch(role.Name.Split(" ").First()) {
                    case "Farmer":
                        messages.AddRange([
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Eggstraordinary work!",
                        ]);
                        break;
                    case "Kilofarmer":
                        messages.AddRange([
                        $"Wow, {discordUser.Mention}! A {role.Name} already? Your wonders never cease to amaze me! Congrats on the new rank and EB of {EarningsBonus}%!.",
                        ]);
                        break;
                    case "Megafarmer":
                        messages.AddRange([
                        $"Now you are at least hundreds of millions times stronger than you were since your first chicken. Mega effort to become a {role.Name} with and EB of {EarningsBonus}%! Congratulations on the new rank, {discordUser.Mention}!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",
                        ]);
                        break;
                    case "Gigafarmer":
                        messages.AddRange([
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Gigafarmer, sweet! Your numbers are increasing along with your eggsperience!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} You made it this far. Looking forward to your next level-up!",
                        ]);
                        break;
                    case "Terafarmer":
                        messages.AddRange([
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Keep going, next up: Petafarmer!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! Chickens won't hatch themselves, get back to farming!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Remember that next <:Egg_of_Prophecy_PE:669981330477547580>increases your EB even more than the last one. Go get it!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Challenge is to never stop prestiging, keep it up!",
                        $"Choo Choo! All aboard the <:Egg_soul_SE:724341890794913964> train with our new {role.Name}. {discordUser.Mention} is driving the train with an EB of {EarningsBonus}%, jump on now!",
                        ]);
                        break;
                    case "Petafarmer":
                        messages.AddRange([
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Prestiging is like a reversed limbo, how high can you go?",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! More chickens, more eggs, higher earnings means more <:Egg_soul_SE:724341890794913964>. Keep hatching!",
                        $"With great EB comes great responsibility. Congrats on hitting an EB of {EarningsBonus}%, {discordUser.Mention}! This means you are officially a {role.Name}. Now get back out there - those wormholes aren’t going to dampen themselves!",
                                                        ]);
                        break;
                    case "Exafarmer":
                        messages.AddRange([
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%, {discordUser.Mention}! You really like eggs, eh? Eggciting hobby, isnt it?",
                        $"You’ve finally reached the rank of { role.Name}, { discordUser.Mention}! Wow. It seems like just yesterday you were running your first chickens. Celebrate!",
                        $"{ role.Name}: achieved. What’s next, { discordUser.Mention}? This calls for omelets. Anyone have eggs? Congrats on the impressive EB of { EarningsBonus}%!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. {discordUser.Mention} Afraid of heights? Hope not, you're climbing higher and higher up the leaderboard!",
                        $"Choo Choo! All aboard the <:Egg_soul_SE:724341890794913964> train with our new { role.Name }. { discordUser.Mention} is driving the train with an EB of { EarningsBonus}%, jump on now!",
                        $"Congrats { discordUser.Mention}, you are a { role.Name} now with an EB of { EarningsBonus}%! How eggciting!",
                        ]);
                        break;
                    case "Zettafarmer":
                        messages.AddRange([
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%. Afraid of heights, {discordUser.Mention}? I hope not, you're climbing higher and higher up the leaderboard!",
                        $"Did anyone else see that blur go by? I think it was {discordUser.Mention} on their way to LEVELING UP TO THE RANK OF {role.Name} with an EB of {EarningsBonus}%! Awesome!",
                        $"Is it just me, or does this place smell like an EB of {EarningsBonus}%? Congrats on achieving the level of {role.Name}, {discordUser.Mention}!",
                        $"Congrats on the new rank of {role.Name} with an EB of {EarningsBonus}%! Eggstraordinary work, there’s no stopping you, {discordUser.Mention}!",
                        $"Eggciting times, {discordUser.Mention}! Your hard work has paid off, and you've reached the {role.Name} rank with an EB of {EarningsBonus}%. Keep the momentum going!",
                        $"Major kudos, {discordUser.Mention}! The farm is buzzing with excitement as you secure the {role.Name} rank with an impressive EB of {EarningsBonus}%. Well done!",
                        ]);
                        break;
                    case "Yottafarmer":
                        messages.AddRange([
                        $"What an effort! Make way for {discordUser.Mention} and their eggcellent EB of {EarningsBonus}%! You are now a {role.Name}. Very impressive!",
                        $"We have a new {role.Name} among us! Congratulations on the rank, and the mighty EB of {EarningsBonus}%, {discordUser.Mention}!",
                        $"{EarningsBonus}%! That’s a milestone right there.You obviously know what you’re doing { discordUser.Mention}. Congratulations, you are now a {role.Name}!",
                         $"Fantastic news, {discordUser.Mention}! You've achieved the impressive rank of {role.Name} with an EB of {EarningsBonus}%. Your dedication is truly eggstraordinary!",
                        $"Bravo, {discordUser.Mention}! You've cracked it! The new rank of {role.Name} is now yours, and with an EB of {EarningsBonus}%, the sky's the limit!",
                        ]);
                        break;
                    case "Xennafarmer":
                        messages.AddRange([
                        $"Hold on tight, everyone! {discordUser.Mention} just soared into the prestigious rank of {role.Name} with an astonishing EB of {EarningsBonus}%! Unbelievable dedication and hard work!",
                        $"Lights, camera, action! {discordUser.Mention} takes the spotlight as they achieve the remarkable rank of {role.Name} with an extraordinary EB of {EarningsBonus}%. Your commitment is truly commendable!",
                        $"Breaking news: {discordUser.Mention} has reached the elite status of {role.Name} with an exceptional EB of {EarningsBonus}%. The farm has never seen such excellence before. Congratulations!",
                        $"Way to go, {discordUser.Mention}! You've leveled up to {role.Name} with an EB of {EarningsBonus}%. The farm has never looked better under your management!",
                        $"Incredible news, {discordUser.Mention}! You're now rocking the {role.Name} rank with an impressive EB of {EarningsBonus}%. Your commitment is truly commendable!",
                        ]);
                        break;
                    case "Weccafarmer":
                        messages.AddRange([
                        $"Speechless. Absolutely speechless. The grind is real, {discordUser.Mention}! Congratulations on the very impressive rank of {role.Name} with the incredible EB of {EarningsBonus}%!",
                        $"Alert! {discordUser.Mention} has just ascended to the remarkable rank of {role.Name} with a jaw-dropping EB of {EarningsBonus}%! The farm is buzzing with your incredible achievement!",
                        $"The farm is shaking with excitement as {discordUser.Mention} conquers the challenging path to become a {role.Name} with an impressive EB of {EarningsBonus}%. Your hard work is truly paying off!",
                        $"Outstanding achievement, {discordUser.Mention}! Your dedication and effort have propelled you to the prestigious rank of {role.Name} with an EB of {EarningsBonus}%. Keep shining!",
                        $"Cheers to you, {discordUser.Mention}! Your farm is flourishing, and so is your rank. Congratulations on achieving {role.Name} with an EB of {EarningsBonus}%. Well deserved!",
                        ]);
                        break;
                    case "Vendafarmer":
                        messages = [
                        $"Step aside, everyone! {discordUser.Mention} has officially reached the top tier as a {role.Name} with an absolutely outstanding EB of {EarningsBonus}%. Your dedication is an inspiration to us all!",
                        ];
                        break;
                }


                var random = new Random();
                var index = random.Next(messages.Count);

                //Attempt to find the "separate channel for rankup messages" channel, if it's been set
                var response = await ChannelHelper.DetermineAndSend(db, _client, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.AltRankup, new() { Text = messages[index] });
                //If it can't be found, use 'General' instead
                if(response == null) await ChannelHelper.DetermineAndSend(db, _client, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.General, new() { Text = messages[index] });
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

        private static async Task CheckPermitRoles(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser dbUser) {
            var standardPermitRole = await _client.GetRoleAsync(GuildChannelType.StandardPermitRole, Guild);
            var proPermitRole = await _client.GetRoleAsync(GuildChannelType.ProPermitRole, Guild);

            if(standardPermitRole is not null && proPermitRole is not null) {
                var hasPro = DiscordUser.RoleIds.Any(x => x == proPermitRole.Id);
                var hasStandard = DiscordUser.RoleIds.Any(x => x == standardPermitRole.Id); ;

                var needsPro = dbUser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 1);
                var needsStandard = dbUser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 0);


                if(!hasPro && needsPro) {
                    await DiscordUser.AddRoleAsync(proPermitRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding ProPermit role for {user}", DiscordUser.GetName());
                }
                if(hasPro && !needsPro) {
                    await DiscordUser.RemoveRoleAsync(proPermitRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing ProPermit role for {user}", DiscordUser.GetName());
                }
                if(!hasStandard && needsStandard) {
                    await DiscordUser.AddRoleAsync(standardPermitRole);
                    GetLogger<DiscordHelpers>().LogInformation("Adding StandardPermit role for {user}", DiscordUser.GetName());

                }
                if(hasStandard && !needsStandard) {
                    await DiscordUser.RemoveRoleAsync(standardPermitRole);
                    GetLogger<DiscordHelpers>().LogInformation("Removing StandardPermit role for {user}", DiscordUser.GetName());
                }

            }
        }
        private static async Task CheckGrades(ApplicationDbContext db, DiscordHostedService _client, IGuildUser DiscordUser, DBUser dbuser, List<(Ei.Contract.Types.PlayerGrade grade, SocketRole role)> grades) {
            var neededGrades = dbuser.EggIncAccounts.Select(x => x.GetGrade());

            var neededRoles = neededGrades.Select(x => grades.First(g => g.grade == x).role).Where(x => x is not null && !DiscordUser.RoleIds.Any(y => y == x.Id)).ToList();

            var extraGrades = grades.Where(x => x.role is not null)
                .Where(g =>
                    !dbuser.EggIncAccounts.Any(a => a.GetGrade() == g.grade) &&
                    DiscordUser.RoleIds.Any(r => r == g.role.Id)
                ).ToList();
            var extraRoles = extraGrades.Select(x => x.role).ToList();

            if(neededRoles.Count > 0) {
                GetLogger<DiscordHelpers>().LogInformation("Adding grade roles {roles} for {user}", string.Join(",", neededRoles.Select(x => x.Name)), DiscordUser.GetName());
                await DiscordUser.AddRolesAsync(neededRoles);
            }

            if(extraRoles.Count > 0) {
                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.UserId == dbuser.Id && !x.Coop.ThreadArchived && !x.Coop.DeletedChannel).ToListAsync();

                //Handle the case where users rank up, and need to still see existing coops
                var lostXrefs = xrefs.Where(x => extraGrades.Any(eg => eg.grade == (Ei.Contract.Types.PlayerGrade)x.Coop.League));
                foreach(var lostXref in lostXrefs) {
                    var guild = _client.Guilds.FirstOrDefault(x => x.Channels.Any(c => c.Id == lostXref.Coop.ThreadParentChannel));
                    if(guild is null) continue;
                    var header = guild.GetTextChannel(lostXref.Coop.ThreadParentChannel);
                    if(header is null) continue;
                    try {
                        await header.AddPermissionOverwriteAsync(DiscordUser,
                            new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Deny, sendMessagesInThreads: PermValue.Allow)
                        );
                    } catch(HttpException) { continue; }
                }
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
                var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && x.Backup.ShipsSent is not null).Any(x => x.Backup.HasMaxedShips());
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

        private static async Task CheckActive(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, List<LeaderboardUser> userAccounts) {
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

        private static async Task<SocketRole> SetRole(SocketGuild guild, IGuildUser DiscordUser, double EarningsBonus, DBUser dbUser) {
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
