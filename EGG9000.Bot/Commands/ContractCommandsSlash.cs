using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Automated.Coops;
using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Interactions;
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
using System.Text;
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

        internal static async Task _fixFullCoopError(SocketInteraction command, ApplicationDbContext db, DiscordHostedService _client, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ILogger logger, DBUser dbuser, Coop coop) {
            var status = await EggIncApi.GetCoopStatus(coop.ContractID, coop.Name, coop.CreatorID);

            if(status is null) { //Safeguarding
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("The API is unresponsive, please try again in a minute or two."); });
                return;
            }

            var customEggs = await db.GetCustomEggsAsync();
            var details = new CoopDetails(coop, coop.Contract, coop.League, coop.UserCoopsXrefs.SelectMany(y => y.User.EggIncAccounts.Select(x => new UserWithBackup { Backup = x.Backup, User = y.User })).ToList(), customEggs, _client.Gateway, status);

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
            status = await EggIncApi.GetCoopStatus(coop.ContractID, coop.Name, coop.CreatorID);

            if(status?.Participants?.Count == contract.MaxUsers) {
                logger.LogInformation("Attempting to fix {user} in {coop} by submitting kick request", dbuser.DiscordUsername, coop.Name);
                var res3 = await EggIncApi.Send(new Ei.KickPlayerCoopRequest {
                    ClientVersion = 24,
                    ContractIdentifier = coop.ContractID,
                    CoopIdentifier = coop.Name,
                    PlayerIdentifier = xref.EggIncId, Reason = KickPlayerCoopRequest.Types.Reason.Private, RequestingUserId = coop.CreatorID
                }, coop.CreatorID);

                await Task.Delay(TimeSpan.FromSeconds(2));
                status = await EggIncApi.GetCoopStatus(coop.ContractID, coop.Name);
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
            var existingCoop = userXrefs.FirstOrDefault(r => r.Coop.Contract == contract && (int)r.Coop.Status > 2 && (int)r.Coop.Status < 13 && r.Coop.CoopEnds > DateTimeOffset.UtcNow);

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
                && c.CoopEnds > DateTimeOffset.UtcNow
                && !c.PseudoExpired
                && c.ProjectedFinish > DateTimeOffset.UtcNow
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
    }

    public class ContractModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient gateway, DiscordHostedService client, Words words, IServiceProvider provider, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, CoopAssignmentLookup lookup, ILogger<ContractModule> logger) : E9KModuleBase(dbFactory) {
        private readonly DiscordSocketClient _gateway = gateway;
        private readonly DiscordHostedService _client = client;
        private readonly Words _words = words;
        private readonly IServiceProvider _provider = provider;
        private readonly ThreadsCoopStatusUpdater _coopStatusUpdaterThreads = coopStatusUpdaterThreads;
        private readonly CoopAssignmentLookup _lookup = lookup;
        private readonly ILogger<ContractModule> _logger = logger;

        [SlashCommand("fixfullcooperror", "Fix for getting full co-op error")]
        public async Task FixFullCoopError() {
            await Context.Interaction.DeferAsync();
            var coop = await Db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel."); });
                return;
            }

            var dbuser = coop.UserCoopsXrefs.FirstOrDefault(x => x.User.DiscordId == Context.User.Id)?.User;
            if(dbuser is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user in co-op."); });
            }

            await ContractCommandsSlash._fixFullCoopError(Context.Interaction, Db, _client, _coopStatusUpdaterThreads, _logger, dbuser, coop);
        }

        [SlashCommand("makepublic", "Makes a co-op public")]
        [DefaultMemberPermissions(GuildPermission.ManageChannels)]
        public async Task MakePublic() {
            await Context.Interaction.DeferAsync();
            var coop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find coop for channel {Context.Channel.Name}"); });
                return;
            }

            if(string.IsNullOrEmpty(coop.CreatorID)) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find creator for {Context.Channel.Name}"); });
                return;
            }

            var response = await EggIncApi.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = EggIncApi.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = true,
                RequestingUserId = coop.CreatorID
            }, coop.CreatorID);

            if(response.Success) {
                await Context.Interaction.ModifyOriginalResponseAsync($"{coop.Name} is now public.");
            } else {
                await Context.Interaction.ModifyOriginalResponseAsync($"{coop.Name} should now be public.");
            }
        }

        [SlashCommand("movegrade", "Move a user to a different grade of coop")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task MoveGrade([Summary("useraccount")][Autocomplete(typeof(UserAccountChannelSpecificAutoComplete))] string useraccount,
            [Summary("newgrade")][Autocomplete(typeof(MoveGradeAutoComplete))] uint newgrade) {
            await Context.Interaction.DeferAsync();
            var targetCoop = await Db.Coops.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(targetCoop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel."); });
                return;
            }

            var userid = useraccount.Split("|")[0];
            var guid = Guid.Parse(userid);
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var dbGuild = await Db.Guilds.FirstOrDefaultAsync(x => x.Id == Context.Interaction.GuildId || x.OverflowServersJson.Contains(Context.Interaction.GuildId.ToString()));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var xref = await Db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == guid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            if(xref == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user in co-op."); });
                return;
            }

            var coops = await Db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == targetCoop.GuildId && x.ContractID == targetCoop.ContractID && x.League == newgrade
                && x.CurrentUsers < x.MaxUsers && (int)x.Status > 2 && (int)x.Status < 13 && x.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();

            if(coops.Count == 0) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}"); });
                return;
            }

            Coop newCoop = null;
            var contract = await Db.Contracts.FirstOrDefaultAsync(x => x.ID == targetCoop.ContractID);
            var customEggs = await Db.GetCustomEggsAsync();
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await Db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, newgrade, usersWithBackups, customEggs, _gateway, coop.LastStatusUpdate);
                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}"); });
                return;
            }

            //Add the grade role before moving them, to give them access to the header channel (if applicable)
            var currentGuild = _gateway.Guilds.FirstOrDefault(g => g.Id == Context.Interaction.GuildId);
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
                var mainGuild = _gateway.Guilds.FirstOrDefault(g => g.Id == dbGuild.DiscordSeverId);
                var socketGradeRole = mainGuild.GetRole(gradeRole.Id);

                //Fetch a new backup so they don't lose access to this channel when role update happens
                var rawBackup = await EggIncApi.FirstContact(account.Id);
                var customBackup = new CustomBackup(rawBackup.Backup, await Db.CachedEiContractsAsync(), account?.Backup ?? null);
                var pulledGrade = customBackup.GetMostRecentContractGrade().Grade;

                if((uint)pulledGrade != newgrade) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => {
                        x.Content = ""; x.Embed = EmbedWarning($"A new backup was pulled, and the obtained grade " +
                        $"({PlayerGradeDetails.GetEmoji(pulledGrade)}) did not match the new target grade ({PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)newgrade)}).\nTry forcing a new backup?");
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

            var coopChannel = newCoop.ThreadID != 0 ? _gateway.GetChannel(newCoop.ThreadID) : _gateway.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", Db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)Context.Channel);
            if(newxref == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                return;
            }
            Db.Add(newxref);
            Db.Remove(xref);

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Removed {discordUser.Mention} ({account.Backup?.UserName}) from {((ITextChannel)Context.Channel).Mention}, and moved to {((ITextChannel)coopChannel).Mention}"); });
            await Db.SaveChangesAsync();
        }

        [SlashCommand("findcoopforuser", "Attempt to find a coop for a user, move user to said coop")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task FindCoopForUser([Summary("useraccount")][Autocomplete(typeof(UserAccountAutoComplete))] string useraccount,
            [Summary("contractid")][Autocomplete(typeof(StaffContractAutoComplete))] string contractid, [Summary("priority")] ContractCommandsSlash.FindCoopPrioritization priority = ContractCommandsSlash.FindCoopPrioritization.FinishTimeLow) {
            await Context.Interaction.DeferAsync();
            var guildRef = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId || g.OverflowServersJson.Contains(Context.Interaction.GuildId.ToString()));
            var contract = await Db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            var userid = useraccount.Split("|")[0];
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var newCoopResponse = await ContractCommandsSlash.FindPotentialCoopForUser(account, contract, guildRef, _gateway, Db, priority);

            switch(newCoopResponse.Response) {
                case ContractCommandsSlash.PotentialCoopCode.NonUltra:
                    await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError("Non-subscribed account cannot be assigned to subscriber-only contract"));
                    return;
                case ContractCommandsSlash.PotentialCoopCode.AlreadyAssigned:
                    await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"User is already assigned a coop for contract {contract.Name}: <#{newCoopResponse.ReturnArgs[0]}>"));
                    return;
                case ContractCommandsSlash.PotentialCoopCode.NoGrade:
                    await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError("User does not have a grade set, and cannot be moved into a coop"));
                    return;
                case ContractCommandsSlash.PotentialCoopCode.NoSpots1:
                case ContractCommandsSlash.PotentialCoopCode.NoSpots2:
                    await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"No open{(contract.cc_only ? "" : $" Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())}")} coop spots found for {contract.Name}"));
                    return;
            }

            var newCoop = newCoopResponse.FoundCoop;
            var discordUser = _gateway.GetUser(dbuser.DiscordId);
            var coopChannel = newCoop.ThreadID != 0 ? _gateway.GetChannel(newCoop.ThreadID) : _gateway.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", Db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)Context.Channel);
            if(newxref == null) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server.\n**User was not moved to a coop.**" : "")}"));
                return;
            }
            Db.Add(newxref);

            await Context.Interaction.RespondAsyncGettingMessage($"Sucessfully moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}");
            await Db.SaveChangesAsync();
        }

        [SlashCommand("makeprivate", "Makes this co-op private")]
        [DefaultMemberPermissions(GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles)]
        public async Task MakePrivate() {
            await Context.Interaction.DeferAsync();
            var name = new Regex(@"\w+").Match(Context.Channel.Name.ToLower()).Value;
            var coop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find coop for this channel {Context.Channel.Name}"); });
                return;
            }

            if(string.IsNullOrEmpty(coop.CreatorID)) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to find creator for {Context.Channel.Name}"); });
                return;
            }

            var response = await EggIncApi.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = EggIncApi.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = false,
                RequestingUserId = coop.CreatorID
            }, coop.CreatorID);

            if(response.Success) {
                await Context.Interaction.ModifyOriginalResponseAsync($"{coop.Name} is now private.");
            } else {
                await Context.Interaction.ModifyOriginalResponseAsync($"{coop.Name} should now be private.");
            }
        }

        [SlashCommand("addcoop", "Adds an outside co-op so you can track it's progress")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task AddCoop([Summary("contract")][Autocomplete(typeof(StaffContractAutoComplete))] string contract,
            [Summary("coopname")] string coopname, [Summary("grade")][Autocomplete(typeof(GradeAutoComplete))] uint grade,
            [Summary("anygrade", "Is the coop any-grade?")] bool anygrade = false) {

            if(grade < 0 || grade > 5) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError($"Specified grade value (`{grade}`) outside of bounds (1-5)."));
                return;
            }

            var dbContract = Db.Contracts.FirstOrDefault(c => c.ID == contract);
            if(contract is null) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError("Contract not found - please use dropdown menu."));
                return;
            }

            var guildContract = Db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.Contract.ID == dbContract.ID && x.GuildID == Context.Interaction.GuildId);
            if(guildContract == null) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError("Unable to find contract details"));
                return;
            }

            var contractChannel = (await _gateway.GetChannelAsync(guildContract.DiscordChannelId) as SocketTextChannel);

            var status = await EggIncApi.GetCoopStatusBot(guildContract.ContractID, coopname.ToLower());
            if(status != null && status.Success) {
                var coop = new Coop {
                    ContractID = guildContract.ContractID,
                    Created = DateTimeOffset.UtcNow,
                    GuildId = guildContract.GuildID,
                    Name = coopname,
                    MaxUsers = guildContract.Contract.MaxUsers,
                    Status = CoopStatusEnum.WaitingOnThread,
                    League = grade,
                    AnyLeague = anygrade,
                    CoopEnds = DateTimeOffset.UtcNow.AddSeconds(status.SecondsRemaining),
                    AddedFromBackup = true
                };
                Db.Coops.Add(coop);
                await Db.SaveChangesAsync();
                await Context.Interaction.RespondAsync(text: "", embed: EmbedSuccess($"Co-op `{coopname}` added for {contractChannel.Mention}"));
                return;
            } else {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError($"Unable to find co-op details, double check co-op name (`{coopname}`) and correct contract channel ({contractChannel.Mention})."));
                return;
            }
        }

        [SlashCommand("fixreference", "Silently moves to coop (if needed), followed by fixing reference")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task FixReference([Summary("useraccount")][Autocomplete(typeof(UserAccountAutoComplete))] string useraccount,
            [Summary("eggincname", "(Usually not required) Egg Inc Name, will match partial name")] string eggincname = "") {
            await Context.Interaction.DeferAsync();

            var coop = await Db.Coops.Include(x => x.Contract).AsQueryable().FirstAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in co-op channels."); });
            }
            Guid userid;
            try {
                userid = Guid.Parse(useraccount.Split("|")[0]);
            } catch(Exception) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse user account, please use the autocomplete dropdown."); });
                return;
            }
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == userid);
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];
            var xref = await Db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == Context.Channel.Id || x.Coop.DiscordChannelId == Context.Channel.Id) && !x.JoinedCoop);
            xref ??= await Db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == Context.Channel.Id || x.Coop.DiscordChannelId == Context.Channel.Id));

            var discordUser = _gateway.GetUser(dbuser.DiscordId);
            var coopChannel = coop.ThreadID != 0 ? _gateway.GetChannel(coop.ThreadID) : _gateway.GetChannel(coop.DiscordChannelId);
            if(xref == null) {
                var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", Db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)Context.Channel, true);

                if(newxref == null) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"**User was not re-added to coop**:\n\nUnable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                    return;
                }
                Db.Add(newxref);
                await Db.SaveChangesAsync();
            }

            xref = await Db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == Context.Channel.Id || x.Coop.DiscordChannelId == Context.Channel.Id) && !x.JoinedCoop);
            xref ??= await Db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == dbuser.DiscordId && (x.Coop.ThreadID == Context.Channel.Id || x.Coop.DiscordChannelId == Context.Channel.Id));

            if(xref == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Even after a `MoveToCoop`, an Xref could not be found for this user. Try again?"); });
                return;
            }

            var foundEIName = account.Backup?.UserName ?? account.Name;
            if(string.IsNullOrEmpty(foundEIName) && string.IsNullOrEmpty(eggincname)) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find user's Egg Inc name from backup. Please specify the username via the command argument."); });
                return;
            }

            var name = string.IsNullOrEmpty(eggincname) ? account.Backup?.UserName : eggincname;
            var t = xref.Coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserName.ToLower().Contains(name.ToLower()));
            if(t == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user in co-op. You can use a partial in-game name."); });
                return;
            }

            xref.FixedUserName = t.UserName;
            await Db.SaveChangesAsync();

            var targetCoop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            var guild = _gateway.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
            var users = await Db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
            var dbguild = await Db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
            var parentGuild = _gateway.Guilds.First(x => x.Id == dbguild.Id);
            await _coopStatusUpdaterThreads.ProcessCoop(targetCoop.Id, guild, parentGuild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);


            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Fixed {discordUser.Mention}'s reference."); });
        }

        [SlashCommand("movetocoop", "Move a user to a co-op.")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task MoveToCoop([Summary("useraccount")][Autocomplete(typeof(UserAccountAutoComplete))] string useraccount,
            [Summary("coopid")][Autocomplete(typeof(MoveToCoopCoopNameAutoComplete))] string coopid, [Summary("silent", "If true, will not ping user in coop channel")] bool silent = false) {
            await Context.Interaction.DeferAsync();

            Guid coopId;
            try {
                coopId = Guid.Parse(coopid);
            } catch(Exception) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse coop, please use the autocomplete dropdown."); });
                return;
            }
            var coop = await Db.Coops.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == coopId);
            if(coop is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse coop, please use the autocomplete dropdown."); });
                return;
            }

            Guid userid;
            try {
                userid = Guid.Parse(useraccount.Split("|")[0]);
            } catch(Exception) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to parse user account, please use the autocomplete dropdown."); });
                return;
            }
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == userid);
            var account = dbuser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var discordUser = _gateway.GetUser(dbuser.DiscordId);
            var coopChannel = coop.ThreadID != 0 ? _gateway.GetChannel(coop.ThreadID) : _gateway.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", Db, discordUser, dbuser, (SocketThreadChannel)coopChannel, (SocketTextChannel)Context.Channel, silent);

            if(newxref == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                return;
            }
            Db.Add(newxref);

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}"); });
            await Db.SaveChangesAsync();
        }


        [SlashCommand("removefromcoop", "Remove user from co-op (only works if the bot doesn't see them as joined)")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task RemoveFromCoop([Summary("useraccount")][Autocomplete(typeof(RemoveFromCoopAutoComplete))] string useraccount) {
            await Context.Interaction.DeferAsync();
            var targetCoop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(targetCoop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please use in a co-op channel"); });
                return;
            }

            var userid = Guid.Parse(useraccount.Split("|")[0]);
            var xref = await Db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == userid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            var username = xref.User.EggIncAccounts.FirstOrDefault(x => x.Id == xref.EggIncId)?.Backup?.UserName ?? "(No Name)";

            if(xref == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user in co-op"); });
                return;
            }

            Db.Remove(xref);
            await Db.SaveChangesAsync();

            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"Removed <@{xref.User.DiscordId}> ({username}) from co-op");

        }

        [SlashCommand("deletecontract", "Delete a contract channel (Please use this instead of deleting the channel in discord)")]
        [DefaultMemberPermissions(GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles)]
        public async Task DeleteContract() {
            var guildContract = Db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == Context.Channel.Id);
            if(guildContract == null) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError("Unable to find contract, use only in contract channels."));
                return;
            }
            var dbGuild = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == guildContract.GuildID);
            _logger.LogInformation("Deleting header channels for {} because the contract channel was deleted", guildContract.Contract.Name);
            await dbGuild.DeleteCoopThreadHeadersAsync(_gateway, guildContract.Contract, _logger);

            guildContract.DeletedChannel = true;
            await Db.SaveChangesAsync();
            var channel = (SocketTextChannel)Context.Channel;
            await channel.DeleteAsync();
        }

        [SlashCommand("createcoop", "Create a co-op with the selected contract for you")]
        public async Task CreateCoop([Summary("contractid")][Autocomplete(typeof(CreateCoopContractAutoComplete))] string contractid) {
            await Context.Interaction.DeferAsync();
            var user = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(user is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find user"); });
                return;
            }
            var contract = await Db.Contracts.FirstOrDefaultAsync(x => x.ID == contractid);
            if(contract is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find contract from input. Please select a choice from the list"); });
                return;
            }
            var guildContract = await Db.GuildContracts.FirstAsync(gc => gc.GuildID == Context.Interaction.GuildId && gc.Contract == contract);

            var subscriptionAccountsCount = user.EggIncAccounts.Where(x => x.HasActiveSubscription()).Count();

            var existContractXrefs = await Db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();
            var activeXrefs = await Db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();

            var dbguild = await Db.Guilds.FirstAsync(x => x.Id == user.GuildId);
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
                    await Context.Interaction.ModifyOriginalResponseAsync(x => {
                        x.Content = ""; x.Embed = EmbedError($"You already have an assigned coop for <#{guildContract.DiscordChannelId}>. A new one was not created. Access your existing coop here: " +
                        $"<#{(xref.Coop.ThreadID != 0 ? xref.Coop.ThreadID : xref.Coop.DiscordChannelId)}>");
                    });
                    return;
                }

                if(activeXrefs is not null && activeXrefs.Count(x => x.EggIncId == (subAccountBypass?.Id ?? user.EggIncAccounts?.First().Id)) >= 4) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You have 4 active coops, and cannot be assigned a new one at this time. Try again when a current coop finishes."); });
                    return;
                }

                var guild = _gateway.GetGuild(Context.Interaction.GuildId.Value);
                var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue, accountHasUltra); //Allow all grades
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Co-op created (`{coop.Name}` - {PlayerGradeDetails.GetEmoji(coop.League)}) for {Context.User.Mention}"); });
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
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to create the co-op with."; x.Components = builder.Build(); });
            }
        }

        [ComponentInteraction("FindMyCoop", ignoreGroupNames: true)]
        public async Task FindMyCoop() {
            var component = (SocketMessageComponent)Context.Interaction;
            await component.RespondAsync(text: "", embed: EmbedInProgress("Working..."), ephemeral: true);

            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            if(dbUser is null || dbUser.GuildId != component.GuildId) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find your record - are you registered correctly?"); });
                return;
            }

            var guildContract = await Db.GuildContracts.Include(gc => gc.Contract)
                .FirstOrDefaultAsync(c => c.GuildID == component.GuildId && c.DiscordChannelId == component.ChannelId);
            if(guildContract is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("This command must be used in a contract channel."); });
                return;
            }

            // Fast path: prebuilt lookup. On a miss, fall back to the DB so a missed cache prune is
            // never wrong, only slightly slower. Both paths scoped to assigned-but-not-yet-joined.
            var found = _lookup.Get(dbUser.Id, guildContract.ContractID)
                ?? (await Db.UserCoopXrefs
                    .Where(x => x.UserId == dbUser.Id
                             && !x.JoinedCoop
                             && x.Coop.ContractID == guildContract.ContractID
                             && (int)x.Coop.Status > 2 && (int)x.Coop.Status < 13
                             && x.Coop.CoopEnds > DateTimeOffset.UtcNow && !x.Coop.PseudoExpired)
                    .Select(x => new AssignedCoop(x.Coop.Id, x.Coop.ThreadID, x.Coop.DiscordChannelId, x.Coop.Name, x.Coop.ContractID))
                    .ToListAsync())
                    .GroupBy(c => c.CoopId).Select(g => g.First()).ToList();

            if(found.Count == 0) {
                await component.ModifyOriginalResponseAsync(x => {
                    x.Content = "";
                    x.Embed = EmbedWarning($"You do not have an unjoined co-op for **{guildContract.Contract.Name}**. Either you've already joined yours, or co-ops are still being formed - once boarding groups launch this button becomes \"Find Coop Spot\" so you can grab an open seat.");
                });
                return;
            }

            var sb = new StringBuilder();
            foreach(var coop in found) {
                var channelId = coop.ThreadId != 0 ? coop.ThreadId : coop.DiscordChannelId;
                sb.AppendLine($"Thread: <#{channelId}>");
                sb.AppendLine($"Co-op code: `{coop.ContractId}` / `{coop.Name}`");
                sb.AppendLine();
            }

            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess(sb.ToString().TrimEnd()); });
        }

        [ComponentInteraction("CreateCoopButton:*", ignoreGroupNames: true)]
        public async Task CreateCoopButton(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var dataObjs = data.Split("|");
            var originalUserId = ulong.Parse(dataObjs[2]);

            if(Context.User.Id != originalUserId) {
                await component.RespondAsync(embed: EmbedError("This wasn't yours to run - don't click others' commands!"), ephemeral: true);
                return;
            }

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Working..."); x.Components = null; });
            var user = await Db.DBUsers.FirstAsync(x => x.DiscordId == Context.User.Id);
            var contractid = data.Split("|")[0];
            var contract = await Db.Contracts.FirstAsync(x => x.ID == contractid);
            var dbguild = await Db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            var account = user.EggIncAccounts.First(x => x.Id == data.Split("|")[1]);

            var guildContract = await Db.GuildContracts.FirstAsync(gc => gc.GuildID == user.GuildId && gc.Contract == contract);
            var existingXrefs = await Db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();
            var activeXrefs = await Db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();

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

            var guild = _gateway.GetGuild(Context.Interaction.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue, accountHasUltra); //Allow all grades
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Co-op created (`{coop.Name}` - {PlayerGradeDetails.GetEmoji(coop.League)}) for {Context.User.Mention}"); });
        }

        [ComponentInteraction("FindCoopSpot", ignoreGroupNames: true)]
        public async Task FindCoopSpot() {
            var component = (SocketMessageComponent)Context.Interaction;
            await component.RespondAsync(text: "", embed: EmbedInProgress("Working..."), ephemeral: true);
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser is null || dbUser.GuildId != Context.Interaction.GuildId) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Could not find your record - are you registered correctly?"); });
                return;
            }
            if(dbUser.TempDisabled) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Looks like you are currently disabled, and therefore cannot be assigned to a co-op."); });
                return;
            }
            var dbguild = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId);
            if(dbguild is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"This command must be used in a server.\n\nCome to think of it, how did you even do this?"); });
                return;
            }
            var guildContract = await Db.GuildContracts.Include(gc => gc.Contract).FirstOrDefaultAsync(c => c.GuildID == Context.Interaction.GuildId && c.DiscordChannelId == Context.Interaction.ChannelId);
            if(guildContract is null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"This command must be used in a contract channel.\n\nCome to think of it, how did you even do this?"); });
                return;
            }
            if(DateTimeOffset.UtcNow >= guildContract.Contract.GoodUntil) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"This contract has expired, and coops can no longer be joined."); });
                return;
            }

            var contract = await Db.Contracts.FirstOrDefaultAsync(c => c.ID == guildContract.ContractID);
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

        [ComponentInteraction("FindCoopSpotForAccount:*", ignoreGroupNames: true)]
        public async Task FindCoopSpotForAccount(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            if(!component.HasResponded) await component.DeferAsync();
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Coops are being filtered. This may take a few seconds."); x.Components = null; });
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            var dbguild = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId);
            var guildContract = await Db.GuildContracts.FirstOrDefaultAsync(c => c.GuildID == Context.Interaction.GuildId && c.DiscordChannelId == Context.Interaction.ChannelId);
            var contract = await Db.Contracts.FirstOrDefaultAsync(c => c.ID == guildContract.ContractID);
            var accountIndex = int.Parse(data.Split("|")[0]);
            var account = dbUser.EggIncAccounts[accountIndex];

            var newCoopResponse = await ContractCommandsSlash.FindPotentialCoopForUser(account, contract, dbguild, _gateway, Db);

            switch(newCoopResponse.Response) {
                case ContractCommandsSlash.PotentialCoopCode.NonUltra:
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Non-subscribed account cannot be assigned to subscriber-only contract"); x.Components = null; });
                    return;
                case ContractCommandsSlash.PotentialCoopCode.AlreadyAssigned:
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You already have an assigned coop for <#{Context.Interaction.ChannelId}>: <#{newCoopResponse.ReturnArgs[0]}>"); x.Components = null; });
                    return;
                case ContractCommandsSlash.PotentialCoopCode.NoGrade:
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"You do not have a grade set, and thus cannot be moved into a coop"); x.Components = null; });
                    return;
                case ContractCommandsSlash.PotentialCoopCode.NoSpots1:
                case ContractCommandsSlash.PotentialCoopCode.NoSpots2:
                    _ = Emote.TryParse(PlayerGradeDetails.GetEmoji(account.LastGrade), out var emote);
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No open{(contract.cc_only ? "" : $" Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())}")} coop spots found for {contract.Name}"); });
                    return;
                default:
                    var customEggs = await Db.GetCustomEggsAsync();
                    var coop = newCoopResponse.FoundCoop;
                    var users = coop.UserCoopsXrefs.Select(c => c.User).ToList().SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
                    var statusReponse = await EggIncApi.GetCoopStatus(coop.ContractID, coop.Name);
                    if(statusReponse is null || !statusReponse.Success || statusReponse.Contributors is null) {
                        statusReponse = coop.LastStatusUpdate; //Fallback to last known status
                    }
                    var coopDetails = new CoopDetails(coop, coop.Contract, coop.League, users, customEggs, _gateway, statusReponse);
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
                            embedBuilder.AddField("Time To Complete", "**∞**", inline: false);
                            embedBuilder.AddField("឵", "឵");
                        }
                    } else if(!statusReponse.Finished()) {
                        embedBuilder.AddField("Time To Complete", "Once everyone checks in", inline: false);
                    }

                    var acceptComponent = new ComponentBuilder().WithButton("Accept Offer", customId: $"AcceptCoopOffer:{dbUser.EggIncAccounts.IndexOf(account)}|{contract.ID}|{coop.Name}").Build();
                    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = acceptComponent; x.Embed = embedBuilder.Build(); });
                    break;
            }
        }

        [ComponentInteraction("AcceptCoopOffer:*", ignoreGroupNames: true)]
        public async Task AcceptCoopOffer(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            if(!component.HasResponded) await component.DeferAsync();
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Attempting to move you to the coop. This may take a few seconds."); x.Components = null; });
            var discordUser = Context.User;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(u => u.DiscordId == Context.User.Id);
            var accountIndex = int.Parse(data.Split("|")[0]);
            var account = dbuser.EggIncAccounts[accountIndex];

            var contractId = data.Split("|")[1];
            var contract = await Db.Contracts.FirstOrDefaultAsync(c => c.ID == contractId);
            if(contract is null) return;

            var coopId = data.Split("|")[2];
            var coop = await Db.Coops.FirstOrDefaultAsync(c => c.GuildId == dbuser.GuildId && c.Name == coopId);
            if(coop is null) return;

            var coopChannel = coop.ThreadID != 0 ? _gateway.GetChannel(coop.ThreadID) : _gateway.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Backup?.UserName ?? "(No Name)", Db, discordUser, dbuser, (SocketThreadChannel)coopChannel, null); //The "commandChannel" here is intentionally nulled to prevent sending messages in Contract channels

            if(newxref == null) {
                await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedError($"Unable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}"); });
                return;
            }
            Db.Add(newxref);

            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedSuccess($"Moved {discordUser.Mention} ({account.Backup?.UserName ?? "(No Name)"}) to {((ITextChannel)coopChannel).Mention}"); });
            await Db.SaveChangesAsync();
        }

        [ComponentInteraction("NoSpotsCreateCoop:*", ignoreGroupNames: true)]
        public async Task NoSpotsCreateCoop(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            if(!component.HasResponded) await component.DeferAsync();
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress("Coop is being created. This may take a few seconds."); x.Components = null; });
            var user = await Db.DBUsers.FirstAsync(x => x.DiscordId == Context.User.Id);
            var contractid = data.Split("|")[0];
            var contract = await Db.Contracts.FirstAsync(x => x.ID == contractid);
            var dbguild = await Db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            var account = user.EggIncAccounts.First(x => x.Id == data.Split("|")[1]);

            var guildContract = await Db.GuildContracts.FirstAsync(gc => gc.GuildID == user.GuildId && gc.Contract == contract);
            var existingXrefs = await Db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Contract == contract && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();
            var activeXrefs = await Db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User == user && x.Coop.Status != CoopStatusEnum.Failed && x.Coop.Status != CoopStatusEnum.Completed && x.Coop.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();

            var userList = new List<UserByAccount> { new() {
                Account = account,
                User = user
            }};

            if(DateTimeOffset.UtcNow >= guildContract.Contract.GoodUntil) {
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

            var guild = _gateway.GetGuild(Context.Interaction.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue, true); //Allow all grades
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = null; x.Embed = EmbedSuccess($"Co-op `{coop.Name}` {PlayerGradeDetails.GetEmoji(coop.League)} created for <#{Context.Interaction.ChannelId}>"); });
        }

        [SlashCommand("leavecoop", "Used to remove a user from a co-op to fix a glitch.")]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task LeaveCoop([Summary("useraccount")][Autocomplete(typeof(UserAccountChannelSpecificAutoComplete))] string useraccount) {
            await Context.Interaction.DeferAsync();
            var coop = await Db.Coops.FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel"); });
                return;
            }
            var userid = useraccount.Split("|")[0];
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbUser is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate DBUser entry for user"); });
                return;
            }

            var account = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];
            var xref = await Db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbUser.Id && x.CoopId == coop.Id && x.EggIncId == account.Id);

            if(xref == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to find xref"); });
                return;
            }

            var contract = await Db.Contracts.FirstAsync(x => x.ID == coop.ContractID);
            await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (Ei.Contract.Types.PlayerGrade)coop.League, coopName: "test" + new Random().Next(10000), contract.Details.LengthSeconds, xref.EggIncId, coop.AnyLeague);

            await Task.Delay(TimeSpan.FromSeconds(2));
            var status = await EggIncApi.GetCoopStatus(coop.ContractID, coop.Name);

            if(status?.Participants?.Count < contract.MaxUsers) {
                _logger.LogInformation("Successfully remove {user} from {coop}", dbUser.DiscordUsername, coop.Name);
                var coopGuild = _client.Guilds.First(x => x.Id == coop.OverflowGuildId);
                var users = await Db.DBUsers.Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync();
                var dbguild = await Db.Guilds.FirstAsync(x => x.Id == coop.GuildId);
                var parentGuild = _client.Guilds.First(x => x.Id == dbguild.Id);
                await _coopStatusUpdaterThreads.ProcessCoop(coop.Id, coopGuild, parentGuild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);

                await Context.Channel.SendMessageAsync($"Successfully removed <@{dbUser.DiscordId}> from co-op, they should be able to rejoin now.");
                await Context.Interaction.DeleteOriginalResponseAsync();
            } else {
                _logger.LogInformation("Did not {user} from {coop}", dbUser.DiscordUsername, coop.Name);
                await Context.Interaction.ModifyOriginalResponseAsync($"Attempted to remove <@{dbUser.DiscordId}> from co-op, please check again in a few minutes.");
            }
        }
    }

    public partial class AdminModule {
        [Discord.Interactions.SlashCommand("fixfullcooperror", "Fix a user getting full co-op error")]
        public async Task FixFullCoopError([Discord.Interactions.Autocomplete(typeof(UserAccountChannelSpecificAutoComplete))][Discord.Interactions.Summary("useraccount")] string useraccount) {
            await Context.Interaction.DeferAsync();
            var userid = useraccount.Split("|")[0];
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbuser is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Unable to locate user in co-op."); });
                return;
            }

            var coop = await Db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel."); });
                return;
            }

            await ContractCommandsSlash._fixFullCoopError(Context.Interaction, Db, client, coopStatusUpdaterThreads, _logger, dbuser, coop);
        }
    }
}
