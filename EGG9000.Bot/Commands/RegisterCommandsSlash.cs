using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Services;


using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.FixedWidthTable;
using EGG9000.Common.Helpers;
using EGG9000.Common.Commands;
using static EGG9000.Common.Database.Entities.DBUser;
using EGG9000.Common.Contracts;
using static EGG9000.Common.Helpers.Prefarm;
using Ei;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Contracts;
using MassTransit.Initializers;
using Bugsnag;

namespace EGG9000.Bot.Commands {
    public static class RegisterCommandsSlash {


        [SlashCommand(Description = "Use to move registration to a different discord server")]
        public static async Task MoveServer(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, Bugsnag.IClient bugsnag) {
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(user == null) {
                await command.RespondAsync($"Cannot find user");
            } else if(user.GuildId == guild.Id) {
                if(user.TempDisabled) {
                    await command.RespondAsync($"It looks like you have been disabled, ask staff for help.");
                } else {
                    await command.RespondAsync($"Already configured for the current server, you should get your roles during the next Leaderboard update.");
                }
            } else {
                await command.RespondAsync($"Please wait...");
                if(user.GuildId == 428181243474214942) {
                    await ((SocketGuildUser)command.User).AddRoleAsync(guild.Roles.FirstOrDefault(x => x.Name == "Prophet"));
                }
                user.GuildId = guild.Id;
                await db.SaveChangesAsync();

                //var Response = await ContractsAPI.FirstContact(user.EggIncIds.First().Id);
                var Response = await apiLink.GetBackup(user.EggIncAccounts.First().Id);
                var earningsBonus = Response.EarningsBonus;

                var guildUser = guild.Users.First(x => x.Id == command.User.Id);

                //var role = await DiscordHelpers.SetRole(guild, guildUser, earningsBonus, bugsnag);


                var dbguild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
                if(dbguild != null && dbguild.OverflowServers.Count > 0) {
                    var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                    if(overflowRole != null) {
                        await guildUser.AddRoleAsync(overflowRole);
                    }
                }

                var role = await DiscordHelpers.CheckRoles(guild, guildUser, user, _client, null, new List<LeaderboardUser>());

                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel.Id == command.Channel.Id) {
                    await command.DeleteOriginalResponseAsync();
                    var text = $"Welcome {command.User.Mention}, you have been moved to this server. You have the rank of {role?.Name} with an EB of {earningsBonus.ToEggString()}";
                    var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
                    await generalChannel.SendMessageAsync(text);
                    await CleanWelcomeChannel(guild, _client, command.User);
                } else {
                    await command.ModifyOriginalResponseAsync("Registration has been moved");
                }
            }
        }

