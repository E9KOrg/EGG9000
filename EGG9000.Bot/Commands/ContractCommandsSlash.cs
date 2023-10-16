using Discord;
using Discord.WebSocket;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using Nito.AsyncEx;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using EGG9000.Common.Migrations;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using Bugsnag.Payload;
using System.Security.Principal;
using static Ei.Contract.Types;
using Ei;

using Microsoft.Extensions.Logging;
using Exception = System.Exception;
using EGG9000.Bot.Automated.Coops;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;

namespace EGG9000.Bot.Commands {
    public static class ContractCommandsSlash {

        [SlashCommand(Description = "Fix a user getting full co-op error", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task FixFullCoopError(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, CoopStatusUpdater coopStatusUpdater, ILogger logger, [SlashParam(AutocompleteHandler = typeof(UserAccountChannelSpecificAutoComplete))] string useraccount) {
            await command.RespondAsync("Please wait...");
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbuser is null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Unable to locate user in co-op.");
                return;
            }

            var coop = await db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Command can only be used in a co-op channel");
                return;
            }

            await _fixFullCoopError(command, db, _client, coopStatusUpdater, logger, dbuser, coop);
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

            await _fixFullCoopError(command, db, _client, coopStatusUpdater, logger, dbuser, coop);
        }

        private static async Task _fixFullCoopError(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, CoopStatusUpdater coopStatusUpdater, ILogger logger, DBUser dbuser, Coop coop) {
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

        [SlashCommand(Description = "Makes a co-op public", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task MakePublic(FauxCommand command, ApplicationDbContext db) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find coop for this channel {command.Channel.Name}");
                return;
            }

            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = true,
                RequestingUserId = coop.CreatorID
            }, coop.CreatorID);

            if(response.Success) {
                await command.RespondAsync($"{coop.Name} is now public.");
            } else {
                await command.RespondAsync($"{coop.Name} should now be public.");
                //await command.RespondAsync($"⚠️ERROR: {response.Message}");
            }
        }

