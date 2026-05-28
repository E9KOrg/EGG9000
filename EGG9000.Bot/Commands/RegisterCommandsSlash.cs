using Bugsnag;

using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Interactions;
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

        public static async Task _RemoveID(SocketInteraction command, ApplicationDbContext db, string eggincid, ulong userid) {
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

        public static async Task _Accept(SocketInteraction command, ApplicationDbContext db, DiscordHostedService _client, IUser targetUser) {
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
                    var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
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
                    var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
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
            await command.RespondAsyncGettingMessage($"{targetUser.Mention}, next we'll need you to register with your Egg, Inc account. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window. More detailed instructions are included in the pinned messages of this channel.\n\nWhy do we need this? The bot needs everyone's ID to be able to track Farming activity, rates, and contract preferences. It also factors this information in to create balanced co-ops for each contract. The bot only reads certain parts of the info and does not make any changes. {channelText}");

            await db.SaveChangesAsync();
        }

        public static async Task _UpdateID(SocketInteraction command, ApplicationDbContext db, string eggincid, SocketGuildUser targetUser, int accountnumber) {
            await command.DeferAsync(ephemeral: true);
            if(targetUser is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("`SocketGuildUser` instance could not be found."); });
                return;
            }
            var response = await EggIncApi.FirstContact(eggincid);
            var backup = new CustomBackup(response.Backup);
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
                var customBackup = new CustomBackup((await EggIncApi.FirstContact(account.Id))?.Backup, account?.Backup ?? null);
                if(customBackup?.Farms is not null) {
                    account.Backup = customBackup;
                }
            }
            user.UpdateAccounts();
            await db.SaveChangesAsync();

            var updatedEmbeds = (await UserStatusCommands.AccountsString(db, user, false)).Select(b => b.Build()).ToArray().Prepend(EmbedSuccess("EID Updated")).ToArray();
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = updatedEmbeds; });
        }

        public static async Task RegisterAccountAsync(IMessageChannel channel, System.Func<System.Action<MessageProperties>, Task> reply, ApplicationDbContext db, DiscordHostedService _client, IClient bugsnag, string eggincid, IUser user, ILogger logger, System.Func<Task> onComplete = null) {
            eggincid = eggincid.ToUpper();

            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == channel.Id));
            var guildObj = db.Guilds.FirstOrDefault(x => x.Id == guild.Id || x.OverflowServersJson.Contains(guild.Id.ToString()));

            var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);

            try {
                var bannedUsers = db.DBUsers.Where(x => x.Banned).ToList().SelectMany(u => u.EggIncAccounts).ToList();
                if(bannedUsers.Any(e => e.Id.ToUpper() == eggincid)) {
                    var bannedUserThread = guildObj.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.BannedUserThread);
                    if(bannedUserThread is not null) {
                        var thread = guild.GetThreadChannel(bannedUserThread.Id);
                        if(thread is not null) {
                            await thread.SendMessageAsync($"{user.Mention} attempted to register with a banned EggInc ID `{eggincid}` in <#{channel.Id}>");
                        }
                        await reply(m => { m.Content = ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` has been banned from registering with this server."); });
                    } else {
                        var staffMention = guildObj.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.CallStaffTagRole).Id.ToString() ?? "";
                        await reply(m => { m.Content = staffMention is not null ? "<@&" + staffMention + ">" : ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` has been banned from registering with this server."); });
                    }
                    return;
                }
            } catch(Exception ex) {
                bugsnag.Notify(ex);
                logger.LogError(ex, "Error checking banned users");
            }

            var existingUsers = await db.DBUsers.ToListAsync();
            if(existingUsers.Any(u => u.EggIncAccounts.Any(a => a.Id.ToUpper() == eggincid))) {
                await reply(m => { m.Content = ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` is already registered with the bot. Reach out to staff for help."); });
                return;
            }

            var firstContactResponse = await EggIncApi.FirstContact(eggincid);
            var backup = new CustomBackup(firstContactResponse.Backup);
            if(backup?.Farms == null || backup.Farms.Count == 0) {
                var id = new Regex(@"\d+").Match(eggincid).Value;
                if(eggincid.StartsWith("E1")) {
                    id = id[1..];
                }
                if(id.Length > 7) {
                    backup = await EggIncApi.GetBackupAsync(eggincid);
                }
            }

            if(backup?.Farms == null || backup.Farms.Count == 0) {
                await reply(m => {
                    m.Content = "";
                    m.Embed = EmbedError($"Possibly wrong EggInc ID ({eggincid}), it should start with the capital letters EI followed by 16 numbers.");
                });
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
                    GuildId = _client.Guilds.First(x => x.TextChannels.Any(y => y.Id == channel.Id)).Id,
                    showEB = true
                };
                db.DBUsers.Add(dbuser);
                addedUser = true;
            } else {
                if(dbuser.EggIncAccounts.Any(y => y.Id == backup.EggIncId)) {
                    await reply(m => { m.Content = ""; m.Embed = EmbedError($"You have already registered this EggInc ID with the bot."); });
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
                var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
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
            if(firstContactResponse == null) await channel.SendMessageAsync(compiledMessage);

            if(dbuser.EggIncAccounts.Count == 1) {
                var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                if(overflowRole != null) {
                    await socketGuildUser.AddRoleAsync(overflowRole);
                }
            }

            try {
                var guildContracts = await db.GuildContracts.Where(x => !x.DeletedChannel && x.GuildID == guild.Id).ToListAsync();
                foreach(var guildContract in guildContracts) {
                    var contractChannel = guild.GetTextChannel(guildContract.DiscordChannelId);
                    if(contractChannel != null) {
                        await contractChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                    }
                }
            } catch(Exception) { }

            await CleanWelcomeChannel(guild, _client, user);

            if(addedUser) {
                try {
                    var ebString = $" ({earningsBonus.ToEggString()})";
                    var newName = ((IGuildUser)user).GetCleanName().Trim().Truncate(32 - ebString.Length) + ebString;
                    try {
                        await ((IGuildUser)user).ModifyAsync(x => x.Nickname = newName);
                    } catch(HttpException) {
                        logger.LogWarning("Unable to update nickname for {user}", user.Username);
                    }
                } catch(Exception) { }
            }
            if(onComplete != null) await onComplete();
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

    }

    public class RegisterModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client, IClient bugsnag, ILogger<RegisterModule> logger) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;
        private readonly IClient _bugsnag = bugsnag;
        private readonly ILogger<RegisterModule> _logger = logger;

        [SlashCommand("moveserver", "Use to move registration to a different discord server")]
        public async Task MoveServer() {
            await Context.Interaction.DeferAsync();
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == Context.Channel.Id));

            if(dbUser == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"); });
                return;
            }

            if(dbUser.TempDisabled) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Staff have previously disabled your account. Please wait for someone to reach out to discuss this."); });
                return;
            }

            if(dbUser.GuildId == guild.Id) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Already configured for the current server, you should get your roles during the next Leaderboard update."); });
            } else {
                await Context.Interaction.RespondAsyncGettingMessage($"Please wait...");
                if(dbUser.GuildId == 428181243474214942) {
                    await ((SocketGuildUser)Context.User).AddRoleAsync(guild.Roles.FirstOrDefault(x => x.Name == "Prophet"));
                }

                if(dbUser.EggIncAccounts is null || dbUser.EggIncAccounts.Count == 0) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning("There are no Egg, Inc. accounts registered to your Discord account. Please `/register` your EID, then run this command again."); });
                    return;
                }

                dbUser.GuildId = guild.Id;
                await Db.SaveChangesAsync();

                var response = await EggIncApi.GetBackupAsync(dbUser.EggIncAccounts.First().Id);
                var earningsBonus = response.EarningsBonus;

                var guildUser = guild.Users.First(x => x.Id == Context.User.Id);
                var dbguild = await Db.Guilds.FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
                if(dbguild != null && dbguild.OverflowServers.Count > 0) {
                    var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                    if(overflowRole != null) {
                        await guildUser.AddRoleAsync(overflowRole);
                    }
                }

                var role = await DiscordHelpers.CheckRoles(Db, guild, guildUser, dbUser, _client, null, []);

                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel.Id == Context.Channel.Id) {
                    await Context.Interaction.DeleteOriginalResponseAsync();
                    var text = $"Welcome {Context.User.Mention}, you have been moved to this server. You have the rank of {role?.Name} with an EB of {earningsBonus.ToEggString()}";
                    await ChannelHelper.DetermineAndSend(_client.Gateway, dbguild, GuildChannelType.General, new() { Text = text });
                    await RegisterCommandsSlash.CleanWelcomeChannel(guild, _client, Context.User);
                } else {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("Registration has been moved"); });
                }
            }
        }

        [SlashCommand("accept", "Accept the rules of this discord server")]
        public async Task Accept() {
            await RegisterCommandsSlash._Accept(Context.Interaction, Db, _client, Context.User);
        }

        [SlashCommand("updateid", "Update your EggIncID if it has changed")]
        [EnabledInDm(true)]
        public async Task UpdateID([Summary("eggincid", "EggIncID starting with EI")] string eggincid, [Summary("accountnumber", "Account Number (if you have more than one)")] int accountnumber = 0) {
            await RegisterCommandsSlash._UpdateID(Context.Interaction, Db, eggincid, await Context.Channel.GetUserAsync(Context.User.Id) as SocketGuildUser, accountnumber);
        }

        [SlashCommand("register", "Register your EggInc account with the bot")]
        public async Task Register([Summary("eggincid", "EggIncID which begins with EI followed by 16 numbers")] string eggincid) {
            await Context.Interaction.DeferAsync();
            await RegisterCommandsSlash.RegisterAccountAsync(Context.Channel, mut => Context.Interaction.ModifyOriginalResponseAsync(mut), Db, _client, _bugsnag, eggincid, Context.User, _logger, onComplete: async () => { try { await Context.Interaction.DeleteOriginalResponseAsync(); } catch(System.Exception) { } });
        }
    }

    public partial class AdminModule {
        [Discord.Interactions.SlashCommand("removeid", "Removed registered EggInc ID from a user's account")]
        public Task RemoveID([Discord.Interactions.Summary("eggincid")] string eggincid, [Discord.Interactions.Summary("targetuser")] SocketUser targetUser) {
            return RegisterCommandsSlash._RemoveID(Context.Interaction, Db, eggincid, targetUser.Id);
        }

        [Discord.Interactions.SlashCommand("accept", "Accept the rules of this discord server")]
        public async Task Accept([Discord.Interactions.Summary("targetuser")] SocketGuildUser targetUser) {
            await RegisterCommandsSlash._Accept(Context.Interaction, Db, client, targetUser);
        }

        [Discord.Interactions.SlashCommand("updateid", "EggIncID someones ID")]
        public async Task UpdateID([Discord.Interactions.Summary("eggincid", "EggIncID starting with EI")] string eggincid, [Discord.Interactions.Summary("targetuser")] SocketGuildUser targetUser, [Discord.Interactions.Summary("accountnumber", "Account Number (if you have more than one)")] int accountnumber = 0) {
            await RegisterCommandsSlash._UpdateID(Context.Interaction, Db, eggincid, targetUser, accountnumber);
        }

        [Discord.Interactions.SlashCommand("register", "Register your EggInc account with the bot")]
        public async Task Register([Discord.Interactions.Summary("eggincid", "EggIncID which begins with EI followed by 16 numbers")] string eggincid, [Discord.Interactions.Summary("user")] SocketGuildUser user) {
            await Context.Interaction.DeferAsync();
            await RegisterCommandsSlash.RegisterAccountAsync(Context.Channel, mut => Context.Interaction.ModifyOriginalResponseAsync(mut), Db, client, bugsnag, eggincid, user, _logger, onComplete: async () => { try { await Context.Interaction.DeleteOriginalResponseAsync(); } catch(System.Exception) { } });
        }

        [Discord.Interactions.SlashCommand("clean", "Removes any unpinned messages from the channel")]
        public async Task Clean() {
            var command = Context.Interaction;
            await command.RespondAsyncGettingMessage("Cleaning...");
            var channel = (SocketTextChannel)command.Channel;
            if(channel.Name.ToLower().Contains("welcome")) {
                var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
                await guild.PruneUsersAsync(10);

                var welcomeChannel = await client.GetChannelAsync(GuildChannelType.Welcome, guild);
                var welcomeMessages = await welcomeChannel.GetMessagesAsync(500).FlattenAsync();
                await welcomeChannel.DeleteMessagesBatchAsync(welcomeMessages);

                await command.DeleteResponseFix();
            } else {
                var messages = await command.Channel.GetMessagesAsync(500).FlattenAsync();
                messages = messages.Where(x => !x.IsPinned);
                await channel.DeleteMessagesBatchAsync(messages);
                await command.DeleteResponseFix();
            }
        }
    }
}
