using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Ei;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;
using static Ei.Contract.Types;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

using Exception = System.Exception;

namespace EGG9000.Bot.Commands {
    public static class ContractCommandsSlash {

        [SlashCommand(Description = "Fix a user getting full co-op error", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task FixFullCoopError(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ILogger logger, [SlashParam(AutocompleteHandler = typeof(UserAccountChannelSpecificAutoComplete))] string useraccount) {
            await command.DeferAsync();
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbuser is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user in co-op."); });
                return;
            }

            var coop = await db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel."); });
                return;
            }

            await _fixFullCoopError(command, db, _client, coopStatusUpdaterThreads, logger, dbuser, coop);
        }

        [SlashCommand(Description = "Fix for getting full co-op error")]
        public static async Task FixFullCoopError(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ILogger logger) {
            await command.DeferAsync();
            var coop = await db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel."); });
                return;
            }

            var dbuser = coop.UserCoopsXrefs.FirstOrDefault(x => x.User.DiscordId == command.User.Id)?.User;
            if(dbuser is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user in co-op."); });
            }

            await _fixFullCoopError(command, db, _client, coopStatusUpdaterThreads, logger, dbuser, coop);
        }

        private static async Task _fixFullCoopError(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ILogger logger, DBUser dbuser, Coop coop) {
            var status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name, coop.CreatorID);