        [SlashCommand(Description = "Removed registered EggInc ID from your account", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam] string eggincid, [SlashParam] SocketGuildUser targetUser) {
            return _RemoveID(command, db, apiLink, eggincid, targetUser.Id);
        }
        /* Moving to a staff-only command, leaving commented out in case of re-activation in future
         * [SlashCommand(Description = "Removed registered EggInc ID from your account")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam] string eggincid) {
            return _RemoveID(command, db, apiLink, eggincid, command.User.Id);
        }*/
        public static async Task _RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, string eggincid, ulong userid) {
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == userid);
            if(user == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find user");
                return;
            } else if(user.EggIncAccounts.Any(x => x.Id == eggincid)) {
                user.RemoveID(eggincid);
            } else {
                await command.RespondAsync($"⚠️ERROR: Unable to find the following EggIncId {eggincid}", embeds: AccountsString(db, user, apiLink, false).Result.Select(b => b.Build()).ToArray());
                return;
            }

            await db.SaveChangesAsync();

            var json = JsonConvert.SerializeObject(user.EggIncAccounts, Formatting.Indented);

            await command.RespondAsync($"ID removed", embeds: AccountsString(db, user, apiLink, false).Result.Select(b => b.Build()).ToArray());
        }

        [SlashCommand(Description = "Used to remove a user from a co-op to fix a glitch.", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task LeaveCoop(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser, CoopStatusUpdater coopStatusUpdater, ILogger logger) {
            await command.RespondAsync("Working...");
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Command can only be used in a co-op channel");
                return;
            }
            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == targetUser.Id);

            var xrefs = await db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == dbuser.Id && x.CoopId == coop.Id).ToListAsync();


            var contract = await db.Contracts.FirstAsync(x => x.ID == coop.ContractID);
            foreach(var xref in xrefs) {
                //var res2 = await ContractsAPI.Send(new Ei.LeaveCoopRequest {
                //    ClientVersion = 24,
                //    ContractIdentifier = coop.ContractID,
                //    CoopIdentifier = coop.Name,
                //    PlayerIdentifier = xref.EggIncId,
                //}, xref.EggIncId);
                await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (Ei.Contract.Types.PlayerGrade)coop.League, new Coop { Name = "test" + new Random().Next(10000), ContractID = coop.ContractID }, contract.Details.LengthSeconds, xref.EggIncId);
            }


            await Task.Delay(2);
            var status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);

            //if(status.Participants.Count == contract.MaxUsers) {
            //    foreach(var xref in xrefs) {

            //        var response = await ContractsAPI.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(new Ei.ContractCoopStatusUpdateRequest {
            //            ContractIdentifier = coop.ContractID,
            //            CoopIdentifier = coop.Name.ToLower(),
            //            Eop = 1, SoulPower = 24, UserId = xref.EggIncId, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = xref.EggIncId, BoostTokens = 1, BoostTokensSpent = 1, EggLayingRateBuff = 1, EarningsBuff = 1,
            //            ProductionParams = new Ei.FarmProductionParams {
            //                FarmPopulation = 1000, Delivered = 10000, Elr = 0, FarmCapacity = 10000, Ihr = 0, Sr = 0
            //            }
            //        }, xref.EggIncId, true);



            //        var res2 = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
            //            ClientVersion = 24,
            //            ContractIdentifier = coop.ContractID,
            //            CoopIdentifier = coop.Name,
            //            PlayerIdentifier = xref.EggIncId, Reason = KickPlayerCoopRequest.Types.Reason.Private, RequestingUserId = coop.CreatorID
            //        }, coop.CreatorID);
            //    }

            //    await Task.Delay(2);
            //    status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);
            //}

            if(status.Participants.Count < contract.MaxUsers) {
                logger.LogInformation("Successfully remove {user} from {coop}", dbuser.DiscordUsername, coop.Name);
                var guild = _client.Guilds.First(x => x.Id == coop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == coop.GuildId);
                await coopStatusUpdater.ProcessCoop(coop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, db);

                await command.Channel.SendMessageAsync($"Successfully removed {targetUser.Mention} from co-op, they should be able to rejoin now.");
                await command.DeleteOriginalResponseAsync();
            } else {
                logger.LogInformation("Did not {user} from {coop}", dbuser.DiscordUsername, coop.Name);
                await command.ModifyOriginalResponseAsync($"Attempted to remove {targetUser.Mention} from co-op, please check again in a few minutes.");
            }
        }

        [SlashCommand(Description = "Fix for getting full co-op error")]
        public static async Task FixFullCoopError(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, CoopStatusUpdater coopStatusUpdater, ILogger logger) {
            await command.RespondAsync("Please wait...");
            var coop = await db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Command can only be used in a co-op channel");
                return;
            }

            var dbuser = coop.UserCoopsXrefs.FirstOrDefault(x => x.User.DiscordId == command.User.Id)?.User;
            if(dbuser is null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Unable to locate user in co-op.");
            }

            var status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);

            var details = new CoopDetails(coop, coop.Contract, coop.League, coop.UserCoopsXrefs.SelectMany(y => y.User.EggIncAccounts.Select(x => new UserWithBackup { Backup = x.Backup, User = y.User })).ToList(), _client, status);

            var xref = details.CoopParticipants.FirstOrDefault(x => x.DBUser?.DiscordId == command.User.Id && x.EggsShipped == 0);

            if(xref is null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Unable to locate user with zero production.");
                return;
            }

            //logger.LogInformation("Attempting to fix {user} in {coop} by submitting leave request", dbuser.DiscordUsername, coop.Name);
            //var res2 = await ContractsAPI.Send(new Ei.LeaveCoopRequest {
            //    ClientVersion = 24,
            //    ContractIdentifier = coop.ContractID,
            //    CoopIdentifier = coop.Name,
            //    PlayerIdentifier = xref.EggIncId,
            //}, xref.EggIncId);
            logger.LogInformation("Attempting to fix {user} in {coop} by creating temp co-op", dbuser.DiscordUsername, coop.Name);
            var contract = await db.Contracts.FirstAsync(x => x.ID == coop.ContractID);
            await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (Ei.Contract.Types.PlayerGrade)coop.League, new Coop { Name = "test" + new Random().Next(10000), ContractID = coop.ContractID }, contract.Details.LengthSeconds, xref.EggIncId);

            await Task.Delay(2);
            status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);

            if(status.Participants.Count == contract.MaxUsers) {
                logger.LogInformation("Attempting to fix {user} in {coop} by submitting kick request", dbuser.DiscordUsername, coop.Name);
                var res3 = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
                    ClientVersion = 24,
                    ContractIdentifier = coop.ContractID,
                    CoopIdentifier = coop.Name,
                    PlayerIdentifier = xref.EggIncId, Reason = KickPlayerCoopRequest.Types.Reason.Private, RequestingUserId = coop.CreatorID
                }, coop.CreatorID);

                await Task.Delay(2);
                status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);
            }


            if(status.Participants.Count < contract.MaxUsers) {
                logger.LogInformation("Successfully remove {user} from {coop}", dbuser.DiscordUsername, coop.Name);
                var guild = _client.Guilds.First(x => x.Id == coop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == coop.GuildId);
                await coopStatusUpdater.ProcessCoop(coop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, db);

                await command.Channel.SendMessageAsync($"Successfully removed {command.User.Mention} from co-op, they should be able to rejoin now.");
                await command.DeleteOriginalResponseAsync();
            } else {
                logger.LogInformation("Did not {user} from {coop}", dbuser.DiscordUsername, coop.Name);
                await command.ModifyOriginalResponseAsync($"Attempted to remove {command.User.Mention} from co-op, please check again in a few minutes.");
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
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(guild == null) {
                await command.RespondAsync("Unable to find server, please run this command in a server");
                return;
            }
            if(user is not null) {
                if(user.GuildId == command.GuildId && user.EggIncAccounts.Count > 0) {
                    if(user.TempDisabled) {
                        await command.RespondAsync($"Looks like you are currently disabled, please ask for someone from staff to find out about getting re-enabled.");
                        return;
                    } else {
                        await command.RespondAsync($"{targetUser.Mention}, you have already accepted the rules. Your roles should show up during the next leaderboard update.");
                        return;
                    }
                } else if(user.GuildId == command.GuildId && user.EggIncAccounts.Count == 0) {
                    await command.RespondAsync($"{targetUser.Mention}, you have already accepted the rules. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window.");
                    return;
                } else if(user.EggIncAccounts.Count > 0 && user.GuildId > 0 & !user.TempDisabled) {
                    await command.RespondAsync($"{targetUser.Mention}, looks like you are registered with another server, if you would like to move to this server use the </moveserver:1095116354329268366> command");
                    return;
                } else if(user.TempDisabled) {
                    await command.RespondAsync($"Looks like you are currently disabled, please wait for someone from staff to get you re-enabled.");
                    return;
                } else if(user.GuildId != guild.Id) {
                    await command.RespondAsync($"{targetUser.Mention}, now run the </moveserver:1095116354329268366> command");
                    return;
                } else {
                    var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
                    await generalChannel.SendMessageAsync($"Welcome back {targetUser.Mention}!");


                    var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
                    if(activeRole != null) {
                        await ((SocketGuildUser)targetUser).AddRoleAsync(activeRole);
                    }

                    await CleanWelcomeChannel(guild, _client, targetUser);
                    return;
                }
            }

            user = new DBUser {
                DiscordId = targetUser.Id,
                DiscordUsername = targetUser.Username,
                AcceptedRules = true,
                CreateOn = DateTimeOffset.Now,
                GuildId = guild.Id,
                showEB = true
            };
            db.DBUsers.Add(user);

            string channelText = "";
            var talkChannel = guild.TextChannels.FirstOrDefault(x => x.Id == 746509501271769210);
            if(talkChannel != null) {
                channelText = $"If you have questions about this, feel free to message us in {talkChannel.Mention}";
            }
            await command.RespondAsync($"{targetUser.Mention}, next we’ll need you to register with your Egg, Inc account. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window. More detailed instructions are included in the pinned messages of this channel.\n\nWhy do we need this? The bot needs everyone's ID to be able to track Farming activity, rates, and contract preferences. It also factors this information in to create balanced co-ops for each contract. The bot only reads certain parts of the info and does not make any changes. {channelText}");


            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Update your EggIncID if it has changed", AllowInDMs = true)]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, _client, apiLink, eggincid, (SocketGuildUser)command.User, accountnumber);
        }
        [SlashCommand(Description = "EggIncID someones ID", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam] SocketGuildUser targetUser, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, _client, apiLink, eggincid, targetUser, accountnumber);
        }
        public static async Task _UpdateID(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, string eggincid, SocketGuildUser targetUser, int accountnumber) {
            var Response = await apiLink.GetBackup(eggincid);


            if(Response == null || Response.Farms == null || Response.Farms.Count == 0) {
                await command.RespondAsync($" {command.User.Mention} Error:  Possibly wrong EggInc ID**", ephemeral: true);
                return;
            }
            if(Response.EggIncId != eggincid) {
                await command.RespondAsync($"Error matching ID {eggincid} - {Response.EggIncId}", ephemeral: true);
                return;
            }

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
            if(user.EggIncAccounts.Count > 1) {
                if(accountnumber == 0) {
                    var count = 1;
                    var accounts = String.Join("\n", user.EggIncAccounts.Select(x => $"{count++} {x.Backup?.UserName} EB: {x.Backup?.EarningsBonus.ToEggString()}"));
                    await command.RespondAsync($"User has multiple accounts, please specifiy which account `/updateid {{eggincid}} {{accountnumber}}`\n{accounts}", ephemeral: true);
                    return;
                }
                var account = accountnumber - 1;

                var eggIncIDs = new List<EggIncAccount>();
                for(var i = 0; i < user.EggIncAccounts.Count; i++) {
                    if(i == account)
                        eggIncIDs.Add(new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName });
                    else
                        eggIncIDs.Add(user.EggIncAccounts[i]);
                }

                user.EggIncAccounts = eggIncIDs;
            } else {
                user.EggIncAccounts = new List<EggIncAccount> { new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName } };
            }
            await db.SaveChangesAsync();

            await command.RespondAsync($"ID Update", embeds: AccountsString(db, user, apiLink, false).Result.Select(b => b.Build()).ToArray(), ephemeral: true);

        }

        [SlashCommand(Description = "Register your EggInc account with the bot", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, Bugsnag.IClient bugsnag, ILogger logger, [SlashParam(Description = "EggIncID which begins with EI followed by numbers")] string eggincid, [SlashParam] SocketGuildUser user) {
            return _Register(command, db, _client, apiLink, bugsnag, eggincid, user, logger);
        }
        [SlashCommand(Description = "Register your EggInc account with the bot")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, Bugsnag.IClient bugsnag, ILogger logger, [SlashParam(Description = "EggIncID which begins with EI followed by numbers")] string eggincid) {
            return _Register(command, db, _client, apiLink, bugsnag, eggincid, command.User, logger);
        }
        public static async Task _Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, Bugsnag.IClient bugsnag, string eggincid, IUser user, ILogger logger) {
            await command.RespondAsync("Processing...");
            eggincid = eggincid.ToUpper();

            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var guildObj = db.Guilds.FirstOrDefault(x => x.Id == guild.Id || x.OverflowServersJson.Contains(guild.Id.ToString()));

            try {
                var bannedUsers = db.DBUsers.Where(x => x.Banned).ToList().SelectMany(u => u.EggIncAccounts).ToList();
                if(bannedUsers is not null) {
                    if(bannedUsers.Select(e => e.Id.ToUpper()).ToList().Contains(eggincid)) {
                        //1132753030022975499 - dev server banned thread
                        var bannedUserThread = guildObj.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.BannedUserThread);
                        if(bannedUserThread is not null) {
                            var thread = guild.GetThreadChannel(bannedUserThread.Id);
                            if(thread is not null) {
                                await thread.SendMessageAsync($"{user.Mention} attempted to register with a banned EggInc ID `{eggincid}` in <#{command.Channel.Id}>");
                            }
                            await command.ModifyOriginalResponseAsync(m => m.Content = $"{user.Mention} Error:  EggInc ID `{eggincid}` has been banned from registering with this server.");
                        } else {
                            var staffMention = guildObj.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.CallStaffTagRole).Id.ToString() ?? "";
                            await command.ModifyOriginalResponseAsync(m => m.Content = $" {(staffMention is not null ? "<@&" + staffMention + ">" : "")} {user.Mention} Error:  EggInc ID `{eggincid}` has been banned from registering with this server.");
                        }
                        return;
                    }
                }
            } catch(Exception ex) {
                bugsnag.Notify(ex);
                logger.LogError(ex, "Error checking banned users");
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
                await command.ModifyOriginalResponseAsync(m => m.Content = $" {user.Mention} Error:  Possibly wrong EggInc ID ({eggincid}), it should start with the capital letters EI followed by numbers. **You can also send a screenshot and someone will help you register.**");
                return;
            }
            var addedUser = false;
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                dbuser = new DBUser {
                    DiscordId = user.Id,
                    DiscordUsername = user.Username,
                    EggIncAccounts = new List<EggIncAccount> { new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName, Backup = Response, Group = 1 } },
                    CreateOn = DateTimeOffset.Now,
                    GuildId = _client.Guilds.First(x => x.TextChannels.Any(y => y.Id == command.Channel.Id)).Id,
                    showEB = true
                };
                db.DBUsers.Add(dbuser);
                addedUser = true;
            } else {
                if(dbuser.EggIncAccounts.Any(y => y.Id == Response.EggIncId)) {
                    await command.ModifyOriginalResponseAsync(m => m.Content = $"You are already registered with the bot. {user.Mention}");
                    return;
                }
                if(dbuser.EggIncAccounts.Count == 0) {
                    addedUser = true;
                }
                dbuser.EggIncAccounts.Add(new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName, Backup = Response, Group = 1 });
                dbuser.UpdateAccounts();
            }
            if(!dbuser.Registered.HasValue) {
                dbuser.Registered = DateTimeOffset.Now;
            }


            var earningsBonus = dbuser.EggIncAccounts.Max(x => x.Backup.EarningsBonus);



            IGuildUser socketGuildUser = null;
            try {
                socketGuildUser = (SocketGuildUser)user;
            } catch(Exception) {
                try {
                    guild.Users.First(x => x.Id == user.Id);
                } catch(Exception) {
                    socketGuildUser = await _client.Rest.GetGuildUserAsync(guild.Id, user.Id);
                }
            }

            await db.SaveChangesAsync();

            var registeredRole = guild.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            //socketGuildUser.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            if(registeredRole is not null && !socketGuildUser.RoleIds.Any(x => x == registeredRole.Id)) {
                await socketGuildUser.AddRoleAsync(registeredRole);
            }

            if(dbuser.GuildId != guild.Id) {
                dbuser.GuildId = guild.Id;
                await db.SaveChangesAsync();
            }
            var role = await DiscordHelpers.CheckRoles(guild, (SocketGuildUser)socketGuildUser, dbuser, _client, null, new List<LeaderboardUser>());

            var roleText = "";
            if(dbuser.EggIncAccounts.Count > 1) {
                roleText = $"Your new account has been added with an EB of {Response.EarningsBonus.ToEggString()}";
            } else if(role != null) {
                roleText = $"You have been assigned the rank of {role?.Name} thanks to your EB of {earningsBonus.ToEggString()}.";
            }
            var faqText = "";
            var faqChannel = await _client.GetChannelAsync(GuildChannelType.FaqChannel, guild);
            if(faqChannel != null && dbuser.EggIncAccounts.Count == 1) {
                faqText = $"When you have a chance, read over {faqChannel.Mention} to get an idea on how the server and bot functions";
            }


            //if(checkLeague.Role != null) {
            //    roleText += $" Your Grade is {checkLeague.Role.Name}";
            //}

            var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
            await (generalChannel ?? command.Channel).SendMessageAsync($"Welcome {user.Mention}! {roleText}. {faqText}");

            var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
            if(overflowRole != null) {
                await socketGuildUser.AddRoleAsync(overflowRole);
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

                } catch(Exception) {

                }

            }
            await command.DeleteResponseFix();
        }

        public static async Task CleanWelcomeChannel(SocketGuild guild, DiscordHostedService _client, IUser socketUser, int chain = 0) {
            try {
                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel != null) {
                    var messages = await welcomeChannel.GetMessagesAsync().FlattenAsync();

                    var userMessage = messages.Where(x => x.MentionedUserIds.Contains(socketUser.Id) || x.Author.Id == socketUser.Id);

                    await welcomeChannel.DeleteMessagesBatchAsync(userMessage);
                }
            } catch(Exception) {
                if(chain < 3) {
                    await CleanWelcomeChannel(guild, _client, socketUser, chain++);
                }
            }
        }


        [SlashCommand(Description = "Get a users status", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam] SocketGuildUser user, [SlashParam(Required = false)] bool ShowInChannel = false) {
            return _userstatus(command, db, _client, apiLink, user, true, ShowInChannel);
        }

        [SlashCommand(Description = "Get your status", AllowInDMs = true)]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            return _userstatus(command, db, _client, apiLink, command.User, false, false);
        }
        public static async Task _userstatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, IUser user, bool admin = false, bool showInChannel = false) {
            await command.DeferAsync(ephemeral: !showInChannel);
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"⚠️ERROR: Bot error - User not registered", ephemeral: !showInChannel);
                return;
            }

            var guild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId);

            //Get a list of all builders from the AccountsString - see method for why this needs to be a list
            var builders = await AccountsString(db, dbuser, apiLink, admin);

            //Grab the last builder from the list to add footers to
            var lastBuilder = builders.LastOrDefault();
            if(lastBuilder.Footer == null) lastBuilder.WithFooter("");

            if(dbuser.TempDisabled) lastBuilder.Footer.Text += $"\n❗User is disabled";

            if(admin && !showInChannel && !string.IsNullOrWhiteSpace(dbuser.Notes)) {
                lastBuilder.Footer.Text += $"\n**Notes:** {dbuser.Notes}";
            }

            if(command.Channel is SocketDMChannel) {
                if(dbuser.GuildId > 0) {
                    lastBuilder.Footer.Text += $"\nRegistered with the server {_client.GetGuild(dbuser.GuildId).Name}";
                } else {
                    lastBuilder.Footer.Text += $"\nNot registered with a guild";
                }
            } else if(dbuser.GuildId == guild.Id && !dbuser.TempDisabled) {
                lastBuilder.Footer.Text += $"\nProperly registered with this server";
            } else if(dbuser.GuildId != guild.Id) {
                lastBuilder.Footer.Text += $"\nNot registered with this server, try the /moveserver command";
            }

            lastBuilder.Footer.Text += $"\nJoined the bot on {dbuser.Registered.Value.ToString("MMM dd, yyyy")}";

            if(dbuser.GuildId > 0 && !dbuser.TempDisabled && user is SocketGuildUser) {
                _ = await DiscordHelpers.CheckRoles(_client.GetGuild(dbuser.GuildId), (SocketGuildUser)user, dbuser, _client, null, new List<LeaderboardUser>());
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
            var accounts = user.EggIncAccounts.OrderBy(u => u.Backup?.EarningsBonus).Reverse();

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

                builder.AddField("――――――――――――――――――", ($"{((account.GetGrade() != default) ? PlayerGradeDetails.GetEmoji(account.GetGrade()) : "")} ***{account.Name} " ?? "***(No Name)") + (backup?.Farms?.Count > 0 ? $"({backup.EarningsBonus.ToEggString()})***: " : "***: ") + (account.Id ?? "No EID"));
                builder.AddField("Last Backup", (backup?.Farms?.Count > 0) ? DiscordHelpers.TimeStamper(DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime)) : "Empty - Check EID", true);

                if(!string.IsNullOrEmpty(account.DeviceID)) {
                    builder.AddField("Device Type", account.DeviceID.Length == 16 ? "Android :robot:" : "iOS :apple:", true);
                }

                if(account.SubscriptionLevel is not null) {
                    builder.AddField("Subscription", $"ULTRA {(UserSubscriptionInfo.Types.Level)account.SubscriptionLevel}", true);
                }

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
                    if(account.SubscriptionLevel is not null) {
                        builder.AddField("Boarding Groups", $"{(account?.Group == 0 ? "**None**" : "BG" + account?.Group)}/{(account?.UltraGroup == 0 ? "**None**" : "UG" + account?.UltraGroup)}");
                    } else {
                        builder.AddField("Boarding Group", account?.Group == 0 ? "**None**" : "BG" + account?.Group, true);
                    }
                }

                var filterStr = string.Join(", ", account.AutoRegisterRewards ?? new List<RewardType>()) ?? "No Filter";
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
                    footers.Add($"⚠️ **Game outdated for {backup.UserName}, showing {backup.ClientVersion}, new version is {ContractsAPI.ClientVersion}** ⚠️");
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
                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.UserId == user.Id && !x.Coop.DeletedChannel).ToListAsync();

                var coopsString = $"{string.Join("\n", xrefs.Select(x => $"<#{x.Coop.DiscordChannelId}> {(user.EggIncAccounts.Count > 1 ? $"({user.EggIncAccounts.FirstOrDefault(y => y.Id == x.EggIncId)?.Name})" : "")}"))}";
                if(coopsString != "") {
                    lastBuilder.AddField("――――――――――――――――――", "User Information");
                    infoSeparatorAdded = true;
                    lastBuilder.AddField("Coops", coopsString);
                }

                var recentDemeritsString = $"{await DemeritCommands.GetDemerits(user.Id, db)}";
                if(recentDemeritsString != "") {
                    if(!infoSeparatorAdded) {
                        lastBuilder.AddField("――――――――――――――――――", "User Information");
                        infoSeparatorAdded = true;
                    }
                    lastBuilder.AddField("Recent Demerits", recentDemeritsString);
                }
            }

            return builderList;
        }


        [SlashCommand(Description = "Disable user, user will not be assigned to co-ops until re-enabled", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task Disable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
            }

            dbuser.TempDisabled = true;
            await db.SaveChangesAsync();

            await command.RespondAsync($"{user.Mention} is disabled.");
        }

        [SlashCommand(Description = "Re-enable user", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task Enable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
            }

            dbuser.TempDisabled = false;
            await db.SaveChangesAsync();

            await command.RespondAsync($"{user.Mention} is enabled and will be assigned to co-ops from now on.");
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
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
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
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
            }

            user.showEB = false;
            await db.SaveChangesAsync();



            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            var newName = ((IGuildUser)command.User).GetCleanName();

            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);


            await command.RespondAsync($"{command.User.Mention} will no longer be updated with their EB.", ephemeral: true);
        }

        [SlashCommand(Description = "Kick and user and send them a link to an appeal form", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task Kick(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser, [SlashParam] string reason, [SlashParam(Required=false)] bool banaccount = false) {
            try {
                var dmChannel = await targetUser.CreateDMChannelAsync();
                var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
                if(banaccount) {
                    var userObj = db.DBUsers.FirstOrDefault(x => x.DiscordId == targetUser.Id);
                    if(userObj is not null) {
                        userObj.Banned = true;
                        await db.SaveChangesAsync();
                    }
                }
                await dmChannel.SendMessageAsync($"You have been kicked from {guild.Name} for the reason: {reason}\n\nHere is an appeal form if you would like the rejoin the server: https://forms.gle/NqrqnDZzJ7YaqpAfA");
                await targetUser.KickAsync();
                await command.RespondAsync("Kicked with DM");
            } catch(HttpException) {
                await command.RespondAsync("Unable to send DM, user is not yet kicked");
            }
        }
    }
}