        [SlashCommand(Description = "Move a user to a different grade of coop", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task MoveGrade(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountChannelSpecificAutoComplete))] string useraccount,
            [SlashParam(AutocompleteHandler = typeof(MoveGradeAutoComplete))] uint newgrade) {
            await command.RespondAsync("Please wait...");
            var targetCoop = await db.Coops.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Please use in a co-op channel");
                return;
            }

            var userid = useraccount.Split("|")[0];
            var guid = Guid.Parse(userid);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            /* Find current coop xrefs */
            var xref = await db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == guid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find user in co-op");
                return;
            }

            /* Find a new co-op */
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == targetCoop.GuildId && x.ContractID == targetCoop.ContractID && x.League == newgrade 
                && x.CurrentUsers < x.MaxUsers && (int)x.Status > 2 && (int)x.Status < 13 && x.CoopEnds > DateTimeOffset.Now).ToListAsync();

            if(coops.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}");
                return;
            }

            Coop newCoop = null;
            var contract = await db.Contracts.FirstOrDefaultAsync(x => x.ID == targetCoop.ContractID);
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, newgrade, usersWithBackups, _client, coop.LastStatusUpdate);
                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}");
                return;
            }
            /* END Find a new co-op */

            /* MOVING TO NEW COOP */
            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = _client.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", discordUser, dbuser, (SocketTextChannel)coopChannel, (SocketTextChannel)command.Channel);
            if(newxref == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }
            db.Add(newxref);
            /* END MOVING TO NEW COOP */

            /* REMOVING FROM OLD COOP */
            db.Remove(xref);
            /* END REMOVING FROM OLD COOP */

            await command.RespondAsync($"Removed {discordUser.Mention} ({account.Backup?.UserName}) from {((ITextChannel)command.Channel).Mention}, and moved to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }

        public enum FindCoopPrioritization {
            [Discord.Interactions.ChoiceDisplay("Low finish time (default)")] FinishTimeLow = 0,
            [Discord.Interactions.ChoiceDisplay("High finish time")] FinishTimeHigh = 1,
            [Discord.Interactions.ChoiceDisplay("Low player count")] LowPlayerCount = 2,
            [Discord.Interactions.ChoiceDisplay("High player count")] HighPlayerCount = 3,
        };

        [SlashCommand(Description="Attempt to find a coop for a user, move user to said coop", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task FindCoopForUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, 
            [SlashParam(AutocompleteHandler = typeof(StaffContractAutoComplete))] string contractid, [SlashParam(Required = false)]FindCoopPrioritization priority = FindCoopPrioritization.FinishTimeLow) {
            await command.DeferAsync();
            var guildRef = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString())); 
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];
            var userXrefs = await db.UserCoopXrefs.Include(x => x.Coop).ThenInclude(x => x.Contract).Include(x => x.Coop).Where(x => x.EggIncId == account.Id).ToListAsync();

            var existingCoop = userXrefs.FirstOrDefault(r => r.Coop.Contract == contract && (int)r.Coop.Status > 2 && (int)r.Coop.Status < 13 && r.Coop.CoopEnds > DateTimeOffset.Now);

            if(contract.cc_only && account.SubscriptionLevel is null) {
                await command.RespondAsync($"⚠️ERROR: Non-subscribed account cannot be assigned to subscriber-only contract");
                return;
            } else if(existingCoop is not null) {
                await command.RespondAsync($"⚠️ERROR: User is already assigned a coop for contract {contract.Name}: <#{existingCoop.Coop.DiscordChannelId}>");
                return;
            } else if(account.GetGrade() is PlayerGrade.GradeUnset) {
                await command.RespondAsync($"⚠️ERROR: User does not have a grade set, and cannot be moved into a coop");
                return;
            }

            var coops = await db.Coops.Include(c => c.Contract).Include(c => c.UserCoopsXrefs).Where(c => c.Contract == contract && c.GuildId == guildRef.Id && c.League == (uint)account.GetGrade()
             && c.CurrentUsers < c.MaxUsers && (int)c.Status > 2 && (int)c.Status < 13 && c.CoopEnds > DateTimeOffset.Now && c.ProjectedFinish > DateTimeOffset.Now).ToListAsync();

            if(!coops.Any()) {
                await command.RespondAsync($"⚠️ERROR: No open Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())} coop spots found for {contract.Name} (I1)");
                return;
            }

            _ = priority switch {
                FindCoopPrioritization.FinishTimeLow => coops = coops.OrderBy(c => c.ProjectedFinish).ToList(),
                FindCoopPrioritization.FinishTimeHigh => coops = coops.OrderByDescending(c => c.ProjectedFinish).ToList(),
                FindCoopPrioritization.LowPlayerCount => coops = coops.OrderBy(c => c.UserCoopsXrefs.Count).ToList(),
                FindCoopPrioritization.HighPlayerCount => coops = coops.OrderByDescending(c => c.UserCoopsXrefs.Count).ToList(),
                _ => coops = coops.OrderBy(c => c.ProjectedFinish).ToList(),
            };

            Coop newCoop = null;
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, (uint)account.GetGrade(), usersWithBackups, _client, coop.LastStatusUpdate);
                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop is null) {
                await command.RespondAsync($"⚠️ERROR: No open Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())} coop spots found for {contract.Name} (I2)");
                return;
            }

            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = _client.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", discordUser, dbuser, (SocketTextChannel)coopChannel, (SocketTextChannel)command.Channel);
            if(newxref == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }
            db.Add(newxref);

            await command.RespondAsync($"Sucessfully moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Makes this co-op private", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task MakePrivate(FauxCommand command, ApplicationDbContext db) {
            var name = new Regex(@"\w+").Match(command.Channel.Name.ToLower()).Value;
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find coop for this channel {command.Channel.Name}");
                return;
            }

            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = false,
                RequestingUserId = coop.CreatorID
            }, coop.CreatorID);

            if(response.Success) {
                await command.RespondAsync($"{coop.Name} is now private.");
            } else {
                await command.RespondAsync($"{coop.Name} should now be private.");
            }
        }

        //[SlashCommand(Description = "Adds prefarmers from selected contract to this channel", AdminOnly = true)]
        //public static async Task AddPrefarmers(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
        //        return;
        //    }
        //    await command.RespondAsync($"Please wait...adding prefarmers");

        //    var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

        //    var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //    foreach(var user in coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants)) {
        //        await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(user.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
        //    }

        //    foreach(var user in coopsBreakdown.ExpiredFarms) {
        //        await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(user.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
        //    }

        //    await command.Channel.SendMessageAsync($"{coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).Count()} prefarmers added");
        //}


        [SlashCommand(Description = "Adds an outside co-op so you can track it's progress", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task AddCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname, [SlashParam] string grade) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }

            int league = 0;
            switch(grade.ToLower().Trim()) {
                case "aaa":
                    league = 5;
                    break;
                case "aa":
                    league = 4;
                    break;
                case "a":
                    league = 3;
                    break;
                case "b":
                    league = 2;
                    break;
                case "c":
                    league = 1;
                    break;
            }

            var status = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coopname.ToLower());
            if(status != null && status.Success) {

                var coop = new Coop {
                    ContractID = guildContract.ContractID,
                    Created = DateTimeOffset.Now,
                    GuildId = guildContract.GuildID,
                    Name = coopname,
                    MaxUsers = guildContract.Contract.MaxUsers,
                    Status = CoopStatusEnum.WaitingOnAssigned,
                    League = (uint)league,
                    CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining)
                };
                db.Coops.Add(coop);
                await db.SaveChangesAsync();
                await command.RespondAsync($"Co-op Added: {coopname} for {((SocketTextChannel)contractchannel).Mention}");
                return;
            } else {
                await command.RespondAsync($"⚠️ERROR: Unable to find co-op details, double check co-op name ({coopname}) and correct contract channel ({((SocketTextChannel)contractchannel).Mention}).");
                return;
            }
        }


        //[SlashCommand(Description = "Start a new co-op")]
        //public static async Task NewCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname, [SlashParam] string grade) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
        //        return;
        //    }

        //    int league = 0;
        //    switch(grade.ToLower().Trim()) {
        //        case "aaa":
        //            league = 5;
        //            break;
        //        case "aa":
        //            league = 4;
        //            break;
        //        case "a":
        //            league = 3;
        //            break;
        //        case "b":
        //            league = 2;
        //            break;
        //        case "c":
        //            league = 1;
        //            break;
        //    }

        //    var status = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coopname.ToLower());
        //    if(status != null && status.Success) {

        //        var coop = new Coop {
        //            ContractID = guildContract.ContractID,
        //            Created = DateTimeOffset.Now,
        //            GuildId = guildContract.GuildID,
        //            Name = coopname,
        //            MaxUsers = guildContract.Contract.MaxUsers,
        //            Status = CoopStatusEnum.WaitingOnAssigned,
        //            League = (uint)league,
        //            CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining)
        //        };
        //        db.Coops.Add(coop);
        //        await db.SaveChangesAsync();
        //        await command.RespondAsync($"Co-op Added: {coopname} for {((SocketTextChannel)contractchannel).Mention}");
        //        return;
        //    } else {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find co-op details, double check co-op name ({coopname}) and correct contract channel ({((SocketTextChannel)contractchannel).Mention}).");
        //        return;
        //    }
        //}



        [SlashCommand(Description = "Fix a users reference in a co-op when they are showing as an alien", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task FixReference(FauxCommand command, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient discord, ApplicationDbContext db, [SlashParam] SocketGuildUser targetuser, [SlashParam(Description = "Egg Inc Name, will match partial name")] string eggincname) {
            //var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            var xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == targetuser.Id && x.Coop.DiscordChannelId == command.Channel.Id && !x.JoinedCoop);
            if(xref == null) {
                xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == targetuser.Id && x.Coop.DiscordChannelId == command.Channel.Id);
            }
            if(xref == null) {
                await command.RespondAsync($"⚠️ERROR: Bot error - Unable to find user assignment to co-op");
                return;
            }


            var t = xref.Coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserName.ToLower().Contains(eggincname.ToLower()));
            if(t == null) {
                await command.RespondAsync($"⚠️ERROR: Bot error - Unable to find user in co-op. You can use a partial in-game name.");
                return;
            }

            //var newxref = new UserCoopXref {
            //    AddedToChannel = true,
            //    CoopId = xref.CoopId,
            //    CreatedOn = xref.CreatedOn,
            //    JoinedCoop = false,
            //    Starter = false,
            //    UserId = xref.GetID(),
            //    WaitingOnStarter = false,
            //    EggIncId = t.GetID(),
            //    RefEggIncId = xref.EggIncId,
            //    WasAssigned = true
            //};

            //db.Remove(xref);
            //db.Add(newxref);

            xref.FixedUserName = t.UserName;
            await db.SaveChangesAsync();

            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            var guild = discord.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
            var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
            await coopStatusUpdater.ProcessCoop(targetCoop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, db);

            await command.RespondAsync($"Fixed {targetuser.Mention} reference.");
        }

        [SlashCommand(Description = "Move a user to a co-op.", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task MoveToCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, [SlashParam(AutocompleteHandler = typeof(MoveToCoopCoopNameAutoComplete))] string coopid) {
            var coop = await db.Coops.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == Guid.Parse(coopid));
            Guid userid;
            try {
                userid = Guid.Parse(useraccount.Split("|")[0]);
            } catch(Exception e) {
                  await command.RespondAsync($"⚠️ERROR: Unable to parse user account, please use the autocomplete dropdown. (DEBUG: Value for useraccount is `{useraccount}`)");
                return;
            }
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == userid);
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = _client.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", discordUser, dbuser, (SocketTextChannel)coopChannel, (SocketTextChannel)command.Channel);

            if(newxref == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }

            db.Add(newxref);

            await command.RespondAsync($"Moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }


        [SlashCommand(Description = "Remove user from co-op (only works if the bot doesn't see them as joined)", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RemoveFromCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(RemoveFromCoopAutoComplete))] string useraccount) {

            await command.RespondAsync("Please wait...");
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Please use in a co-op channel");
                return;
            }

            var userid = Guid.Parse(useraccount.Split("|")[0]);

            var xref = await db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == userid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();

            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unabled to find user in co-op");
                return;
            }

            db.Remove(xref);
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Removed <@{xref.User.DiscordId}> from co-op");

        }

        [SlashCommand(Description = "Delete a contract channel (Please use this instead of deleting the channel in discord)", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task DeleteContract(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find contract, use only in contract channels.");
                return;
            }
            guildContract.DeletedChannel = true;
            await db.SaveChangesAsync();
            var channel = (SocketTextChannel)command.Channel;
            await channel.DeleteAsync();
        }

        [SlashCommand(Description = "Create a co-op with the selected contract for you")]
        public static async Task CreateCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, Words _words, IServiceProvider _provider, CoopStatusUpdater coopStatusUpdater, [SlashParam(AutocompleteHandler = typeof(CreateCoopContractAutoComplete))] string contractid) {
            await command.RespondAsync("Working...", ephemeral: true);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user is null) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find user");
                return;
            }
            var contract = await db.Contracts.FirstAsync(x => x.ID == contractid);
            var guildContract = await db.GuildContracts.FirstAsync(gc => gc.GuildID == command.GuildId && gc.Contract == contract);

            var subscriptionAccountsCount = user.EggIncAccounts.Where(x => x.SubscriptionLevel is not null).Count();

            var existContractXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();
            var activeXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();

            var dbguild = await db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            if(user.EggIncAccounts.Count == 1 || (contract.cc_only && subscriptionAccountsCount == 1)) {

                EggIncAccount subAccountBypass = null;
                if(contract.cc_only) {
                    subAccountBypass = user.EggIncAccounts.FirstOrDefault(x => x.SubscriptionLevel is not null);
                }

                var userList = new List<UserByAccount> { new UserByAccount {
                    Account = subAccountBypass ?? user.EggIncAccounts.First(),
                    User = user
                } };

                if(existContractXrefs is not null && existContractXrefs.Any(x => x.EggIncId == (subAccountBypass?.Id ?? user.EggIncAccounts?.First().Id))) {
                    await command.ModifyOriginalResponseAsync($"⚠️ERROR: You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: <#{existContractXrefs.First().Coop.DiscordChannelId}>");
                    return;
                }

                if(activeXrefs is not null && activeXrefs.Count(x => x.EggIncId == (subAccountBypass?.Id ?? user.EggIncAccounts?.First().Id)) >= 4) {
                    await command.ModifyOriginalResponseAsync($"⚠️ERROR: You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes.");
                    return;
                }

                var guild = _client.GetGuild(command.GuildId.Value);
                var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue);
                await command.ModifyOriginalResponseAsync("Done");
                await command.Channel.SendMessageAsync($"Co-op created {coop.Name} {PlayerGradeDetails.GetEmoji(coop.League)} for {command.User.Mention}");
            } else {
                var builder = new ComponentBuilder();
                var userList = user.EggIncAccounts;
                if(contract.cc_only) {
                    userList = userList.Where(x => x.SubscriptionLevel is not null).ToList();
                }

                foreach(var account in userList) {
                    _ = Emote.TryParse(PlayerGradeDetails.GetEmoji(account.LastGrade), out var emote);
                    builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"CreateCoopButton:{contractid}|{account.Id}", emote: emote);
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to create the co-op with."; x.Components = builder.Build(); });
            }
        }
        [ComponentCommand]
        public static async Task CreateCoopButton(SocketMessageComponent component, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            await component.UpdateAsync(x => { x.Content = "Working..."; x.Components = null; });
            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var contractid = data.Split("|")[0];
            var contract = await db.Contracts.FirstAsync(x => x.ID == contractid);
            var dbguild = await db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            var account = user.EggIncAccounts.First(x => x.Id == data.Split("|")[1]);

            var guildContract = await db.GuildContracts.FirstAsync(gc => gc.GuildID == user.GuildId && gc.Contract == contract);
            var existingXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();
            var activeXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();

            var userList = new List<UserByAccount> { new UserByAccount {
                    Account = account,
                    User = user
            } };

            if(existingXrefs.Any(x => x.EggIncId == account.Id)) {
                await component.Channel.SendMessageAsync($"{component.User.Mention} - ⚠️ERROR: You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: <#{existingXrefs.First().Coop.DiscordChannelId}>");
                return;
            }

            if(activeXrefs.Count(x => x.EggIncId == account.Id) >= 4) {
                await component.Channel.SendMessageAsync($"{component.User.Mention} - ⚠️ERROR: You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes.");
                return;
            }

            var guild = _client.GetGuild(component.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue);
            await component.ModifyOriginalResponseAsync(x => x.Content = "Done");
            await component.Channel.SendMessageAsync($"Co-op created {coop.Name} {PlayerGradeDetails.GetEmoji(coop.League)} for {component.User.Mention}");
        }

        private static Dictionary<ulong, SemaphoreSlim> startSemapohores = new Dictionary<ulong, SemaphoreSlim>();
        private static SemaphoreSlim dictionarySemaphore = new SemaphoreSlim(1);

        private static async Task<SemaphoreSlim> AwaitStartSemaphore(FauxCommand command) {
            await dictionarySemaphore.WaitAsync();
            SemaphoreSlim semaphore;
            if(startSemapohores.ContainsKey(command.ChannelId.Value)) {
                semaphore = startSemapohores[command.ChannelId.Value];
            } else {
                semaphore = new SemaphoreSlim(1);
                startSemapohores.Add(command.ChannelId.Value, semaphore);
            }
            dictionarySemaphore.Release();
            await semaphore.WaitAsync();
            return semaphore;
        }
    }
}