            if(status is null) { //Safeguarding
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("The API is unresponsive, please try again in a minute or two."); });
                return;
            }

            var customEggs = await db.GetCustomEggsAsync();
            var details = new CoopDetails(coop, coop.Contract, coop.League, coop.UserCoopsXrefs.SelectMany(y => y.User.EggIncAccounts.Select(x => new UserWithBackup { Backup = x.Backup, User = y.User })).ToList(), customEggs, _client, status);

            if(details is null || details.CoopParticipants is null || details.CoopParticipants.Count == 0) { // Edge cases were throwing when details was null
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user in co-op. Co-op may be, or may have been public, or user may no longer be assigned to coop."); });
                return;
            }

            UserFarmDetails xref = null;
            try {
                xref = details.CoopParticipants.FirstOrDefault(x => x.DBUser.Id == dbuser.Id && x.EggsShipped == 0);
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user in co-op. Co-op may be, or may have been public, or user may no longer be assigned to coop."); });
                return;
            }

            if(xref is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user with zero production."); });
                return;
            }

            logger.LogInformation("Attempting to fix {user} in {coop} by creating temp co-op", dbuser.DiscordUsername, coop.Name);
            var contract = await db.Contracts.FirstAsync(x => x.ID == coop.ContractID);
            await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (PlayerGrade)coop.League, coopName: "test" + new Random().Next(10000), contract.Details.LengthSeconds, xref.EggIncId, coop.AnyLeague);

            await Task.Delay(TimeSpan.FromSeconds(2));
            status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name, coop.CreatorID);

            if(status?.Participants?.Count == contract.MaxUsers) {
                logger.LogInformation("Attempting to fix {user} in {coop} by submitting kick request", dbuser.DiscordUsername, coop.Name);
                var res3 = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
                    ClientVersion = 24,
                    ContractIdentifier = coop.ContractID,
                    CoopIdentifier = coop.Name,
                    PlayerIdentifier = xref.EggIncId, Reason = KickPlayerCoopRequest.Types.Reason.Private, RequestingUserId = coop.CreatorID
                }, coop.CreatorID);

                await Task.Delay(TimeSpan.FromSeconds(2));
                status = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);
            }


            if(status?.Participants?.Count < contract.MaxUsers) {
                logger.LogInformation("Successfully remove {user} from {coop}", dbuser.DiscordUsername, coop.Name);
                var guild = _client.Guilds.First(x => x.Id == coop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == coop.GuildId);
                var parentGuild = _client.Guilds.First(x => x.Id == dbguild.Id);
                await coopStatusUpdaterThreads.ProcessCoop(coop.Id, guild, parentGuild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);


                await command.Channel.SendMessageAsync($"Successfully removed <@{dbuser.DiscordId}> from co-op, they should be able to rejoin now.");
                await command.DeleteOriginalResponseAsync();
            } else {
                logger.LogInformation("Did not {user} from {coop}", dbuser.DiscordUsername, coop.Name);
                await command.ModifyOriginalResponseAsync($"Attempted to remove {command.User.Mention} from co-op, please check again in a few minutes.");
            }
        }

        [SlashCommand(Description = "Makes a co-op public", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task MakePublic(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find coop for channel {command.Channel.Name}"); });
                return;
            }

            if(string.IsNullOrEmpty(coop.CreatorID)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find creator for {command.Channel.Name}"); });
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
                await command.ModifyOriginalResponseAsync($"{coop.Name} is now public.");
            } else {
                await command.ModifyOriginalResponseAsync($"{coop.Name} should now be public.");
            }
        }

        [SlashCommand(Description = "Move a user to a different grade of coop", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task MoveGrade(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountChannelSpecificAutoComplete))] string useraccount,
            [SlashParam(AutocompleteHandler = typeof(MoveGradeAutoComplete))] uint newgrade) {
            await command.DeferAsync();
            var targetCoop = await db.Coops.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel."); });
                return;
            }

            var userid = useraccount.Split("|")[0];
            var guid = Guid.Parse(userid);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var dbGuild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == command.GuildId || x.OverflowServersJson.Contains(command.GuildId.ToString()));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            /* Find current coop xrefs */
            var xref = await db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == guid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user in co-op."); });
                return;
            }

            /* Find a new co-op */
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == targetCoop.GuildId && x.ContractID == targetCoop.ContractID && x.League == newgrade
                && x.CurrentUsers < x.MaxUsers && (int)x.Status > 2 && (int)x.Status < 13 && x.CoopEnds > DateTimeOffset.Now).ToListAsync();

            if(coops.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}"); });
                return;
            }

            Coop newCoop = null;
            var contract = await db.Contracts.FirstOrDefaultAsync(x => x.ID == targetCoop.ContractID);
            var customEggs = await db.GetCustomEggsAsync();
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, newgrade, usersWithBackups, customEggs, _client, coop.LastStatusUpdate);
                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}"); });
                return;
            }
            /* END Find a new co-op */


            //Add the grade role before moving them, to give them access to the header channel (if applicable)
            var currentGuild = _client.Guilds.FirstOrDefault(g => g.Id == command.GuildId);
            var discordUser = currentGuild.GetUser(dbuser.DiscordId);
            var gradeRole = dbGuild.ChannelDetails.FirstOrDefault(x => x.ChannelType == newgrade switch {
                1 => GuildChannelType.GradeC,
                2 => GuildChannelType.GradeB,
                3 => GuildChannelType.GradeA,
                4 => GuildChannelType.GradeAA,
                5 => GuildChannelType.GradeAAA,
                _ => default
            });
            if(gradeRole != null) {
                //Get the main guild
                var mainGuild = _client.Guilds.FirstOrDefault(g => g.Id == dbGuild.DiscordSeverId);
                var socketGradeRole = mainGuild.GetRole(gradeRole.Id);

                //Fetch a new backup so they don't lose access to this channel when role update happens
                var rawBackup = await ContractsAPI.FirstContact(account.Id);
                var customBackup = new CustomBackup(rawBackup.Backup, account?.Backup ?? null);

                if((uint)customBackup.Grade != newgrade) {
                    await command.ModifyOriginalResponseAsync(x => {
                        x.Content = ""; x.Embed = EmbedWarning($"A new backup was pulled, and the obtained grade " +
                        $"({PlayerGradeDetails.GetEmoji(customBackup.Grade)}) did not match the new target grade ({PlayerGradeDetails.GetEmoji(newgrade)}).\nTry forcing a new backup?");
                    });
                    return;
                }
                if(customBackup?.Farms is not null) {
                    account.Backup = customBackup;
                    dbuser.UpdateAccounts();
                }
                await mainGuild.GetUser(dbuser.DiscordId).AddRoleAsync(socketGradeRole.Id);
                if(mainGuild.Id != currentGuild.Id) {
                    var currentGuildSocketRole = currentGuild.Roles.FirstOrDefault(r => r.Name == socketGradeRole.Name);
                    if(currentGuildSocketRole != null) {
                        await discordUser.AddRoleAsync(currentGuildSocketRole.Id);
                    }
                }
            }

            /* MOVING TO NEW COOP */
            var coopChannel = newCoop.ThreadID != 0 ? _client.GetChannel(newCoop.ThreadID) : _client.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)command.Channel);
            if(newxref == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                return;
            }
            db.Add(newxref);
            db.Remove(xref);

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Removed {discordUser.Mention} ({account.Backup?.UserName}) from {((ITextChannel)command.Channel).Mention}, and moved to {((ITextChannel)coopChannel).Mention}"); });
            await db.SaveChangesAsync();
        }

        public enum FindCoopPrioritization {
            [Discord.Interactions.ChoiceDisplay("Low finish time (default)")] FinishTimeLow = 0,
            [Discord.Interactions.ChoiceDisplay("High finish time")] FinishTimeHigh = 1,
            [Discord.Interactions.ChoiceDisplay("Low player count")] LowPlayerCount = 2,
            [Discord.Interactions.ChoiceDisplay("High player count")] HighPlayerCount = 3,
            [Discord.Interactions.ChoiceDisplay("Needs high EB")] NeedsHighEB = 4,
            [Discord.Interactions.ChoiceDisplay("Has high EB")] HasHighEB = 5,
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

            if(contract.cc_only && !account.HasActiveSubscription()) {
                return new() { Response = PotentialCoopCode.NonUltra };
            } else if(existingCoop is not null) {
                return new() { Response = PotentialCoopCode.AlreadyAssigned, ReturnArgs = [existingCoop.Coop.ThreadID != 0 ? existingCoop.Coop.ThreadID.ToString() : existingCoop.Coop.DiscordChannelId.ToString()] };
            } else if(account.GetGrade() is PlayerGrade.GradeUnset) {
                return new() { Response = PotentialCoopCode.NoGrade };
            }

            var coops = await db.Coops.Include(c => c.Contract).Include(c => c.UserCoopsXrefs).Where(c =>
                c.Contract == contract
                && c.GuildId == guild.Id
                && (c.League == (uint)account.GetGrade() || c.AnyLeague)
                && c.CurrentUsers < c.MaxUsers
                && (int)c.Status > 2 && (int)c.Status < 13
                && c.CoopEnds > DateTimeOffset.Now
                && !c.PseudoExpired
                && c.ProjectedFinish > DateTimeOffset.Now
            ).ToListAsync();

            if(!coops.Any()) {
                return new() { Response = PotentialCoopCode.NoSpots1 };
            }

            _ = priority switch {
                FindCoopPrioritization.FinishTimeLow => coops = [.. coops.OrderBy(c => c.ProjectedFinish)],
                FindCoopPrioritization.FinishTimeHigh => coops = [.. coops.OrderByDescending(c => c.ProjectedFinish)],
                FindCoopPrioritization.LowPlayerCount => coops = [.. coops.OrderBy(c => c.UserCoopsXrefs.Count)],
                FindCoopPrioritization.HighPlayerCount => coops = [.. coops.OrderByDescending(c => c.UserCoopsXrefs.Count)],
                FindCoopPrioritization.NeedsHighEB => coops = [.. coops.OrderBy(c => c.UserCoopsXrefs.Max(x => x.SoulPower))],
                FindCoopPrioritization.HasHighEB => coops = [.. coops.OrderByDescending(c => c.UserCoopsXrefs.Max(x => x.SoulPower))],
                _ => coops = [.. coops.OrderBy(c => c.ProjectedFinish)],
            };

            var customEggs = await db.GetCustomEggsAsync();
            Coop newCoop = null;
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, (uint)account.GetGrade(), usersWithBackups, customEggs, _client, coop.LastStatusUpdate);

                if(coop.ThreadID == 0 || coop.ThreadArchived)
                    continue;

                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop is null) {
                return new() { Response = PotentialCoopCode.NoSpots2, ReturnArgs = [PlayerGradeDetails.GetEmoji(account.GetGrade()), contract.Name] };
            }

            return new() { Response = PotentialCoopCode.CoopFound, FoundCoop = newCoop };
        }

        [SlashCommand(Description = "Attempt to find a coop for a user, move user to said coop", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task FindCoopForUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount,
            [SlashParam(AutocompleteHandler = typeof(StaffContractAutoComplete))] string contractid, [SlashParam(Required = false)] FindCoopPrioritization priority = FindCoopPrioritization.FinishTimeLow) {
            await command.DeferAsync();
            var guildRef = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString()));
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var newCoopResponse = await FindPotentialCoopForUser(account, contract, guildRef, _client, db, priority);

            switch(newCoopResponse.Response) {
                case PotentialCoopCode.NonUltra:
                    await command.RespondAsync(content: "", embed: EmbedError("Non-subscribed account cannot be assigned to subscriber-only contract"));
                    return;
                case PotentialCoopCode.AlreadyAssigned:
                    await command.RespondAsync(content: "", embed: EmbedError($"User is already assigned a coop for contract {contract.Name}: <#{newCoopResponse.ReturnArgs[0]}>"));
                    return;
                case PotentialCoopCode.NoGrade:
                    await command.RespondAsync(content: "", embed: EmbedError("User does not have a grade set, and cannot be moved into a coop"));
                    return;
                case PotentialCoopCode.NoSpots1:
                case PotentialCoopCode.NoSpots2:
                    await command.RespondAsync(content: "", embed: EmbedError($"No open{(contract.cc_only ? "" : $" Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())}")} coop spots found for {contract.Name}"));
                    return;
            }

            var newCoop = newCoopResponse.FoundCoop;
            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = newCoop.ThreadID != 0 ? _client.GetChannel(newCoop.ThreadID) : _client.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)command.Channel);
            if(newxref == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server.\n**User was not moved to a coop.**" : "")}"));
                return;
            }
            db.Add(newxref);

            await command.RespondAsync($"Sucessfully moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Makes this co-op private", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task MakePrivate(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var name = new Regex(@"\w+").Match(command.Channel.Name.ToLower()).Value;
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find coop for this channel {command.Channel.Name}"); });
                return;
            }

            if(string.IsNullOrEmpty(coop.CreatorID)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find creator for {command.Channel.Name}"); });
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
                await command.ModifyOriginalResponseAsync($"{coop.Name} is now private.");
            } else {
                await command.ModifyOriginalResponseAsync($"{coop.Name} should now be private.");
            }
        }

        //[SlashCommand(Description = "Adds prefarmers from selected contract to this channel", AdminOnly = true)]
        //public static async Task AddPrefarmers(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync(content: "", embed: EmbedError($"Unable to find contract details, have you tagged a contract channel?"));
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

        //GradeAutoComplete

        [SlashCommand(Description = "Adds an outside co-op so you can track it's progress", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task AddCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client,
            [SlashParam(AutocompleteHandler = typeof(StaffContractAutoComplete))] string contract,
            [SlashParam] string coopname, [SlashParam(AutocompleteHandler = typeof(GradeAutoComplete))] uint grade,
            [SlashParam(Description = "Is the coop any-grade?", Required = false)] bool anygrade = false) {

            if(grade < 0 || grade > 5) {
                await command.RespondAsync(content: "", embed: EmbedError($"Specified grade value (`{grade}`) outside of bounds (1-5)."));
                return;
            }

            var dbContract = db.Contracts.FirstOrDefault(c => c.ID == contract);
            if(contract is null) {
                await command.RespondAsync(content: "", embed: EmbedError("Contract not found - please use dropdown menu."));
                return;
            }

            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.Contract.ID == dbContract.ID && x.GuildID == command.GuildId);
            if(guildContract == null) {
                await command.RespondAsync(content: "", embed: EmbedError("Unable to find contract details"));
                return;
            }

            var contractChannel = (await _client.GetChannelAsync(guildContract.DiscordChannelId) as SocketTextChannel);

            var status = await ContractsAPI.GetCoopStatusBot(guildContract.ContractID, coopname.ToLower());
            if(status != null && status.Success) {
                var coop = new Coop {
                    ContractID = guildContract.ContractID,
                    Created = DateTimeOffset.Now,
                    GuildId = guildContract.GuildID,
                    Name = coopname,
                    MaxUsers = guildContract.Contract.MaxUsers,
                    Status = CoopStatusEnum.WaitingOnThread,
                    League = grade,
                    AnyLeague = anygrade,
                    CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining),
                    AddedFromBackup = true
                };
                db.Coops.Add(coop);
                await db.SaveChangesAsync();
                await command.RespondAsync(content: "", embed: EmbedSuccess($"Co-op `{coopname}` added for {contractChannel.Mention}"));
                return;
            } else {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to find co-op details, double check co-op name (`{coopname}`) and correct contract channel ({contractChannel.Mention})."));
                return;
            }
        }


        //[SlashCommand(Description = "Start a new co-op")]
        //public static async Task NewCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname, [SlashParam] string grade) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync(content: "", embed: EmbedError("Unable to find contract details, have you tagged a contract channel?"));
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
        //        await command.RespondAsync(content: "", embed: EmbedError("Unable to find co-op details, double check co-op name ({coopname}) and correct contract channel ({((SocketTextChannel)contractchannel).Mention})."));
        //        return;
        //    }
        //}



        [SlashCommand(Description = "Silently moves to coop (if needed), followed by fixing reference", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task FixReference(FauxCommand command, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, DiscordSocketClient discord, ApplicationDbContext db,
            [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount,
            [SlashParam(Description = "(Usually not required) Egg Inc Name, will match partial name", Required = false)] string eggincname = "") {
            await command.DeferAsync();

            var coop = await db.Coops.Include(x => x.Contract).AsQueryable().FirstAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in co-op channels."); });
            }
            Guid userid;
            try {
                userid = Guid.Parse(useraccount.Split("|")[0]);
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse user account, please use the autocomplete dropdown."); });
                return;
            }
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == userid);
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];
            //Primary lookup
            var xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == command.Channel.Id || x.Coop.DiscordChannelId == command.Channel.Id) && !x.JoinedCoop);
            //Secondary lookup
            xref ??= await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == command.Channel.Id || x.Coop.DiscordChannelId == command.Channel.Id));

            var discordUser = discord.GetUser(dbuser.DiscordId);
            var coopChannel = coop.ThreadID != 0 ? discord.GetChannel(coop.ThreadID) : discord.GetChannel(coop.DiscordChannelId);
            //Lookups failed, a MoveToCoop is needed (run silently)
            if(xref == null) {
                var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)command.Channel, true);

                if(newxref == null) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"**User was not re-added to coop**:\n\nUnable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                    return;
                }
                db.Add(newxref);
                await db.SaveChangesAsync();
            }

            //Relookup xref
            xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == command.Channel.Id || x.Coop.DiscordChannelId == command.Channel.Id) && !x.JoinedCoop);
            //Secondary relookup
            xref ??= await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == command.Channel.Id || x.Coop.DiscordChannelId == command.Channel.Id));

            //Failsafe
            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Even after a `MoveToCoop`, an Xref could not be found for this user. Try again?"); });
                return;
            }

            var foundEIName = account.Backup?.UserName ?? account.Name;
            if(string.IsNullOrEmpty(foundEIName) && string.IsNullOrEmpty(eggincname)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find user's Egg Inc name from backup. Please specify the username via the command argument."); });
                return;
            }

            var name = string.IsNullOrEmpty(eggincname) ? account.Backup?.UserName : eggincname;
            var t = xref.Coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserName.ToLower().Contains(name.ToLower()));
            if(t == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user in co-op. You can use a partial in-game name."); });
                return;
            }

            xref.FixedUserName = t.UserName;
            await db.SaveChangesAsync();

            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            var guild = discord.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
            var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
            var parentGuild = discord.Guilds.First(x => x.Id == dbguild.Id);
            await coopStatusUpdaterThreads.ProcessCoop(targetCoop.Id, guild, parentGuild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);


            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Fixed {discordUser.Mention}'s reference."); });
        }

        [SlashCommand(Description = "Move a user to a co-op.", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task MoveToCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount,
            [SlashParam(AutocompleteHandler = typeof(MoveToCoopCoopNameAutoComplete))] string coopid, [SlashParam(Required = false, Description = "If true, will not ping user in coop channel")] bool silent = false) {
            await command.DeferAsync();

            Guid coopId;
            try {
                coopId = Guid.Parse(coopid);
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse coop, please use the autocomplete dropdown."); });
                return;
            }
            var coop = await db.Coops.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == coopId);
            if(coop is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse coop, please use the autocomplete dropdown."); });
                return;
            }

            Guid userid;
            try {
                userid = Guid.Parse(useraccount.Split("|")[0]);
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse user account, please use the autocomplete dropdown."); });
                return;
            }
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == userid);
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = coop.ThreadID != 0 ? _client.GetChannel(coop.ThreadID) : _client.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)command.Channel, silent);

            if(newxref == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                return;
            }
            db.Add(newxref);

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}"); });
            await db.SaveChangesAsync();
        }


        [SlashCommand(Description = "Remove user from co-op (only works if the bot doesn't see them as joined)", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RemoveFromCoop(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(RemoveFromCoopAutoComplete))] string useraccount) {
            await command.DeferAsync();
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please use in a co-op channel"); });
                return;
            }

            var userid = Guid.Parse(useraccount.Split("|")[0]);
            var xref = await db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == userid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            var username = xref.User.EggIncAccounts.FirstOrDefault(x => x.Id == xref.EggIncId)?.Backup?.UserName ?? "(No Name)";

            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user in co-op"); });
                return;
            }

            db.Remove(xref);
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Removed <@{xref.User.DiscordId}> ({username}) from co-op");

        }

        [SlashCommand(Description = "Delete a contract channel (Please use this instead of deleting the channel in discord)", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task DeleteContract(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, ILogger _logger) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync(content: "", embed: EmbedError("Unable to find contract, use only in contract channels."));
                return;
            }
            var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == guildContract.GuildID);
            _logger.LogInformation("Deleting header channels for {} because the contract channel was deleted", guildContract.Contract.Name);
            await dbGuild.DeleteCoopThreadHeadersAsync(_client, guildContract.Contract, _logger);

            guildContract.DeletedChannel = true;
            await db.SaveChangesAsync();
            var channel = (SocketTextChannel)command.Channel;
            await channel.DeleteAsync();
        }

        [SlashCommand(Description = "Create a co-op with the selected contract for you")]
        public static async Task CreateCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [SlashParam(AutocompleteHandler = typeof(CreateCoopContractAutoComplete))] string contractid) {
            await command.DeferAsync();
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user"); });
                return;
            }
            var contract = await db.Contracts.FirstOrDefaultAsync(x => x.ID == contractid);
            if(contract is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find contract from input. Please select a choice from the list"); });
                return;
            }
            var guildContract = await db.GuildContracts.FirstAsync(gc => gc.GuildID == command.GuildId && gc.Contract == contract);

            var subscriptionAccountsCount = user.EggIncAccounts.Where(x => x.HasActiveSubscription()).Count();

            var existContractXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();
            var activeXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();

            var dbguild = await db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            if(user.EggIncAccounts.Count == 1 || (contract.cc_only && subscriptionAccountsCount == 1)) {

                EggIncAccount subAccountBypass = null;
                if(contract.cc_only) {
                    subAccountBypass = user.EggIncAccounts.FirstOrDefault(x => x.HasActiveSubscription());
                }

                var userList = new List<UserByAccount> { new() {
                    Account = subAccountBypass ?? user.EggIncAccounts.First(),
                    User = user
                } };

                var accountHasUltra = (subAccountBypass ?? user.EggIncAccounts.First()).HasActiveSubscription();

                if(existContractXrefs is not null && existContractXrefs.Any(x => x.EggIncId == (subAccountBypass?.Id ?? user.EggIncAccounts?.First().Id))) {
                    var xref = existContractXrefs.First();
                    await command.ModifyOriginalResponseAsync(x => {
                        x.Content = ""; x.Embed = EmbedError($"You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: " +
                        $"<#{(xref.Coop.ThreadID != 0 ? xref.Coop.ThreadID : xref.Coop.DiscordChannelId)}>");
                    });
                    return;
                }

                if(activeXrefs is not null && activeXrefs.Count(x => x.EggIncId == (subAccountBypass?.Id ?? user.EggIncAccounts?.First().Id)) >= 4) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes."); });
                    return;
                }

                var guild = _client.GetGuild(command.GuildId.Value);
                var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue, accountHasUltra); //Allow all grades 
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Co-op created (`{coop.Name}` - {PlayerGradeDetails.GetEmoji(coop.League)}) for {command.User.Mention}"); });
                //await command.Channel.SendMessageAsync(text: "", embed: EmbedSuccess($"Co-op created (`{coop.Name}` - {PlayerGradeDetails.GetEmoji(coop.League)}) for {command.User.Mention}"));
            } else {
                var builder = new ComponentBuilder();
                var userList = user.EggIncAccounts;
                if(contract.cc_only) {
                    userList = userList.Where(x => x.HasActiveSubscription()).ToList();
                }

                foreach(var account in userList) {
                    _ = Emote.TryParse(PlayerGradeDetails.GetEmoji(account.LastGrade), out var emote);
                    builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"CreateCoopButton:{contractid}|{account.Id}|{user.DiscordId}", emote: emote);
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to create the co-op with."; x.Components = builder.Build(); });
            }
        }
        [ComponentCommand]
        public static async Task CreateCoopButton(SocketMessageComponent component, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            var dataObjs = data.Split("|");
            var originalUserId = ulong.Parse(dataObjs[2]);

            if(component.User.Id != originalUserId) {
                await component.RespondAsync(embed: EmbedError("This wasn't yours to run - don't click others' commands!"), ephemeral: true);
                return;
            }

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Working..."); x.Components = null; });
            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var contractid = data.Split("|")[0];
            var contract = await db.Contracts.FirstAsync(x => x.ID == contractid);
            var dbguild = await db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            var account = user.EggIncAccounts.First(x => x.Id == data.Split("|")[1]);

            var guildContract = await db.GuildContracts.FirstAsync(gc => gc.GuildID == user.GuildId && gc.Contract == contract);
            var existingXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();
            var activeXrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.Now).ToListAsync();

            var userList = new List<UserByAccount> { new() {
                    Account = account,
                    User = user
            } };

            var accountHasUltra = account.HasActiveSubscription();

            if(existingXrefs.Any(x => x.EggIncId == account.Id)) {
                var xref = existingXrefs.First();
                await component.UpdateAsync(x => {
                    x.Content = ""; x.Embed = EmbedError($"You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: " +
                    $"<#{(xref.Coop.ThreadID != 0 ? xref.Coop.ThreadID : xref.Coop.DiscordChannelId)}>");
                });
                return;
            }

            if(activeXrefs.Count(x => x.EggIncId == account.Id) >= 4) {
                await component.UpdateAsync(x => { x.Content = ""; x.Embed = EmbedError($"You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes."); });
                return;
            }

            var guild = _client.GetGuild(component.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue, accountHasUltra); //Allow all grades
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Co-op created (`{coop.Name}` - {PlayerGradeDetails.GetEmoji(coop.League)}) for {component.User.Mention}"); });
            //await component.Channel.SendMessageAsync(text: "", embed: EmbedSuccess($"Co-op created (`{coop.Name}` - {PlayerGradeDetails.GetEmoji(coop.League)}) for {component.User.Mention}"));
        }

        [ComponentCommand]
        public static async Task FindCoopSpot(SocketMessageComponent component, ApplicationDbContext db) {
            await component.RespondAsync(text: "", embed: EmbedInProgress("Working..."), ephemeral: true);
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            if(dbUser is null || dbUser.GuildId != component.GuildId) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Could not find your record - are you registered correctly?"); });
                return;
            }
            if(dbUser.TempDisabled) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Looks like you are currently disabled, and therefore cannot be assigned to a co-op."); });
                return;
            }
            var dbguild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == component.GuildId);
            if(dbguild is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"This command must be used in a server.\n\nCome to think of it, how did you even do this?"); });
                return;
            }
            var guildContract = await db.GuildContracts.Include(gc => gc.Contract).FirstOrDefaultAsync(c => c.GuildID == component.GuildId && c.DiscordChannelId == component.ChannelId);
            if(guildContract is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"This command must be used in a contract channel.\n\nCome to think of it, how did you even do this?"); });
                return;
            }
            if(DateTimeOffset.Now >= guildContract.Contract.GoodUntil) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"This contract has expired, and coops can no longer be joined."); });
                return;
            }

            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == guildContract.ContractID);
            if(contract is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"`GuildContract` was found, but the base `Contract` was not ..."); });
                return;
            }

            var eligibleAccounts = dbUser.EggIncAccounts.Where(a => a.Backup?.SoulEggs > 1000 && (!contract.cc_only || a.HasActiveSubscription())).ToList();
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
        public static async Task FindCoopSpotForAccount(SocketMessageComponent component, DiscordSocketClient _client, [ComponentData] string data, ApplicationDbContext db) {
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
                    //var createNewCoopComponent = new ComponentBuilder().WithButton("Create New Coop", customId: $"NoSpotsCreateCoop:{guildContract.ContractID}|{account.Id}", emote: emote).Build();
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open{(contract.cc_only ? "" : $" Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())}")} coop spots found for {contract.Name}"); /*x.Components = createNewCoopComponent;*/ });
                    return;
                default:
                    var customEggs = await db.GetCustomEggsAsync();
                    var coop = newCoopResponse.FoundCoop;
                    var users = coop.UserCoopsXrefs.Select(c => c.User).ToList().SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
                    var statusReponse = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name);
                    if(statusReponse is null || !statusReponse.Success || statusReponse.Contributors is null) {
                        statusReponse = coop.LastStatusUpdate; //Fallback to last known status
                    }
                    var coopDetails = new CoopDetails(coop, coop.Contract, coop.League, users, customEggs, _client, statusReponse);
                    var highestEB = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                    var league = (int)coop.League;

                    _ = Emote.TryParse(PlayerGradeDetails.GetEmoji(coop.League), out var coopGradeEmoji);
                    var embedBuilder = new EmbedBuilder()
                        .WithColor(Color.Green)
                        .WithAuthor(
                            new EmbedAuthorBuilder()
                                .WithName("Potential Coop Found")
                                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
                        .AddField("Grade", coopGradeEmoji + (coop.AnyLeague ? " (Any Grade) <:ultra:1131045418319495369>" : ""), inline: true)
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
                            } catch(OverflowException) { }
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

            var coopChannel = coop.ThreadID != 0 ? _client.GetChannel(coop.ThreadID) : _client.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", db, discordUser, dbuser, (SocketThreadChannel)coopChannel, null); //The "commandChannel" here is intentionally nulled to prevent sending messages in Contract channels

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

            var userList = new List<UserByAccount> { new() {
                Account = account,
                User = user
            }};

            if(DateTimeOffset.Now >= guildContract.Contract.GoodUntil) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"This contract has expired, and no new co-ops can be formed."); });
                return;
            }

            if(existingXrefs.Any(x => x.EggIncId == account.Id)) {
                var xref = existingXrefs.First();
                await component.ModifyOriginalResponseAsync(x => {
                    x.Content = ""; x.Components = null; x.Embed = EmbedError($"You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: " +
                    $"<#{(xref.Coop.ThreadID != 0 ? xref.Coop.ThreadID : xref.Coop.DiscordChannelId)}>");
                });
                return;
            }

            if(activeXrefs.Count(x => x.EggIncId == account.Id) >= 4) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError("You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes."); });
                return;
            }

            var guild = _client.GetGuild(component.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue, true); //Allow all grades
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedSuccess($"Co-op `{coop.Name}` {PlayerGradeDetails.GetEmoji(coop.League)} created for <#{component.ChannelId}>"); });
        }
    }
}