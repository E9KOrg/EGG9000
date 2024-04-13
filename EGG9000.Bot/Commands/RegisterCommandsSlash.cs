using Bugsnag;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
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
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace EGG9000.Bot.Commands {
    public static class RegisterCommandsSlash {


        [SlashCommand(Description = "Use to move registration to a different discord server")]
        public static async Task MoveServer(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
            } else if(dbUser.GuildId == guild.Id) {
                if(dbUser.TempDisabled) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"It looks like you have been disabled, ask staff for help."); });
                } else {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Already configured for the current server, you should get your roles during the next Leaderboard update."); });
                }
            } else {
                await command.RespondAsync($"Please wait...");
                if(dbUser.GuildId == 428181243474214942) {
                    await ((SocketGuildUser)command.User).AddRoleAsync(guild.Roles.FirstOrDefault(x => x.Name == "Prophet"));
                }
                dbUser.GuildId = guild.Id;
                await db.SaveChangesAsync();

                if(dbUser.EggIncAccounts is null || dbUser.EggIncAccounts.Count == 0) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning("There are no Egg, Inc. accounts registered to your Discord account. Please `/register` your EID."); });
                    return;
                }

                var Response = await apiLink.GetBackup(dbUser.EggIncAccounts.First().Id);
                var earningsBonus = Response.EarningsBonus;

                var guildUser = guild.Users.First(x => x.Id == command.User.Id);
                var dbguild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
                if(dbguild != null && dbguild.OverflowServers.Count > 0) {
                    var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                    if(overflowRole != null) {
                        await guildUser.AddRoleAsync(overflowRole);
                    }
                }

                var role = await DiscordHelpers.CheckRoles(db, guild, guildUser, dbUser, _client, null, []);

                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel.Id == command.Channel.Id) {
                    await command.DeleteOriginalResponseAsync();
                    var text = $"Welcome {command.User.Mention}, you have been moved to this server. You have the rank of {role?.Name} with an EB of {earningsBonus.ToEggString()}";
                    var response = await ChannelHelper.DetermineAndSend(db, _client, dbguild, guild, GuildChannelType.General, new() { Text =  text });
                    await CleanWelcomeChannel(guild, _client, command.User);
                } else {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("Registration has been moved"); });
                }
            }
        }

        [SlashCommand(Description = "Removed registered EggInc ID from a user's account", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam] string eggincid, [SlashParam] SocketUser targetUser) {
            return _RemoveID(command, db, apiLink, eggincid, targetUser.Id);
        }
        /* Moving to a staff-only command, leaving commented out in case of re-activation in future
         * [SlashCommand(Description = "Removed registered EggInc ID from your account")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam] string eggincid) {
            return _RemoveID(command, db, apiLink, eggincid, command.User.Id);
        }*/
        public static async Task _RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, string eggincid, ulong userid) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == userid);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{userid}>"); });
                return;
            } else if(dbUser.EggIncAccounts.Any(x => x.Id == eggincid)) {
                dbUser.RemoveID(eggincid);
            } else {
                Embed[] embedArrayErr = [EmbedError($"Unable to find the EggIncId `{eggincid}` registered with <@{userid}>")];
                embedArrayErr = [.. embedArrayErr, .. AccountsString(db, dbUser, apiLink, false).Result.Select(b => b.Build()).ToArray()];
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embedArrayErr; });
                return;
            }

            await db.SaveChangesAsync();
            var json = JsonConvert.SerializeObject(dbUser.EggIncAccounts, Formatting.Indented);

            Embed[] embedArray = [EmbedSuccess($"ID `{eggincid}` removed from <@{userid}>"), .. AccountsString(db, dbUser, apiLink, false).Result.Select(b => b.Build()).ToArray()];
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embedArray; });
        }

        [SlashCommand(Description = "Used to remove a user from a co-op to fix a glitch.", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task LeaveCoop(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser, CoopStatusUpdater coopStatusUpdater, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ILogger logger) {
            await command.DeferAsync();
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync( x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel"); });
                return;
            }
            var dbUser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == targetUser.Id);
            if(dbUser is null) {
                await command.RespondAsync($"Unable to locate DBUser entry for <@{targetUser.Id}>");
                return;
            }

            var xrefs = await db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == dbUser.Id && x.CoopId == coop.Id).ToListAsync();


            var contract = await db.Contracts.FirstAsync(x => x.ID == coop.ContractID);
            foreach(var xref in xrefs) {
                await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (Ei.Contract.Types.PlayerGrade)coop.League, new Coop { Name = "test" + new Random().Next(10000), ContractID = coop.ContractID }, contract.Details.LengthSeconds, xref.EggIncId, coop.AnyLeague);
            }


            await Task.Delay(2);
            var status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);

            if(status.Participants.Count < contract.MaxUsers) {
                logger.LogInformation("Successfully remove {user} from {coop}", dbUser.DiscordUsername, coop.Name);
                var guild = _client.Guilds.First(x => x.Id == coop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == coop.GuildId);
                if(coop.ThreadID != 0) {
                    var slashCommands = (await guild.GetApplicationCommandsAsync()).ToList().Where(c => c.Type == ApplicationCommandType.Slash).ToList();
                    await coopStatusUpdaterThreads.ProcessCoop(coop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, slashCommands);
                } else if(coop.DiscordChannelId != 0) {
                    await coopStatusUpdater.ProcessCoop(coop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);
                }

                await command.Channel.SendMessageAsync($"Successfully removed {targetUser.Mention} from co-op, they should be able to rejoin now.");
                await command.DeleteOriginalResponseAsync();
            } else {
                logger.LogInformation("Did not {user} from {coop}", dbUser.DiscordUsername, coop.Name);
                await command.ModifyOriginalResponseAsync($"Attempted to remove {targetUser.Mention} from co-op, please check again in a few minutes.");
            }
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
            var dbUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(guild == null) {
                await command.RespondAsync("Unable to find server, please run this command in a server");
                return;
            }
            if(dbUser is not null) {
                if(dbUser.GuildId == command.GuildId && dbUser.EggIncAccounts.Count > 0) {
                    if(dbUser.TempDisabled) {
                        await command.RespondAsync($"Looks like you are currently disabled, please ask for someone from staff to find out about getting re-enabled.");
                        return;
                    } else {
                        await DiscordHelpers.CheckRoles(db, guild, (command.User as SocketGuildUser), dbUser, _client, null, []);
                        await command.DeleteOriginalResponseAsync();
                        var response = await ChannelHelper.DetermineAndSend(db, _client, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.General, new() { Text = $"Welcome back {targetUser.Mention}!" });
                        var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
                        if(activeRole != null) {
                            await ((SocketGuildUser)targetUser).AddRoleAsync(activeRole);
                        }
                        await CleanWelcomeChannel(guild, _client, targetUser);
                        return;
                    }
                } else if(dbUser.GuildId == command.GuildId && dbUser.EggIncAccounts.Count == 0) {
                    await command.RespondAsync($"{targetUser.Mention}, you have already accepted the rules. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window.");
                    return;
                } else if(dbUser.EggIncAccounts.Count > 0 && dbUser.GuildId > 0 & !dbUser.TempDisabled) {
                    await command.RespondAsync($"{targetUser.Mention}, looks like you are registered with another server, if you would like to move to this server use the </moveserver:1095116354329268366> command");
                    return;
                } else if(dbUser.TempDisabled) {
                    await command.RespondAsync($"Looks like you are currently disabled, please wait for someone from staff to get you re-enabled.");
                    return;
                } else if(dbUser.GuildId != guild.Id) {
                    var moveservercommand = (await guild.GetApplicationCommandsAsync()).FirstOrDefault(c => c.Type == ApplicationCommandType.Slash && c.Name == "moveserver");
                    await command.RespondAsync($"{targetUser.Mention}, now run the </moveserver:{moveservercommand?.Id ?? 0}> command");
                    return;
                } else {

                    var response = await ChannelHelper.DetermineAndSend(db, _client, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.General, new() { Text = $"Welcome back {targetUser.Mention}!" });

                    var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
                    if(activeRole != null) {
                        await ((SocketGuildUser)targetUser).AddRoleAsync(activeRole);
                    }

                    await CleanWelcomeChannel(guild, _client, targetUser);
                    return;
                }
            }

            dbUser = new DBUser {
                DiscordId = targetUser.Id,
                DiscordUsername = targetUser.Username,
                AcceptedRules = true,
                CreateOn = DateTimeOffset.Now,
                GuildId = guild.Id,
                showEB = true
            };
            db.DBUsers.Add(dbUser);

            var dbGuild = db.Guilds.FirstOrDefault(g => g.Id == guild.Id);

            var talkChannel = ChannelHelper.DetermineChannelType(dbGuild, guild, GuildChannelType.TalkToStaff);
            var talkChannelMention = talkChannel != null ? (talkChannel.GetType() == typeof(SocketTextChannel) ? ((SocketTextChannel)talkChannel).Mention
                : ((SocketThreadChannel)talkChannel).Mention) : null;
            var channelText = talkChannelMention == null ? "" : $"If you have questions about this, feel free to message us in {talkChannelMention}";
            await command.RespondAsync($"{targetUser.Mention}, next we’ll need you to register with your Egg, Inc account. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window. More detailed instructions are included in the pinned messages of this channel.\n\nWhy do we need this? The bot needs everyone's ID to be able to track Farming activity, rates, and contract preferences. It also factors this information in to create balanced co-ops for each contract. The bot only reads certain parts of the info and does not make any changes. {channelText}");


            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Update your EggIncID if it has changed", AllowInDMs = true)]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, apiLink, eggincid, await command.Channel.GetUserAsync(command.User.Id) as SocketGuildUser, accountnumber);
        }
        [SlashCommand(Description = "EggIncID someones ID", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam] SocketGuildUser targetUser, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, apiLink, eggincid, targetUser, accountnumber);
        }
        public static async Task _UpdateID(FauxCommand command, ApplicationDbContext db, APILink apiLink, string eggincid, SocketGuildUser targetUser, int accountnumber) {
            await command.DeferAsync(ephemeral: true);
            if(targetUser is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("`SocketGuildUser` instance could not be found."); });
                return;
            }
            var Response = await apiLink.GetBackup(eggincid);
            if(Response == null || Response.Farms == null || Response.Farms.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Possibly wrong EggInc ID"); });
                return;
            }
            if(Response.EggIncId != eggincid) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Error matching ID {eggincid} - {Response.EggIncId}"); });
                return;
            }

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);

            if(accountnumber == 0 && user.EggIncAccounts.Count > 1) {
                var count = 1;
                var accounts = string.Join("\n", user.EggIncAccounts.Select(x => $"{count++} {x.Backup?.UserName} EB: {x.Backup?.EarningsBonus.ToEggString()}"));
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User has multiple accounts, please specifiy which account `/updateid {{eggincid}} {{accountnumber}}`\n{accounts}"); });
                return;
            }

            var existingAccount = user.EggIncAccounts.Count > 1 ? user.EggIncAccounts[accountnumber - 1] : user.EggIncAccounts.First();
            var newAccount = new EggIncAccount {
                Id = Response.EggIncId,
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
                var customBackup = new CustomBackup((await ContractsAPI.FirstContact(account.Id))?.Backup, account?.Backup ?? null); //Pass current backup to maintain username where possible
                if(customBackup?.Farms is not null) {
                    account.Backup = customBackup;
                }
            }
            user.UpdateAccounts();
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = AccountsString(db, user, apiLink, false).Result.Select(b => b.Build()).ToArray().Prepend(EmbedSuccess("EID Updated")).ToArray(); });

        }

        [SlashCommand(Description = "Register your EggInc account with the bot", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, IClient bugsnag, ILogger logger, [SlashParam(Description = "EggIncID which begins with EI followed by 16 numbers")] string eggincid, [SlashParam] SocketGuildUser user) {
            return _Register(command, db, _client, apiLink, bugsnag, eggincid, user, logger);
        }
        [SlashCommand(Description = "Register your EggInc account with the bot")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, IClient bugsnag, ILogger logger, [SlashParam(Description = "EggIncID which begins with EI followed by 16 numbers")] string eggincid) {
            return _Register(command, db, _client, apiLink, bugsnag, eggincid, command.User, logger);
        }
        public static async Task _Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, IClient bugsnag, string eggincid, IUser user, ILogger logger) {
            await command.DeferAsync();
            eggincid = eggincid.ToUpper();

            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var guildObj = db.Guilds.FirstOrDefault(x => x.Id == guild.Id || x.OverflowServersJson.Contains(guild.Id.ToString()));

            try {
                var bannedUsers = db.DBUsers.Where(x => x.Banned).ToList().SelectMany(u => u.EggIncAccounts).ToList();
                if(bannedUsers is not null) {
                    if(bannedUsers.Select(e => e.Id.ToUpper()).ToList().Contains(eggincid)) {
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
                }
            } catch(Exception ex) {
                bugsnag.Notify(ex);
                logger.LogError(ex, "Error checking banned users");
            }

            var existingUsers = await db.DBUsers.Where(x => x.GuildId == guildObj.Id && x.DiscordId != command.User.Id).ToListAsync();
            if(existingUsers.Any(u => u.EggIncAccounts.Any(a => a.Id.ToUpper() == eggincid))) {
                await command.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = EmbedError($"EggInc ID `{eggincid}` is already registered with this server. Reach out to staff if you believe this is an error."); });
                return;
            }

            var Response = await apiLink.GetBackup(eggincid);
            if(Response?.Farms == null || Response.Farms.Count == 0) {
                var id = new Regex(@"\d+").Match(eggincid).Value;
                if(eggincid.StartsWith("E1")) {
                    id = id[1..];
                }
                if(id.Length > 7) {
                    Response = await apiLink.GetBackup(eggincid);
                }
            }

            if(Response?.Farms == null || Response.Farms.Count == 0) {
                await command.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = EmbedError($"Possibly wrong EggInc ID ({eggincid}), it should start with the capital letters EI followed by 16 numbers. **You can also send a screenshot and someone will help you register.**"); });
                return;
            }
            var addedUser = false;
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                dbuser = new DBUser {
                    DiscordId = user.Id,
                    DiscordUsername = user.Username,
                    EggIncAccounts = [new EggIncAccount { Id = Response.EggIncId, Backup = Response, Group = 1 }],
                    CreateOn = DateTimeOffset.Now,
                    GuildId = _client.Guilds.First(x => x.TextChannels.Any(y => y.Id == command.Channel.Id)).Id,
                    showEB = true
                };
                db.DBUsers.Add(dbuser);
                addedUser = true;
            } else {
                if(dbuser.EggIncAccounts.Any(y => y.Id == Response.EggIncId)) {
                    await command.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = EmbedError($"You have already registered this EggInc ID with the bot."); });
                    return;
                }
                if(dbuser.EggIncAccounts.Count == 0) {
                    addedUser = true;
                }
                dbuser.EggIncAccounts.Add(new EggIncAccount { Id = Response.EggIncId, Backup = Response, Group = 1 });
                dbuser.UpdateAccounts();
            }


            await db.SaveChangesAsync();

            IGuildUser socketGuildUser = null;
            try {
                socketGuildUser = (SocketGuildUser)user;
            } catch(Exception) {
                try {
                    socketGuildUser = guild.Users.First(x => x.Id == user.Id);
                } catch(Exception) {
                    socketGuildUser = await _client.Rest.GetGuildUserAsync(guild.Id, user.Id);
                }
            }

            if(!dbuser.Registered.HasValue) {
                dbuser.Registered = DateTimeOffset.Now;
                await db.SaveChangesAsync();
                var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
                if(unjoinedRole is not null) {
                    await socketGuildUser.AddRoleAsync(unjoinedRole);
                }
            }


            var earningsBonus = dbuser.EggIncAccounts.Max(x => x.Backup.EarningsBonus);





            var registeredRole = guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            //socketGuildUser.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
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
                roleText = $"Your new account has been added with an EB of {Response.EarningsBonus.ToEggString()}";
            } else if(role != null) {
                roleText = $"You have been assigned the rank of {role?.Name} thanks to your EB of {earningsBonus.ToEggString()}";
            }

            var faqChannel = ChannelHelper.DetermineChannelType(db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.FaqChannel);
            var faqMention = faqChannel != null ? (faqChannel.GetType() == typeof(SocketTextChannel) ? ((SocketTextChannel)faqChannel).Mention : ((SocketThreadChannel)faqChannel).Mention) : null;
            var faqText = (faqMention != null && dbuser.EggIncAccounts.Count == 1) ? $" When you have a chance, read over {faqMention} to get an idea on how the server and bot functions." : "";

            //if(checkLeague.Role != null) {
            //    roleText += $" Your Grade is {checkLeague.Role.Name}";
            //}

            var compiledMessage = $"Welcome {user.Mention}! {roleText}.{faqText}";
            var response = await ChannelHelper.DetermineAndSend(db, _client, db.Guilds.FirstOrDefault(g => g.Id == guild.Id), guild, GuildChannelType.General, new() { Text = compiledMessage }, logger);
            if(response == null) await command.Channel.SendMessageAsync(compiledMessage);

            //Only add the overflow role for the first registered account
            if(dbuser.EggIncAccounts.Count == 1) {
                var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                if(overflowRole != null) {
                    await socketGuildUser.AddRoleAsync(overflowRole);
                }
            }

            try {
                var guildContracts = await db.GuildContracts.AsQueryable().Where(x => !x.DeletedChannel && x.GuildID == guild.Id).ToListAsync();
                foreach(var guildContract in guildContracts) {
                    var channel = guild.GetTextChannel(guildContract.DiscordChannelId);
                    if(channel != null) {
                        await channel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                    }
                }
            } catch(Exception) { }

            var responseId = command.GetOriginalResponseAsync().Id;
            await CleanWelcomeChannel(guild, _client, user, responseId);
            if(addedUser) {
                try {
                    var ebString = $" ({earningsBonus.ToEggString()})";
                    var newName = ((IGuildUser)user).GetCleanName().Trim().Truncate(32 - ebString.Length) + ebString;

                    try {
                        await ((IGuildUser)user).ModifyAsync(x => x.Nickname = newName);
                    } catch(HttpException) {
                        logger.LogWarning("Unable to update nickname for {user}", user.Username);
                    }

                } catch(Exception) {

                }

            }
            await command.DeleteResponseFix();
        }

        public static async Task CleanWelcomeChannel(SocketGuild guild, DiscordHostedService _client, IUser socketUser, int chain = 0, ulong excludeId = ulong.MaxValue) {
            try {
                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel != null) {
                    var messages = await welcomeChannel.GetMessagesAsync().FlattenAsync();
                    var userMessage = messages.Where(x => (x.MentionedUserIds.Contains(socketUser.Id) || x.Author.Id == socketUser.Id || x?.Interaction?.User?.Id == socketUser.Id) && (excludeId == ulong.MaxValue || x.Id != excludeId));
                    await welcomeChannel.DeleteMessagesBatchAsync(userMessage);
                }
            } catch(Exception) {
                if(chain < 3) {
                    await CleanWelcomeChannel(guild, _client, socketUser, chain++, excludeId);
                }
            }
        }

        [SlashCommand(Description = "Get a users status", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam] SocketUser user, [SlashParam(Required = false)] bool showinchannel = false, 
            [SlashParam(Required = false, Description = "Pull a fresh backup for all accounts of this user before reporting their status")] bool pullfreshbackup = false ) {
            return _userstatus(command, db, _client, apiLink, user, true, showinchannel, pullfreshbackup);
        }

        [SlashCommand(Description = "Get your status", AllowInDMs = true)]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            return _userstatus(command, db, _client, apiLink, command.User);
        }
        public static async Task _userstatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, IUser user, bool admin = false, bool showInChannel = false, bool pullFreshBackup = false) {
            await command.DeferAsync(ephemeral: !showInChannel);
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"); });
                return;
            }
            if(dbuser.EggIncAccounts == null || dbuser.EggIncAccounts.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No registered accounts found for <@{user.Id}>"); });
                return;
            }

            //Pull a fresh backup before userstatus
            if(pullFreshBackup) {
                foreach(var account in dbuser.EggIncAccounts) {
                    var rawBackup = await ContractsAPI.FirstContact(account.Id);
                    if(rawBackup is null || rawBackup.Backup is null) {
                        await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Backup for account `{account?.Backup?.UserName ?? account.Id}` returned as null from the API"); });
                        return;
                    }
                    var customBackup = new CustomBackup(rawBackup.Backup, account?.Backup ?? null);
                    if(customBackup?.Farms is not null) {
                        account.Backup = customBackup;
                    }
                }
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();
            }

            var guild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId);

            //Get a list of all builders from the AccountsString - see method for why this needs to be a list
            var builders = await AccountsString(db, dbuser, apiLink, admin);

            //Grab the last builder from the list to add footers to
            var lastBuilder = builders.LastOrDefault();
            if(lastBuilder.Footer == null) lastBuilder.WithFooter("");

            if(dbuser.TempDisabled) lastBuilder.Footer.Text += $"\n❗User is disabled";

            if(command.Channel is SocketDMChannel) {
                if(dbuser.GuildId > 0) {
                    lastBuilder.Footer.Text += $"\nRegistered with the server {_client.GetGuild(dbuser.GuildId).Name}";
                } else {
                    lastBuilder.Footer.Text += $"\nNot registered with a guild";
                }
            } else if(guild is not null && dbuser.GuildId == guild.Id && !dbuser.TempDisabled) {
                lastBuilder.Footer.Text += $"\nProperly registered with this server";
            } else if(guild is null || dbuser.GuildId != guild.Id) {
                lastBuilder.Footer.Text += $"\nNot registered with this server, try the /moveserver command";
            }

            if(dbuser.Registered.HasValue) {
                lastBuilder.Footer.Text += $"\nJoined the bot on {dbuser.Registered.Value:MMM dd, yyyy}";
            } else {
                lastBuilder.Footer.Text += $"\nMissing bot registration date";
            }

            if(guild is not null && dbuser.GuildId > 0 && !dbuser.TempDisabled && user is SocketGuildUser guildUser && guild.Id == guildUser.Guild.Id) {
                _ = await DiscordHelpers.CheckRoles(db, _client.GetGuild(dbuser.GuildId), guildUser, dbuser, _client, null, []);
            }

            await command.RespondAsync("", embeds: builders.Select(builder => builder.Build()).ToArray(), ephemeral: !showInChannel);
        }


        private static async Task<List<EmbedBuilder>> AccountsString(ApplicationDbContext db, DBUser user, APILink apiLink, bool admin) {
            var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == user.GuildId);

            /*
             Create a new list to store builders in
             25 Fields per embed, means users with 3+ accounts
             cause this to error out with one embed
            */
            var builderList = new List<EmbedBuilder>();

            //Create storage for footers that will be applied at the end
            var footers = new List<string>();

            //Header for first embed
            var builder = new EmbedBuilder {
                Title = "User Status",
                Url = (admin ? $"https://egg9000.com/MyFarms/ViewUser?discordId={user.DiscordId}" : "")
            };

            //Get a list of accounts tied to the user, in order of High -> Low EB
            var accounts = user.EggIncAccounts.OrderByDescending(u => u.Backup?.EarningsBonus);

            //Loop through each account, with the object, and its index
            foreach(var (account, index) in accounts.Select((value, i) => (value, i))) {

                /*
                 If the account is a multiple of 2, we should 
                 add it to the list, and create a new builder instance
                */
                if(index % 2 == 0 && index != 0) {
                    builderList.Add(builder);
                    builder = new EmbedBuilder();
                }

                var backup = await apiLink.GetBackup(account.Id);
                if(backup == null)
                    continue;

                var deviceTypeEmoji = account.DeviceID is not null ? (account.DeviceID.Length == 16 ? ":robot: " : ":apple: ") : "";
                var permitEmoji = account.Backup is not null ? (account.Backup?.PermitLevel == 0 ? "<:Standard_Permit:755734059761795173> " : "<:Pro_Permit:724392625276452955> ") : "";
                var subscriptionEmoji = account.HasActiveSubscription() ? "<:ultra:1131045418319495369> " : "";

                builder.AddField("――――――――――――――――――", ($"{deviceTypeEmoji}{permitEmoji}{subscriptionEmoji}{((account.GetGrade() != default) ? $"{PlayerGradeDetails.GetEmoji(account.GetGrade())} " : "")}***{account.Backup?.UserName} " ?? "***(No Name)") + (backup?.Farms?.Count > 0 ? $"({backup.EarningsBonus.ToEggString()})***: " : "***: ") + (account.Id ?? "No EID"));
                builder.AddField("Last Backup", (backup?.Farms?.Count > 0) ? DiscordHelpers.TimeStamper(DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime)) : "Empty - Check EID", true);

                if(account.GetGrade() != default) {
                    var pGrade = account.GetGrade();
                    var gradeProgressPercent = Math.Round(Math.Round(account.Backup?.GradeProgress ?? 0, 4) * 100, 2);

                    if(gradeProgressPercent > 0 && pGrade != Ei.Contract.Types.PlayerGrade.GradeAaa) {
                        var percentageString = $"{gradeProgressPercent}% to {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)((int)pGrade + 1))} :chart_with_upwards_trend:";
                        builder.AddField("Rankup Percentage", percentageString, true);
                    } else if(gradeProgressPercent < 0 && pGrade != Ei.Contract.Types.PlayerGrade.GradeC) {
                        //Negative percentage indicates ranking down - need to -1 invert the percentage for it to make sense
                        var percentageString = $"\n\t{gradeProgressPercent * -1}% to {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)((int)pGrade - 1))} :chart_with_downwards_trend:";
                        builder.AddField("Rankdown Percentage", percentageString, true);
                    }
                }

                if(dbguild is null || !dbguild.DisableBG) { 
                    if(account.HasActiveSubscription()) {
                        builder.AddField("Boarding Groups", $"{(account?.Group == 0 ? "**None**" : "BG" + account?.Group)}/{(account?.UltraGroup == 0 ? "**None**" : "UG" + account?.UltraGroup)}", true);
                    } else {
                        builder.AddField("Boarding Group", account?.Group == 0 ? "**None**" : "BG" + account?.Group, true);
                    }
                }

                var filterStr = string.Join(", ", account.AutoRegisterRewards ?? []) ?? "No Filter";
                var breakStr = account.OnBreakUntil == default ? "No" : "On break until <t:" + account.OnBreakUntil.ToUnixTimeSeconds() + ":f>";
                var redoOpt = account.RedoLeggacySelection == default ? RedoLeggacyOption.NotSet : account.RedoLeggacySelection;
                var redoStr = redoOpt == RedoLeggacyOption.YesThreshold ? $"{redoOpt} {((double)account.RedoScoreThreshold).ToEggString()}" : redoOpt.ToString();

                builder.AddField("Filter", filterStr == "" ? "None" : filterStr, true);
                builder.AddField("Break", breakStr == "" ? "No" : breakStr, true);
                builder.AddField("Redo Leggacy", redoStr == "" ? "Not Set" : redoStr, true);

                if(dbguild?.AllowGuilds ?? false) {
                    builder.AddField("Guild", string.IsNullOrWhiteSpace(account.Guild) ? "None" : account.Guild, true);
                }

                if(backup.ClientVersion < ContractsAPI.ClientVersion && backup.ClientVersion > 0) {
                    footers.Add($"⚠️ Game outdated for {backup.UserName}, showing {backup.ClientVersion}, new version is {ContractsAPI.ClientVersion} ⚠️");
                }

                /*
                 If an account is the last the user has, pop it into
                 the list, as the loop won't run again
                */
                if(index + 1 == accounts.Count()) {
                    foreach(var footer in footers) {
                        builder.WithFooter(footer);
                    }
                    builderList.Add(builder);
                }
            }

            if(admin) {
                var lastBuilder = builderList.Last();
                var infoSeparatorAdded = false;
                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.UserId == user.Id && !x.Coop.ThreadArchived && !x.Coop.DeletedChannel).ToListAsync();
                var xrefsShortened = false;
                if(xrefs.Count > 4) {
                    xrefs = xrefs.OrderByDescending(x => x.CreatedOn).Take(4).ToList();
                    xrefsShortened = true;
                }

                var coopsString = $"{string.Join("\n", xrefs.Select(x => $"<#{(x.Coop.ThreadID != 0 ? x.Coop.ThreadID : x.Coop.DiscordChannelId)}> {(user.EggIncAccounts.Count > 1 ? $"({user.EggIncAccounts.FirstOrDefault(y => y.Id == x.EggIncId)?.Backup?.UserName ?? "(No name)"})" : "")}"))}";
                if(coopsString != "") {
                    lastBuilder.AddField("――――――――――――――――――", "User Information");
                    infoSeparatorAdded = true;
                    lastBuilder.AddField($"Coops {(xrefsShortened ? "(Shortened List)" : "")}", coopsString);
                }

                var recentDemeritsString = $"{await DemeritCommands.GetDemerits(user.Id, db)}";
                if(recentDemeritsString != "") {
                    if(!infoSeparatorAdded) {
                        lastBuilder.AddField("――――――――――――――――――", "User Information");
                        infoSeparatorAdded = true;
                    }
                    lastBuilder.AddField("Recent Demerits", recentDemeritsString);
                }

                if(!string.IsNullOrEmpty(user.Notes)) {
                    if(!infoSeparatorAdded) {
                        lastBuilder.AddField("――――――――――――――――――", "User Information");
                        infoSeparatorAdded = true;
                    }
                    lastBuilder.AddField("Notes", user.Notes);
                }
            }

            return builderList;
        }


        [SlashCommand(Description = "Disable user, user will not be assigned to co-ops until re-enabled", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task Disable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketUser user) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"));
                return;
            }

            dbuser.TempDisabled = true;
            await db.SaveChangesAsync();

            await command.RespondAsync($"{user.Mention} is disabled.");
        }

        [SlashCommand(Description = "Re-enable user", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task Enable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketUser user) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"));
                return;
            }

            dbuser.TempDisabled = false;
            await db.SaveChangesAsync();

            var responseText = (dbuser.NextBreakExpire is not null && dbuser.NextBreakExpire > DateTimeOffset.Now) ? $" when their break expires {DiscordHelpers.TimeStamper((DateTimeOffset)dbuser.NextBreakExpire, DiscordHelpers.DiscordTimestampFormat.Relative)}" : " from now on.";

            await command.RespondAsync($"{user.Mention} is enabled and will be assigned to co-ops {responseText}");
        }

        private static async Task _cleanWelcome(FauxCommand command, DiscordHostedService _client) {
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            await guild.PruneUsersAsync(10);

            var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);

            var messages = await welcomeChannel.GetMessagesAsync(500).FlattenAsync();

            await (welcomeChannel).DeleteMessagesBatchAsync(messages);

            await command.DeleteResponseFix();
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

        private static async Task _cleanUnpinned(FauxCommand command) {
            var messages = await command.Channel.GetMessagesAsync(500).FlattenAsync();
            messages = messages.Where(x => !x.IsPinned);

            await ((SocketTextChannel)command.Channel).DeleteMessagesBatchAsync(messages);

            await command.DeleteResponseFix();
        }


        [SlashCommand(Description = "Have to bot keep add your EB to your nickname in this server (will auto update)")]
        public static async Task ShowEB(FauxCommand command, ApplicationDbContext db) {
            var dbUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"));
                return;
            }
            if(dbUser.showEB) {
                await command.RespondAsync($"The bot is already set to update your EB automatically. It will update every {LeaderboardUpdater.UpdateTime.TotalMinutes} mins when the leaderboard does.", ephemeral: true);
                return;
            }

            //var higherEB = user.Backups.OrderByDescending(x => x.EarningsBonus).First();

            var ebs = dbUser.EggIncAccounts.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).Select(x => x.Backup.EarningsBonus.ToEggString());
            var ebString = $" ({string.Join(",", values: ebs)})";
            var newName = ((IGuildUser)command.User).GetCleanName().Truncate(32 - ebString.Length) + ebString;

            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);

            dbUser.showEB = true;
            await db.SaveChangesAsync();


            await command.RespondAsync($"{command.User.Mention} will be updated with their EB. To stop this run the command /hideEB", ephemeral: true);
        }

        [SlashCommand(Description = "Remove the EB from your nickname")]
        public static async Task HideEB(FauxCommand command, ApplicationDbContext db) {
            var dbUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"));
            }

            dbUser.showEB = false;
            await db.SaveChangesAsync();

            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            var newName = ((IGuildUser)command.User).GetCleanName();

            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);
            await command.RespondAsync($"{command.User.Mention} will no longer be updated with their EB.", ephemeral: true);
        }

        [SlashCommand(Description = "Check the list of Users/EIDs that have been banned from the server via /kick", ParentCommand = "b", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task BanList(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var guildId = (await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString())))?.Id ?? ulong.MaxValue;
            var bannedUsers = await db.DBUsers.Where(u => (u.Banned && (u.LastGuild == guildId || u.GuildId == guildId)) || (u.ServersBannedFrom != null && u.ServersBannedFrom.IndexOf(guildId.ToString()) > -1)).ToListAsync();
            if(bannedUsers is null || bannedUsers.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("No users are banned from this guild."); });
                return;
            }
            var userList = string.Join("\n", bannedUsers.Select(u => $"{u.DiscordUsername}\t{u.DiscordId}\t" + string.Join(", ", u.EggIncAccounts.Select(a => a.Id).ToList()))) ?? "Could not compile list";
            var guildName = (await db.Guilds.FirstOrDefaultAsync(x => x.Id == command.GuildId)).Name;

            var responseEmbedBuilder = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder().WithName("Banned Users").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).WithColor(Color.DarkRed)
                        .WithDescription($"Users Banned from {guildName}\n\n" +
                        $"{(userList.Length > 1600 ? "_(List too large for Discord - see attached file)_\n" : userList)}");

            //Catch content that is too large, respond with file instead
            if(userList.Length > 1600) await command.RespondWithFileAsync(new FileAttachment(new MemoryStream(Encoding.UTF8.GetBytes(userList.Replace("<@", "").Replace(">", ""))), "BannedUsers.txt"), text: "", embed: responseEmbedBuilder.Build());
            else await command.RespondAsync(content: "", embed: responseEmbedBuilder.Build());
        }

        [SlashCommand(Description = "Remove the ban placed on a user, and their associated EID(s)", ParentCommand = "b", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task RemoveBan(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam(Description = "Discord ID of user to unban")] SocketUser user) {
            await command.DeferAsync();
            var dbBanMessage = "";
            var dbuser = db.DBUsers.FirstOrDefault(u => u.DiscordId == user.Id);
            if(dbuser is not null && dbuser.Banned) {
                var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString()));
                var bannedServersList = dbuser.ServersBannedFrom?.Split(",").ToList() ?? [];
                var wasDbBanned = bannedServersList.Contains(dbGuild.Id.ToString());
                if(wasDbBanned) {
                    bannedServersList.Remove(dbGuild.Id.ToString());
                    dbuser.ServersBannedFrom = string.Join(",", bannedServersList);
                }
                if(dbuser.Banned) dbuser.Banned = false;
                await db.SaveChangesAsync();
                dbBanMessage = "User's DB ban was removed";
            } else {
                dbBanMessage = "No banned DBUser entry found for this user.";
            }

            var discordBanMessage = "";
            var socketGuild = _client.GetGuild(command.GuildId ?? ulong.MaxValue);
            var targetUser = socketGuild.GetUser(user.Id) ?? await _client.GetUserAsync(user.Id);
            var runningUser = socketGuild?.Users?.ToList().FirstOrDefault(u => u.Id == command.User.Id);
            if(runningUser is not null && runningUser.GuildPermissions.ToList().Contains(GuildPermission.BanMembers)) { //Check if running user has ban perms
                var banObject = await socketGuild.GetBanAsync(targetUser);
                if(banObject is null) {
                    discordBanMessage = "User is not banned from via Discord.";
                } else {
                    await socketGuild.RemoveBanAsync(targetUser);
                    discordBanMessage = "User has been unbanned via Discord.";
                }
            } else {
                discordBanMessage = "You do not have the `BanMembers` permission.";
            }

            var unbanEmbed = new EmbedBuilder().WithColor(Color.LighterGrey)
                .AddField("Database Ban Status", dbBanMessage)
                .AddField("Discord Ban Status", discordBanMessage)
                .WithAuthor(new EmbedAuthorBuilder().WithName("Ban Status")
                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
            .Build();

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = unbanEmbed; });
        }

        [SlashCommand(Description = "Kick and user and send them a link to an appeal form", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task Kick(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketUser targetUser, [SlashParam] string intReason, [SlashParam(Required=false)] bool banaccount = false) {
            await command.DeferAsync();
            var kickedWithoutDm = false;
            var dmChannel = await targetUser.CreateDMChannelAsync();
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString()));
            if(banaccount) {
                var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
                if(dbUser is not null) {
                    var bannedServersList = dbUser.ServersBannedFrom?.Split(",")?.ToList() ?? [];
                    bannedServersList.Add(dbGuild.Id.ToString());
                    dbUser.ServersBannedFrom = string.Join(",", bannedServersList);
                    dbUser.Banned = true;
                    await db.SaveChangesAsync();
                }
            }
            try {
                await dmChannel.SendMessageAsync($"You have been {(banaccount ? "banned" : "kicked")} from {guild.Name} for the reason: {intReason}\n\nHere is an appeal form if you would like the rejoin the server: https://forms.gle/NqrqnDZzJ7YaqpAfA");
            } catch(HttpException) {
                kickedWithoutDm = true;
            }

            //Check if running user has ban perms
            var runningUser = _client.Guilds?.FirstOrDefault(g => g.Id == command.GuildId)?.Users?.ToList().FirstOrDefault(u => u.Id == command.User.Id);
            var canBan = (banaccount && runningUser is not null && runningUser.GuildPermissions.ToList().Contains(GuildPermission.BanMembers));

            try {
                var execDiscordUser = (targetUser as SocketGuildUser);
                await (canBan ? execDiscordUser.BanAsync(0, intReason) : execDiscordUser.KickAsync(intReason));
                await command.ModifyOriginalResponseAsync(x => { x.Content = $"{(canBan ? "Banned" : (banaccount ? "DB Banned & Kicked" : "Kicked"))} <@{targetUser.Id}> {(kickedWithoutDm ? "**without**" : "with")} DM"; });
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. The user may not have been {(canBan ? "banned" : "kicked")} from the server.{(canBan ? $" \n\n**The DB Ban was applied to the user's account.**" : "")}"); });
            }
        }
    }
}

