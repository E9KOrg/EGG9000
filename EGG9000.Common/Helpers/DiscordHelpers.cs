using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
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
            if(dmUser is null || dmUser?.Id is null) return DMResult.DiscordError;
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
                return DMResult.DiscordError;
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

        public static FileAttachment GetFileAttachment(this SixLabors.ImageSharp.Image image, string imageName = "Image.png", string imageDescription = "An image") {
            var imageB64 = image.ToBase64String(PngFormat.Instance);
            imageB64 = imageB64.Replace("data:image/png;base64,", "");
            var discordImage = new FileAttachment(new MemoryStream(Convert.FromBase64String(imageB64)), imageName, imageDescription);
            return discordImage;
        }

        public static async Task<RestUserMessage> SendFileIfExistsAsync(this SocketTextChannel channel, FileAttachment? attachment, string text = null, bool isTTS = false, Embed embed = null,
            RequestOptions options = null, AllowedMentions allowedMentions = null,
            MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null,
            Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null) {

            if(attachment is not null && attachment.HasValue) {
                return await channel.SendFileAsync(attachment.Value, text, isTTS, embed, options, allowedMentions, messageReference, components, stickers, embeds, flags, poll);
            } else {
                return await channel.SendMessageAsync(text, isTTS, embed, options, allowedMentions, messageReference, components, stickers, embeds, flags, poll);
            }
            
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

        public static async Task<SocketRole> CheckRoles(ApplicationDbContext db, SocketGuild guild, SocketGuildUser discordUser, DBUser dbUser, DiscordHostedService _client, List<(Ei.Contract.Types.PlayerGrade, SocketRole)> grades, List<LeaderboardUser> leaderboardUsers, ILogger logger = null) {
            grades ??= await GetGradeRoles(_client, guild);
            try {

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

                var eb = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus ?? 0).FirstOrDefault()?.Backup.EarningsBonus ?? 0;

                if(role != existingRole) {
                    logger?.LogInformation("Role change for {user} in guild {guild} from {oldrole} to {newrole}, Positions {oldpos} {newpos}", discordUser.GetName(), guild.Name, existingRole?.Name ?? "None", role?.Name ?? "None", existingRole?.Position, role?.Position);
                }

                if(role != null && existingRole != null && existingRole.Name != role.Name && role.Position > existingRole.Position) {
                    var guildEntity = db.Guilds.FirstOrDefault(g => g.Id == guild.Id);
                    var newRank = RankRegistry.ForEB(eb);
                    if(guildEntity is { RankupMessagesEnabled: true } && newRank.Oom > dbUser.HighestAnnouncedOom) {
                        if(!guildEntity.RankupDisabledGroups.Contains(newRank.GroupBase)) {
                            var text = await db.PickMessageAsync(_client, guildEntity, newRank, discordUser, eb);
                            if(text != null) {
                                var response = await ChannelHelper.DetermineAndSend(_client.Gateway, guildEntity, GuildChannelType.AltRankup, new() { Text = text });
                                if(response == null) await ChannelHelper.DetermineAndSend(_client.Gateway, guildEntity, GuildChannelType.General, new() { Text = text });
                            }
                        }
                        dbUser.HighestAnnouncedOom = newRank.Oom;
                        await db.SaveChangesAsync();
                    }
                }

                return role;
            }catch(Exception e) {
                logger?.LogError(e + $" Userid: {discordUser?.Id} {discordUser?.DisplayName}","", null);
                return null;
            }
        }

        private static async Task CheckSiloResearch(SocketGuild Guild, IGuildUser DiscordUser, List<CustomBackup> backups) {
            var needSiloERRole = Guild.Roles.FirstOrDefault(x => x.Name.ToLower() == "needssiloepicresearch");
            if(needSiloERRole is null) return;

            var needsResearch = backups.Any(backup => {
                var awayTime = Research.GetTotalSiloCapacity(backup);
                var hasPermit = backup.PermitLevel > 0;
                return awayTime < 72 || (!hasPermit && awayTime < 120);
            });

            await RoleToggle.ApplyAsync(DiscordUser, needSiloERRole, needsResearch);
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
                var needsPro = dbUser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 1);
                var needsStandard = dbUser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 0);

                await RoleToggle.ApplyAsync(DiscordUser, proPermitRole, needsPro, "ProPermit role");
                await RoleToggle.ApplyAsync(DiscordUser, standardPermitRole, needsStandard, "StandardPermit role");
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
                    //Make sure user is in the server
                    if(header.Guild.GetUser(DiscordUser.Id) is null) continue;
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
            var hatchlingRole = Guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("hatchling"));
            if(hatchlingRole is null) return;
            var needsRole = user.Registered > DateTimeOffset.Now.AddDays(-21);
            await RoleToggle.ApplyAsync(DiscordUser, hatchlingRole, needsRole);
        }
        private static async Task CheckOudatedGameRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var gameOutdatedRole = await _client.GetRoleAsync(GuildChannelType.GameVersionOutdated, Guild);
            var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => x.Backup.ClientVersion > 0 && x.Backup.ClientVersion < EggIncApi.ClientVersion);
            await RoleToggle.ApplyAsync(DiscordUser, gameOutdatedRole, needsRole, "outdated role");
        }

        private static async Task CheckEnDRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var endRole = await _client.GetRoleAsync(GuildChannelType.EnDRole, Guild);
            var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && (int)x.Backup.MaxEggReached >= 19 && x.Backup.MaxFarmSizeReached is not null && x.Backup.MaxFarmSizeReached.ContainsKey(Ei.Egg.Enlightenment)).Any(b => b.Backup.MaxFarmSizeReached[Ei.Egg.Enlightenment] >= 10000000000);
            await RoleToggle.ApplyAsync(DiscordUser, endRole, needsRole, "EnD role");
        }
        private static async Task CheckNAHRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var nahRole = await _client.GetRoleAsync(GuildChannelType.NAHRole, Guild);
            var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && (int)x.Backup.MaxEggReached >= 19 && x.Backup.MaxFarmSizeReached is not null && x.Backup.MaxFarmSizeReached.ContainsKey(Ei.Egg.Enlightenment)).Any(b => b.Backup.MaxFarmSizeReached[Ei.Egg.Enlightenment] >= 20_837_250_000);
            await RoleToggle.ApplyAsync(DiscordUser, nahRole, needsRole, "NAH role");
        }

        private static async Task CheckASCRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var ascRole = await _client.GetRoleAsync(GuildChannelType.ASCRole, Guild);
            var needsRole = user.EggIncAccounts.Where(x => x.Backup is not null && x.Backup.ShipsSent is not null).Any(x => x.Backup.HasMaxedShips());
            await RoleToggle.ApplyAsync(DiscordUser, ascRole, needsRole, "ASC role");
        }

        private static async Task CheckUnjoined(SocketGuild Guild, IGuildUser DiscordUser, LeaderboardUser luser) {
            if(luser?.RecentXrefs is null)
                return;
            var unjoinedRole = Guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
            var needsUnjoined = luser.RecentXrefs.Count == 0 || luser.RecentXrefs.All(x => !x.Joined);
            await RoleToggle.ApplyAsync(DiscordUser, unjoinedRole, needsUnjoined, "unjoined Role", canAdd: false);
        }

        private static async Task CheckUserOSRole(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var iOSRole = await _client.GetRoleAsync(GuildChannelType.IosRole, Guild);
            var droidRole = await _client.GetRoleAsync(GuildChannelType.AndroidRole, Guild);

            var needsIosRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => !string.IsNullOrEmpty(x.DeviceID) && x.DeviceID.Length == 36);
            await RoleToggle.ApplyAsync(DiscordUser, iOSRole, needsIosRole, "iOS Role");

            var needsDroidRole = user.EggIncAccounts.Where(x => x.Backup is not null).Any(x => !string.IsNullOrEmpty(x.DeviceID) && x.DeviceID.Length == 16);
            await RoleToggle.ApplyAsync(DiscordUser, droidRole, needsDroidRole, "Droid Role");
        }

        private static async Task CheckFreshEggsRole(SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var freshEggRole = Guild.Roles.FirstOrDefault(x => x.Id == 761005564615983152);
            var needsRole = user.Registered is not null && user.Registered > DateTimeOffset.Now.AddDays(-7);
            await RoleToggle.ApplyAsync(DiscordUser, freshEggRole, needsRole);
        }

        private static async Task CheckActive(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, List<LeaderboardUser> userAccounts) {
            var activeRole = await _client.GetRoleAsync(GuildChannelType.ActiveRole, Guild);
            if(activeRole is null) return;

            foreach(var account in userAccounts) {
                var recentJoin = account.RecentXrefs.Any(x => x.Joined);
                if(recentJoin != account.Account.Active) {
                    account.Account.Active = recentJoin;
                    account.User.UpdateAccounts();
                }
            }

            var needsRole = userAccounts.Any(x => x.Account.Active);
            await RoleToggle.ApplyAsync(DiscordUser, activeRole, needsRole, "active role");
        }

        private static async Task CheckBG(DiscordHostedService _client, SocketGuild Guild, IGuildUser DiscordUser, DBUser user) {
            var missingBGRole = await _client.GetRoleAsync(GuildChannelType.MissingBoardingGroupRole, Guild);
            var needsRole = user.EggIncAccounts.Any(y => y.Group == 0);
            await RoleToggle.ApplyAsync(DiscordUser, missingBGRole, needsRole, "missingbg role");
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
