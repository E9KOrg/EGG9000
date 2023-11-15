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
using static EGG9000.Bot.Automated.Coops.CoopStatusUpdater;

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

            var xref = details.CoopParticipants.FirstOrDefault(x => x.DBUser.Id == dbuser.Id && x.EggsShipped == 0);

            if(xref is null) {
                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Unable to locate user with zero production.");
                return;
            }

            logger.LogInformation("Attempting to fix {user} in {coop} by creating temp co-op", dbuser.DiscordUsername, coop.Name);
            var contract = await db.Contracts.FirstAsync(x => x.ID == coop.ContractID);
            await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (PlayerGrade)coop.League, new Coop { Name = "test" + new Random().Next(10000), ContractID = coop.ContractID }, contract.Details.LengthSeconds, xref.EggIncId);

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

        [SlashCommand(Description = "Makes a co-op public", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
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

        public enum PotentialCoopCode {
            NonUltra = 0,
            AlreadyAssigned = 1,
            NoGrade = 2,
            NoSpots1 = 3,
            NoSpots2 = 4,
            CoopFound = 5,
        };

        public class PotentialCoopResponse {
            public PotentialCoopCode Response { get; set; } = PotentialCoopCode.CoopFound;
            public List<string> ReturnArgs { get; set; } = null;
            public Coop FoundCoop { get; set; } = null;
        }

        public static async Task<PotentialCoopResponse> FindPotentialCoopForUser(EggIncAccount account, EGG9000.Common.Database.Entities.Contract contract, Guild guild, DiscordSocketClient _client, ApplicationDbContext db, FindCoopPrioritization priority = FindCoopPrioritization.FinishTimeLow) {

            var userXrefs = await db.UserCoopXrefs.Include(x => x.Coop).ThenInclude(x => x.Contract).Include(x => x.Coop).Where(x => x.EggIncId == account.Id).ToListAsync();
            var existingCoop = userXrefs.FirstOrDefault(r => r.Coop.Contract == contract && (int)r.Coop.Status > 2 && (int)r.Coop.Status < 13 && r.Coop.CoopEnds > DateTimeOffset.Now);

            if(contract.cc_only && account.SubscriptionLevel is null) {
                return new PotentialCoopResponse { Response = PotentialCoopCode.NonUltra };
            } else if(existingCoop is not null) {
                return new PotentialCoopResponse { Response = PotentialCoopCode.AlreadyAssigned, ReturnArgs = new() { existingCoop.Coop.DiscordChannelId.ToString() } };
            } else if(account.GetGrade() is PlayerGrade.GradeUnset) {
                return new PotentialCoopResponse { Response = PotentialCoopCode.NoGrade };
            }

            var coops = await db.Coops.Include(c => c.Contract).Include(c => c.UserCoopsXrefs).Where(c => c.Contract == contract && c.GuildId == guild.Id && c.League == (uint)account.GetGrade()
             && c.CurrentUsers < c.MaxUsers && (int)c.Status > 2 && (int)c.Status < 13 && c.CoopEnds > DateTimeOffset.Now && c.ProjectedFinish > DateTimeOffset.Now).ToListAsync();

            if(!coops.Any()) {
                return new PotentialCoopResponse { Response = PotentialCoopCode.NoSpots1 };
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
                return new PotentialCoopResponse { Response = PotentialCoopCode.NoSpots2, ReturnArgs = new() { PlayerGradeDetails.GetEmoji(account.GetGrade()), contract.Name } };
            }

            return new PotentialCoopResponse { Response = PotentialCoopCode.CoopFound, FoundCoop = newCoop };
        }

        [SlashCommand(Description="Attempt to find a coop for a user, move user to said coop", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task FindCoopForUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, 
            [SlashParam(AutocompleteHandler = typeof(StaffContractAutoComplete))] string contractid, [SlashParam(Required = false)]FindCoopPrioritization priority = FindCoopPrioritization.FinishTimeLow) {
            await command.DeferAsync();
            var guildRef = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString())); 
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var newCoopResponse = await FindPotentialCoopForUser(account, contract, guildRef, _client, db);

            switch(newCoopResponse.Response) {
                case PotentialCoopCode.NonUltra:
                    await command.RespondAsync($"⚠️ERROR: Non-subscribed account cannot be assigned to subscriber-only contract");
                    return;
                case PotentialCoopCode.AlreadyAssigned:
                    await command.RespondAsync($"⚠️ERROR: User is already assigned a coop for contract {contract.Name}: <#{newCoopResponse.ReturnArgs[0]}>");
                    return;
                case PotentialCoopCode.NoGrade:
                    await command.RespondAsync($"⚠️ERROR: User does not have a grade set, and cannot be moved into a coop");
                    return;
                case PotentialCoopCode.NoSpots1:
                case PotentialCoopCode.NoSpots2:
                    await command.RespondAsync($"⚠️ERROR: No open Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())} coop spots found for {contract.Name}");
                    return;
            }

            var newCoop = newCoopResponse.FoundCoop;
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
            } catch(Exception) {
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

        [ComponentCommand]
        public static async Task FindCoopSpot(SocketMessageComponent component, DiscordSocketClient _client, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {

            //Set back to ephemeral: true before prod
            await component.RespondAsync(embed: EmbedInProgress("Working...") , ephemeral: true);
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            if(dbUser is null || dbUser.GuildId != component.GuildId) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Could not find your record - are you registered correctly?"); });
                return;
            }
            var dbguild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == component.GuildId);
            if(dbguild is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"This command must be used in a server.\n\nCome to think of it, how did you even do this?"); });
                return;
            }
            var guildContract = await db.GuildContracts.FirstOrDefaultAsync(c => c.GuildID == component.GuildId && c.DiscordChannelId == component.ChannelId);
            if(guildContract is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"This command must be used in a contract channel.\n\nCome to think of it, how did you even do this?"); });
                return;
            }

            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == guildContract.ContractID);
            if(contract is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"`GuildContract` was found, but the base `Contract` was not ..."); });
                return;
            }

            var eligibleAccounts = dbUser.EggIncAccounts.Where(a => a.Backup?.SoulEggs > 1000 && (!contract.cc_only || a.SubscriptionLevel is not null)).ToList();

            if(eligibleAccounts.Count < 1) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You have no accounts that are eligible for this contract."); });
                return;
            }

            var builder = new ComponentBuilder();
            foreach(var account in eligibleAccounts) {
                _ = Emote.TryParse(PlayerGradeDetails.GetEmoji(account.LastGrade), out var emote);
                builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"FindCoopSpotForAccount:{dbUser.EggIncAccounts.IndexOf(account)}", emote: emote);
            }
            await component.ModifyOriginalResponseAsync(x => { x.Embed = null; x.Content = $"Select an account: "; x.Components = builder.Build(); });
        }

        [ComponentCommand]
        public static async Task FindCoopSpotForAccount(SocketMessageComponent component, DiscordSocketClient _client, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Coops are being filtered. This may take a few seconds."); x.Components = null; });
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            var dbguild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == component.GuildId);
            var guildContract = await db.GuildContracts.FirstOrDefaultAsync(c => c.GuildID == component.GuildId && c.DiscordChannelId == component.ChannelId);
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == guildContract.ContractID);
            var accountIndex = int.Parse(data.Split("|")[0]);
            var account = dbUser.EggIncAccounts[accountIndex];

            var newCoopResponse = await FindPotentialCoopForUser(account, contract, dbguild, _client, db);

            switch(newCoopResponse.Response) {
                case PotentialCoopCode.NonUltra:
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Non-subscribed account cannot be assigned to subscriber-only contract"); x.Components = null; });
                    return;
                case PotentialCoopCode.AlreadyAssigned:
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You already have an assigned coop for <#{component.ChannelId}>: <#{newCoopResponse.ReturnArgs[0]}>"); x.Components = null; });
                    return;
                case PotentialCoopCode.NoGrade:
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You do not have a grade set, and thus cannot be moved into a coop"); x.Components = null; });
                    return;
                case PotentialCoopCode.NoSpots1:
                case PotentialCoopCode.NoSpots2:
                    _ = Emote.TryParse(PlayerGradeDetails.GetEmoji(account.LastGrade), out var emote);
                    var createNewCoopComponent = new ComponentBuilder().WithButton("Create New Coop", customId: $"NoSpotsCreateCoop:{guildContract.ContractID}|{account.Id}", emote: emote).Build();
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())} coop spots found for {contract.Name}"); x.Components = createNewCoopComponent; });
                    return;
                default:
                    var coop = newCoopResponse.FoundCoop;
                    var coopChannel = _client.Guilds.FirstOrDefault(g => g.Id == component.GuildId).GetChannel(coop.DiscordChannelId);
                    var users = coop.UserCoopsXrefs.Select(c => c.User).ToList().SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
                    var statusReponse = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);
                    var coopDetails = new CoopDetails(coop, coop.Contract, coop.League, users, _client, statusReponse);
                    var highestEB = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                    var league = (int)coop.League;

                    var embedBuilder = new EmbedBuilder()
                        .WithColor(Color.Green)
                        .WithAuthor(
                            new EmbedAuthorBuilder()
                                .WithName("Potential Coop Found")
                                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
                        .AddField("Users Assigned", $"{coop.CurrentUsers}/{contract.MaxUsers}", inline: true);

                    if(highestEB is not null) embedBuilder.AddField("Highest EB", $"`{highestEB.Backup?.EarningsBonus.ToEggString()}`" ?? "Unknown", inline: false);

                    var targetAmount = coop.Contract.Details.GetGoals(league).Max(x => x.TargetAmount);
                    var amountWithOffline = coopDetails.CoopParticipants.Where(x => x.CoopStatus is not null).Sum(x => x.EggsShipped + x.OfflineEggs);
                    var remainingAmount = targetAmount - amountWithOffline;
                    var totalRate = statusReponse.Participants.Sum(x => x.ContributionRate);
                    var timeToComplete = GetTimeRemaining(targetAmount, totalRate, amountWithOffline);

                    if(remainingAmount > 0) {
                        var remainingTime = remainingAmount / totalRate;
                        if(remainingTime < TimeSpan.MaxValue.TotalSeconds) {
                            try {
                                var timeSpan = TimeSpan.FromSeconds(remainingTime);
                                embedBuilder.AddField("Time To Complete", GetTimeRemaining(targetAmount, totalRate, amountWithOffline) ?? "Unknown", inline: false);
                            } catch(OverflowException) {}
                        } else {
                            embedBuilder.AddField("Time To Complete", "**\u221E**", inline: false);
                            embedBuilder.AddField("\u17B5", "\u17B5");
                        }
                    } else if(!statusReponse.Finished()) {
                        embedBuilder.AddField("Time To Complete", "Once everyone checks in", inline: false);
                    }

                    var acceptComponent = new ComponentBuilder().WithButton("Accept Offer", customId: $"AcceptCoopOffer:{dbUser.EggIncAccounts.IndexOf(account)}|{contract.ID}|{coop.Name}").Build();
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = acceptComponent; x.Embed = embedBuilder.Build(); });
                    break;
            }
        }

        [ComponentCommand]
        public static async Task AcceptCoopOffer(SocketMessageComponent component, DiscordSocketClient _client, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Attempting to move you to the coop. This may take a few seconds."); x.Components = null; });
            var discordUser = component.User;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(u => u.DiscordId == component.User.Id);
            var accountIndex = int.Parse(data.Split("|")[0]);
            var account = dbuser.EggIncAccounts[accountIndex];

            var contractId = data.Split("|")[1];
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractId);
            if(contract is null) return;

            var coopId = data.Split("|")[2];
            var coop = await db.Coops.FirstOrDefaultAsync(c => c.GuildId == dbuser.GuildId && c.Name == coopId);
            if(coop is null) return;

            var coopChannel = _client.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", discordUser, dbuser, (SocketTextChannel)coopChannel, null); //The "commandChannel" here is intentionally nulled to prevent sending messages in Contract channels

            if(newxref == null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"Unable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                return;
            }
            db.Add(newxref);

            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedSuccess($"Moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}"); });
            await db.SaveChangesAsync();
        }

        [ComponentCommand]
        public static async Task NoSpotsCreateCoop(SocketMessageComponent component, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Coop is being created. This may take a few seconds."); x.Components = null; });
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
            }};

            if(DateTimeOffset.Now >= guildContract.Contract.GoodUntil) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"This contract has expired, and no new co-ops can be formed."); });
                return;
            }

            if(existingXrefs.Any(x => x.EggIncId == account.Id)) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: <#{existingXrefs.First().Coop.DiscordChannelId}>"); });
                return;
            }

            if(activeXrefs.Count(x => x.EggIncId == account.Id) >= 4) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError("You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes."); });
                return;
            }

            var guild = _client.GetGuild(component.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedSuccess($"Co-op `{coop.Name}` {PlayerGradeDetails.GetEmoji(coop.League)} created for <#{component.ChannelId}>"); });
        }

        public static Embed EmbedInProgress(string text) {
            return new EmbedBuilder().WithColor(Color.Blue).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Please wait...").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedSuccess(string text) {
            return new EmbedBuilder().WithColor(Color.Green).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Success").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedWarning(string warningText) {
            return new EmbedBuilder().WithColor(Color.LightOrange).WithDescription(warningText).WithAuthor(new EmbedAuthorBuilder().WithName("Warning").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedError(string errorText) {
            return new EmbedBuilder().WithColor(Color.Red).WithDescription(errorText).WithAuthor(new EmbedAuthorBuilder().WithName("Error").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

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