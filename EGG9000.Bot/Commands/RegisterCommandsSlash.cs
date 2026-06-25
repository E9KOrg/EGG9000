using Bugsnag;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using MassTransit.Initializers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public static class RegisterCommandsSlash {

        [SlashCommand(Description = "Use to move registration to a different discord server")]
        public static async Task MoveServer(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));

            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            if(dbUser.TempDisabled) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Staff have previously disabled your account. Please wait for someone to reach out to discuss this."); });
                return;
            }

            if(dbUser.GuildId == guild.Id) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Already configured for the current server, you should get your roles during the next Leaderboard update."); });
            } else {
                await command.RespondAsync($"Please wait...");
                if(dbUser.GuildId == 428181243474214942) {
                    await ((SocketGuildUser)command.User).AddRoleAsync(guild.Roles.FirstOrDefault(x => x.Name == "Prophet"));
                }

                if(dbUser.EggIncAccounts is null || dbUser.EggIncAccounts.Count == 0) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning("There are no Egg, Inc. accounts registered to your Discord account. Please `/register` your EID, then run this command again."); });
                    return;
                }

                dbUser.GuildId = guild.Id;
                await db.SaveChangesAsync();

                var (response, _) = await EggIncApi.GetBackupAsync(dbUser.EggIncAccounts.First().Id, await db.CachedEiContractsAsync());
                var earningsBonus = response.EarningsBonus;

                var guildUser = guild.Users.First(x => x.Id == command.User.Id);
                var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
                if(dbguild != null && dbguild.OverflowServers.Count > 0) {
                    var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == KnownRoles.Overflow);
                    if(overflowRole != null) {
                        await guildUser.AddRoleAsync(overflowRole);
                    }
                }

                var role = await DiscordHelpers.CheckRoles(db, guild, guildUser, dbUser, _client, null, []);

                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel.Id == command.Channel.Id) {
                    await command.DeleteOriginalResponseAsync();
                    var text = $"Welcome {command.User.Mention}, you have been moved to this server. You have the rank of {role?.Name} with an EB of {earningsBonus.ToEggString()}";
                    await ChannelHelper.DetermineAndSend(_client.Gateway, dbguild, GuildChannelType.General, new() { Text = text });
                    await CleanWelcomeChannel(guild, _client, command.User);
                } else {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("Registration has been moved"); });
                }
            }
        }

        [SlashCommand(Description = "Removed registered EggInc ID from a user's account", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, [SlashParam] string eggincid, [SlashParam] SocketUser targetUser) {
            return _RemoveID(command, db, eggincid, targetUser.Id);
        }

        public static async Task _RemoveID(FauxCommand command, ApplicationDbContext db, string eggincid, ulong userid) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == userid);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{userid}>"); });
                return;
            } else if(dbUser.EggIncAccounts.Any(x => x.Id == eggincid)) {
                dbUser.RemoveID(eggincid);
            } else {
                Embed[] embedArrayErr = [EmbedError($"Unable to find the EggIncId `{eggincid}` registered with <@{userid}>")];
                embedArrayErr = [.. embedArrayErr, .. (await UserStatusCommands.AccountsString(db, dbUser, false)).Select(b => b.Build()).ToArray()];
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embedArrayErr; });
                return;
            }

            await db.SaveChangesAsync();

            db.Entry(dbUser).Reload();
            Embed[] embedArray = [EmbedSuccess($"ID `{eggincid}` removed from <@{userid}>"), .. (await UserStatusCommands.AccountsString(db, dbUser, false)).Select(b => b.Build()).ToArray()];
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embedArray; });
        }

        [SlashCommand(Description = "Accept the rules of this discord server")]
        public static async Task Accept(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client) {
            await _Accept(command, db, _client, command.User);
        }
        [SlashCommand(Description = "Accept the rules of this discord server", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task Accept(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser) {
            await _Accept(command, db, _client, targetUser);
        }
        public static async Task _Accept(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, IUser targetUser) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(guild == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = "Unable to find server, please run this command in a server");
                return;
            }
            if(dbUser is not null) {
                if(dbUser.TempDisabled) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"Looks like staff have previously disabled your account. Please wait for someone to reach out to discuss this.");
                    return;
                }

                if(dbUser.GuildId == command.GuildId && dbUser.EggIncAccounts.Count > 0) {
                    await DiscordHelpers.CheckRoles(db, guild, (command.User as SocketGuildUser), dbUser, _client, null, []);
                    await command.DeleteOriginalResponseAsync();
                    await ChannelHelper.DetermineAndSend(_client.Gateway, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), GuildChannelType.General, new() { Text = $"Welcome back {targetUser.Mention}!" });
                    var activeRole = guild.Roles.FirstOrDefault(x => x.Id == KnownRoles.Active);
                    if(activeRole != null) {
                        await ((SocketGuildUser)targetUser).AddRoleAsync(activeRole);
                    }
                    await CleanWelcomeChannel(guild, _client, targetUser);
                    return;
                } else if(dbUser.GuildId == command.GuildId && dbUser.EggIncAccounts.Count == 0) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"{targetUser.Mention}, you have already accepted the rules. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window.");
                    return;
                } else if(dbUser.GuildId > 0) {
                    var moveServerCommandString = await _client.GetSlashCommandStringAsync(guild, "MoveServer");
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"{targetUser.Mention}, looks like you are registered with another server, if you would like to move to this server use the ${moveServerCommandString} command.");
                    return;
                } else {
                    await ChannelHelper.DetermineAndSend(_client.Gateway, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), GuildChannelType.General, new() { Text = $"Welcome back {targetUser.Mention}!" });
                    var activeRole = guild.Roles.FirstOrDefault(x => x.Id == KnownRoles.Active);
                    if(activeRole != null) await ((SocketGuildUser)targetUser).AddRoleAsync(activeRole);
                    await CleanWelcomeChannel(guild, _client, targetUser);
                    return;
                }
            }

            dbUser = new DBUser {
                DiscordId = targetUser.Id,
                DiscordUsername = targetUser.Username,
                AcceptedRules = true,
                CreateOn = DateTimeOffset.UtcNow,
                GuildId = guild.Id,
                showEB = true
            };
            db.DBUsers.Add(dbUser);

            var dbGuild = db.Guilds.FirstOrDefault(g => g.Id == guild.Id);
            var talkChannel = ChannelHelper.DetermineChannelType(dbGuild, guild, GuildChannelType.TalkToStaff);
            var talkChannelMention = talkChannel != null ? (talkChannel.GetType() == typeof(SocketTextChannel) ? ((SocketTextChannel)talkChannel).Mention
                : ((SocketThreadChannel)talkChannel).Mention) : null;
            var channelText = talkChannelMention == null ? "" : $"If you have questions about this, feel free to message us in {talkChannelMention}";
            await command.RespondAsync($"{targetUser.Mention}, next we'll need you to register with your Egg, Inc account. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window. More detailed instructions are included in the pinned messages of this channel.\n\nWhy do we need this? The bot needs everyone's ID to be able to track Farming activity, rates, and contract preferences. It also factors this information in to create balanced co-ops for each contract. The bot only reads certain parts of the info and does not make any changes. {channelText}");

            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Update your EggIncID if it has changed", AllowInDMs = true)]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, eggincid, await command.Channel.GetUserAsync(command.User.Id) as SocketGuildUser, accountnumber);
        }
        [SlashCommand(Description = "EggIncID someones ID", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam] SocketGuildUser targetUser, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, eggincid, targetUser, accountnumber);
        }
        public static async Task _UpdateID(FauxCommand command, ApplicationDbContext db, string eggincid, SocketGuildUser targetUser, int accountnumber) {
            await command.DeferAsync(ephemeral: true);
            if(targetUser is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("`SocketGuildUser` instance could not be found."); });
                return;
            }
            var response = await EggIncApi.FirstContact(eggincid);
            var backup = new CustomBackup(response.Backup, await db.CachedEiContractsAsync());
            if(backup == null || backup.Farms == null || backup.Farms.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Possibly wrong EggInc ID"); });
                return;
            }
            if(backup.EggIncId != eggincid) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Error matching ID {eggincid} - {backup.EggIncId}"); });
                return;
            }

            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);

            if(accountnumber == 0 && user.EggIncAccounts.Count > 1) {
                var count = 1;
                var accounts = string.Join("\n", user.EggIncAccounts.Select(x => $"{count++} {x.Backup?.UserName} EB: {x.Backup?.EarningsBonus.ToEggString()}"));
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User has multiple accounts, please specifiy which account `/updateid {{eggincid}} {{accountnumber}}`\n{accounts}"); });
                return;
            }

            var existingAccount = user.EggIncAccounts.Count > 1 ? user.EggIncAccounts[accountnumber - 1] : user.EggIncAccounts.First();
            var newAccount = new EggIncAccount {
                Id = backup.EggIncId,
                Group = existingAccount.Group,
                UltraGroup = existingAccount.UltraGroup,
                Guild = existingAccount.Guild,
                RedoLeggacy = existingAccount.RedoLeggacy,
                RedoLeggacySelection = existingAccount.RedoLeggacySelection,
                RedoScoreThreshold = existingAccount.RedoScoreThreshold,
                LeggacyAutoRegisterRewards = existingAccount.LeggacyAutoRegisterRewards,
                AutoRegisterRewards = existingAccount.AutoRegisterRewards,
                PingForNCUltra = existingAccount.PingForNCUltra,
                DoTwoToThreeContracts = existingAccount.DoTwoToThreeContracts,
            };

            if(user.EggIncAccounts.Count > 1) user.EggIncAccounts[accountnumber - 1] = newAccount;
            else user.EggIncAccounts = [newAccount];

            foreach(var account in user.EggIncAccounts) {
                var customBackup = new CustomBackup((await EggIncApi.FirstContact(account.Id))?.Backup, await db.CachedEiContractsAsync(), account?.Backup ?? null);
                if(customBackup?.Farms is not null) {
                    account.Backup = customBackup;
                }
            }
            user.UpdateAccounts();
            await db.SaveChangesAsync();

            var updatedEmbeds = (await UserStatusCommands.AccountsString(db, user, false)).Select(b => b.Build()).ToArray().Prepend(EmbedSuccess("EID Updated")).ToArray();
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = updatedEmbeds; });
        }

        [SlashCommand(Description = "Register your EggInc account with the bot", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, IClient bugsnag, ILogger logger, [SlashParam(Description = "EggIncID which begins with EI followed by 16 numbers")] string eggincid, [SlashParam] SocketGuildUser user) {
            return _Register(command, db, _client, bugsnag, eggincid, user, logger, isStaff: true);
        }
        [SlashCommand(Description = "Register your EggInc account with the bot")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, IClient bugsnag, ILogger logger, [SlashParam(Description = "EggIncID which begins with EI followed by 16 numbers")] string eggincid) {
            return _Register(command, db, _client, bugsnag, eggincid, command.User, logger);
        }
        public static async Task _Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, IClient bugsnag, string eggincid, IUser user, ILogger logger, bool isStaff = false) {
            eggincid = eggincid.ToUpper();

            if(!Regex.IsMatch(eggincid, @"^EI\d{16}$")) {
                await command.RespondAsync(embed: EmbedError("Your EggInc ID must start with `EI` followed by 16 numbers. To find your ID, go to Settings → Privacy & Data → Copy button next to the EID near the bottom in the Egg Inc app."), ephemeral: true);
                return;
            }

            await command.DeferAsync(ephemeral: !isStaff);

            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var guildObj = db.Guilds.FirstOrDefault(x => x.Id == guild.Id || x.OverflowServersJson.Contains(guild.Id.ToString()));

            var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);

            try {
                var bannedUsers = db.DBUsers.Where(x => x.Banned).ToList().SelectMany(u => u.EggIncAccounts).ToList();
                if(bannedUsers.Any(e => e.Id.ToUpper() == eggincid)) {
                    var bannedUserThread = guildObj.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.BannedUserThread);
                    if(bannedUserThread is not null) {
                        var thread = guild.GetThreadChannel(bannedUserThread.Id);
                        if(thread is not null) {
                            await thread.SendMessageAsync($"{user.Mention} attempted to register with a banned EggInc ID `{eggincid}` in <#{command.Channel.Id}>");
                        }
                        await command.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` has been banned from registering with this server."); });
                    } else {
                        var staffMention = guildObj.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.CallStaffTagRole).Id.ToString() ?? "";
                        await command.ModifyOriginalResponseAsync(m => { m.Content = staffMention is not null ? "<@&" + staffMention + ">" : ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` has been banned from registering with this server."); });
                    }
                    return;
                }
            } catch(Exception ex) {
                bugsnag.Notify(ex);
                logger.LogError(ex, "Error checking banned users");
            }

            var existingAccountColumns = await db.DBUsers
                .Select(u => new { u._eggIncIds, u._contractRegistrationByte })
                .ToListAsync();
            if(existingAccountColumns.Any(u => DBUser.FromAccountColumns(u._eggIncIds, u._contractRegistrationByte).EggIncAccounts.Any(a => a.Id.ToUpper() == eggincid))) {
                await command.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` is already registered with the bot. Reach out to staff for help."); });
                if (!isStaff) await NotifyRegistrationIssueChannel($"{user.Mention} tried to register EggInc ID `{eggincid}` in <#{command.Channel.Id}>, but it's already registered to another user.");
                return;
            }

            var firstContactResponse = await EggIncApi.FirstContact(eggincid);
            var backup = new CustomBackup(firstContactResponse.Backup, await db.CachedEiContractsAsync());
            if(backup?.Farms == null || backup.Farms.Count == 0) {
                var id = new Regex(@"\d+").Match(eggincid).Value;
                if(eggincid.StartsWith("E1")) {
                    id = id[1..];
                }
                if(id.Length > 7) {
                    backup = (await EggIncApi.GetBackupAsync(eggincid, await db.CachedEiContractsAsync())).Value;
                }
            }

            if(backup?.Farms == null || backup.Farms.Count == 0) {
                await command.ModifyOriginalResponseAsync(m => {
                    m.Content = "";
                    m.Embed = EmbedError($"Possibly wrong EggInc ID ({eggincid}), it should start with the capital letters EI followed by 16 numbers.");
                });
                if(!isStaff) await NotifyRegistrationIssueChannel($"{user.Mention} tried to register EggInc ID `{eggincid}` in <#{command.Channel.Id}>, but no valid backup was returned. Could be an invalid ID or API issue.");
                return;
            }

            var addedUser = false;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                dbuser = new DBUser {
                    DiscordId = user.Id,
                    DiscordUsername = user.Username,
                    EggIncAccounts = [new EggIncAccount { Id = backup.EggIncId, Backup = backup, Group = 1 }],
                    CreateOn = DateTimeOffset.UtcNow,
                    GuildId = _client.Guilds.First(x => x.TextChannels.Any(y => y.Id == command.Channel.Id)).Id,
                    showEB = true
                };
                db.DBUsers.Add(dbuser);
                addedUser = true;
            } else {
                if(dbuser.EggIncAccounts.Any(y => y.Id == backup.EggIncId)) {
                    await command.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = EmbedError($"You have already registered this EggInc ID with the bot."); });
                    if (!isStaff) await NotifyRegistrationIssueChannel($"{user.Mention} tried to register EggInc ID `{backup.EggIncId}` in <#{command.Channel.Id}>, but it's already registered to the same user.");
                    return;
                }
                if(dbuser.EggIncAccounts.Count == 0) {
                    addedUser = true;
                }
                dbuser.EggIncAccounts.Add(new EggIncAccount { Id = backup.EggIncId, Backup = backup, Group = 1 });
                dbuser.UpdateAccounts();
            }

            await db.SaveChangesAsync();

            IGuildUser socketGuildUser = user as SocketGuildUser
                ?? guild.Users.FirstOrDefault(x => x.Id == user.Id)
                ?? (IGuildUser)await _client.Rest.GetGuildUserAsync(guild.Id, user.Id);

            if(!dbuser.Registered.HasValue) {
                dbuser.Registered = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == KnownRoles.Unjoined);
                if(unjoinedRole is not null) {
                    await socketGuildUser.AddRoleAsync(unjoinedRole);
                }
            }

            var earningsBonus = dbuser.EggIncAccounts.Max(x => x.Backup.EarningsBonus);

            var registeredRole = guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            if(registeredRole is not null && !socketGuildUser.RoleIds.Any(x => x == registeredRole.Id)) {
                await socketGuildUser.AddRoleAsync(registeredRole);
            }

            if(dbuser.GuildId != guild.Id) {
                dbuser.GuildId = guild.Id;
                await db.SaveChangesAsync();
            }
            var role = await DiscordHelpers.CheckRoles(db, guild, (SocketGuildUser)socketGuildUser, dbuser, _client, null, []);

            var roleText = "";
            if(dbuser.EggIncAccounts.Count > 1) {
                roleText = $"Your new account has been added with an EB of {backup.EarningsBonus.ToEggString()}";
            } else if(role != null) {
                roleText = $"You have been assigned the rank of {role?.Name} thanks to your EB of {earningsBonus.ToEggString()}";
            }

            var faqChannel = ChannelHelper.DetermineChannelType(db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.FaqChannel);
            var faqMention = faqChannel != null ? (faqChannel.GetType() == typeof(SocketTextChannel) ? ((SocketTextChannel)faqChannel).Mention : ((SocketThreadChannel)faqChannel).Mention) : null;
            var faqText = (faqMention != null && dbuser.EggIncAccounts.Count == 1) ? $" When you have a chance, read over {faqMention} to get an idea on how the server and bot functions." : "";

            var compiledMessage = $"Welcome {user.Mention}! {roleText}.{faqText}";
            await ChannelHelper.DetermineAndSend(_client.Gateway, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), GuildChannelType.General, new() { Text = compiledMessage }, logger);
            if(firstContactResponse == null) await command.Channel.SendMessageAsync(compiledMessage);

            if(dbuser.EggIncAccounts.Count == 1) {
                var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == KnownRoles.Overflow);
                if(overflowRole != null) {
                    await socketGuildUser.AddRoleAsync(overflowRole);
                }
            }

            try {
                var guildContracts = await db.GuildContracts.Where(x => !x.DeletedChannel && x.GuildID == guild.Id).ToListAsync();
                foreach(var guildContract in guildContracts) {
                    var channel = guild.GetTextChannel(guildContract.DiscordChannelId);
                    if(channel != null) {
                        await channel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                    }
                }
            } catch(Exception e) {
                logger.LogWarning(e, "Failed to grant contract-channel permissions for {user}", user.Username);
            }

            var responseId = (await command.GetOriginalResponseAsync()).Id;
            await CleanWelcomeChannel(guild, _client, user, excludeId: responseId);

            if(addedUser) {
                try {
                    var ebString = $" ({earningsBonus.ToEggString()})";
                    var newName = ((IGuildUser)user).GetCleanName().Trim().Truncate(32 - ebString.Length) + ebString;
                    try {
                        await ((IGuildUser)user).ModifyAsync(x => x.Nickname = newName);
                    } catch(HttpException) {
                        logger.LogWarning("Unable to update nickname for {user}", user.Username);
                    }
                } catch(Exception e) {
                    logger.LogWarning(e, "Failed to set nickname for {user}", user.Username);
                }
            }

            await command.DeleteResponseFix();

            async Task NotifyRegistrationIssueChannel(string description) {
                if(guild is null || guildObj is null || !guildObj.HasChannel(GuildChannelType.RegisterIssues)) return;
                var staffRole = guild.Roles.FirstOrDefault(x => x.Id == (guildObj.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffTagRole)?.Id ?? 0));
                var staffTag = staffRole is null ? "" : $"<@&{staffRole.Id}>: ";
                await ChannelHelper.DetermineAndSend(_client.Gateway, guildObj, GuildChannelType.RegisterIssues, new() { Text = $"{staffTag}{description}" });
            }
        }

        public static async Task CleanWelcomeChannel(SocketGuild guild, DiscordHostedService _client, IUser socketUser, int chain = 0, ulong excludeId = ulong.MaxValue) {
            try {
                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel != null) {
                    var messages = await welcomeChannel.GetMessagesAsync().FlattenAsync();
                    var userMessages = messages.Where(x =>
                        (x.MentionedUserIds.Contains(socketUser.Id)
                        || x.Author.Id == socketUser.Id
                        || (x is IUserMessage userMsg && userMsg.InteractionMetadata?.UserId == socketUser.Id))
                        && (excludeId == ulong.MaxValue || x.Id != excludeId)
                    );
                    await welcomeChannel.DeleteMessagesBatchAsync(userMessages);
                }
            } catch(Exception) {
                if(chain < 3) {
                    await CleanWelcomeChannel(guild, _client, socketUser, chain++, excludeId);
                }
            }
        }

        [SlashCommand(Description = "Removes any unpinned messages from the channel", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task Clean(FauxCommand command, DiscordHostedService _client) {
            await command.RespondAsync("Cleaning...");
            var channel = (SocketTextChannel)command.Channel;
            if(channel.Name.ToLower().Contains("welcome")) {
                await _cleanWelcome(command, _client);
            } else {
                await _cleanUnpinned(command);
            }
        }

        private static async Task _cleanWelcome(FauxCommand command, DiscordHostedService _client) {
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            await guild.PruneUsersAsync(10);

            var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
            var messages = await welcomeChannel.GetMessagesAsync(500).FlattenAsync();
            await welcomeChannel.DeleteMessagesBatchAsync(messages);

            await command.DeleteResponseFix();
        }

        private static async Task _cleanUnpinned(FauxCommand command) {
            var messages = await command.Channel.GetMessagesAsync(500).FlattenAsync();
            messages = messages.Where(x => !x.IsPinned);
            await ((SocketTextChannel)command.Channel).DeleteMessagesBatchAsync(messages);
            await command.DeleteResponseFix();
        }
    }
}
