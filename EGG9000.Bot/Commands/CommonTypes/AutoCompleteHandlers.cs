using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Services;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiAfxData;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Ei.Contract.Types;

namespace EGG9000.Bot.Commands.DiscordEnums {
    public class AutoCompleteHandlers {

        private static async Task<List<Guild>> ResolveGuilds(IServiceProvider services) {
            var cache = services.GetRequiredService<IMemoryCache>();
            if(!cache.TryGetValue("dbguilds", out List<Guild> dbguilds)) {
                var factory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                await using var db = await factory.CreateDbContextAsync();
                dbguilds = await db.Guilds.ToListAsync();
                cache.Set("dbguilds", dbguilds, System.TimeSpan.FromHours(1));
            }
            return dbguilds;
        }

        #region UserAutoCompletes
        public class UserAccountAutoComplete(IDbContextFactory<ApplicationDbContext> dbFactory, DatabaseCache cache) : AutocompleteHandler {
            private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;
            private readonly DatabaseCache _cache = cache;

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                // GuildId is null in DMs; bail rather than dereferencing it.
                if(arg.GuildId is not ulong guildId) return new List<AutocompleteResult>();
                var guildIdStr = guildId.ToString();
                var guild = guilds.FirstOrDefault(x => x.Id == guildId || (x.OverflowServersJson?.Contains(guildIdStr) ?? false));
                if(guild is null) return new List<AutocompleteResult>();
                var allusers = _cache.GetCachedUsers();
                if(allusers is null) return new List<AutocompleteResult>();
                // Current.Value is null on the first focus before any text is typed.
                var query = arg.Data.Current.Value?.ToString() ?? string.Empty;
                var users = allusers
                    .Where(
                        x => x.GuildId == guild.Id && (
                            (x.DiscordUsername?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||  //Match discord username
                            (x.Usernames?.Contains(query) ?? false) //Or match egg inc username
                        )
                    )
                    .Take(10);

                var accounts = users.SelectMany(x => (x.EggIncAccounts ?? []).Select(y => new { User = x, Account = y })).OrderBy(x => x.Account.Backup?.EarningsBonus);

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts.DistinctBy(x => x.Account.Id)) {
                    var name = account.Account.Backup?.UserName;
                    results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Backup?.UserName ?? "(No Name)"} ({account.Account.Backup?.EarningsBonus.ToEggString() ?? "?"})", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                }
                return results;
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var guilds = await ResolveGuilds(services);
                var results = await ComputeResults(arg, guilds);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public class UserAccountChannelSpecificAutoComplete(IDbContextFactory<ApplicationDbContext> dbFactory) : AutocompleteHandler {
            private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var coop = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == arg.Channel.Id);

                if(coop is null || coop.FinishedOrFailedOrExpired()) {
                    return new List<AutocompleteResult>();
                }
                var eidsIn = coop.UserCoopsXrefs.Select(x => x.EggIncId).ToList();
                if(eidsIn.Count == 0) {
                    return new List<AutocompleteResult>();
                }

                //Filter users by current search
                var users = string.IsNullOrWhiteSpace((string)arg.Data.Current.Value) ?
                    coop.UserCoopsXrefs :
                    coop.UserCoopsXrefs.Where(x =>
                        x.User.DiscordUsername.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase) || //Match discord username
                        x.User.Usernames.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase) //Or match egg inc username
                    );

                var accounts = users.SelectMany(x => x.User.EggIncAccounts.Where(a => eidsIn.Contains(a.Id)).Select(y => new { User = x.User, Account = y }));

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts.DistinctBy(a => a.Account.Id)) {
                    if(account.User.EggIncAccounts.Count > 1) {
                        var name = account.Account.Backup?.UserName;
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Backup?.UserName ?? "(No Name)"}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    } else {
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    }
                }
                return results;
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public class PersonalUserAccountAutoComplete(IDbContextFactory<ApplicationDbContext> dbFactory, DatabaseCache cache) : AutocompleteHandler {
            private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;
            private readonly DatabaseCache _cache = cache;

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                // GuildId is null in DMs; bail rather than dereferencing it.
                if(arg.GuildId is not ulong guildId) return new List<AutocompleteResult>();
                var guildIdStr = guildId.ToString();
                var guild = guilds.FirstOrDefault(x => x.Id == guildId || (x.OverflowServersJson?.Contains(guildIdStr) ?? false));
                if(guild is null) return new List<AutocompleteResult>();
                var allusers = _cache.GetCachedUsers();
                if(allusers is null) return new List<AutocompleteResult>();
                var users = allusers
                    .Where(x => x.GuildId == guild.Id && x.DiscordId == arg.User.Id)
                    .Take(10);

                // EggIncAccounts can be null for a user whose accounts blob hasn't materialised yet.
                var accounts = users.SelectMany(x => (x.EggIncAccounts ?? []).Select(y => new { User = x, Account = y })).OrderBy(x => x.Account.Backup?.EarningsBonus);

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts.DistinctBy(x => x.Account.Id)) {
                    if((account.User.EggIncAccounts?.Count ?? 0) > 1) {
                        var name = account.Account.Backup?.UserName;
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Backup?.UserName ?? "(No Name)"} {account.Account.Backup?.EarningsBonus.ToEggString() ?? "?"}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    } else {
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    }
                }
                return results;
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var guilds = await ResolveGuilds(services);
                var results = await ComputeResults(arg, guilds);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }
        #endregion

        #region ContractAutoCompletes
        /*
         *  Clone of ContractAutoComplete with no limitation on who can select Ultra coops
         */
        public class StaffContractAutoComplete(IDbContextFactory<ApplicationDbContext> dbFactory) : AutocompleteHandler {
            private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var contracts = await db.Contracts.Where(x => x.MaxUsers > 1 && x.GoodUntil > DateTimeOffset.UtcNow.AddDays(-14)).Select(x => new { x.ID, x.Name }).ToListAsync();
                var stringArg = (string)arg.Data.Current.Value;
                if(!string.IsNullOrEmpty(stringArg) && stringArg != " ") contracts = contracts.Where(x => x.Name.Contains(stringArg) || x.ID.Contains(stringArg)).ToList(); //Filter by name
                return contracts.DistinctBy(x => x.Name).ToList().Select(c => new AutocompleteResult(c.Name, c.ID)).ToList();
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public class CreateCoopContractAutoComplete(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient client) : AutocompleteHandler {
            private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;
            private readonly DiscordSocketClient _discord = client;

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var guild = db.Guilds.FirstOrDefault(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                if(guild is null) return new List<AutocompleteResult>();
                var discordGuild = _discord.GetGuild(guild.Id);
                var discordUserPerms = discordGuild.GetUser(arg.User.Id).GuildPermissions.ToList();
                var isStaff = discordUserPerms.Contains(GuildPermission.Administrator) || discordUserPerms.Contains(GuildPermission.ManageChannels) || discordUserPerms.Contains(GuildPermission.CreatePrivateThreads) || discordUserPerms.Contains(GuildPermission.ModerateMembers);
                var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == arg.User.Id);
                var hasSubscriptionAccounts = dbUser?.EggIncAccounts.Where(x => x.HasActiveSubscription()).Any() ?? false;

                var contracts = db.Contracts.Where(x => x.MaxUsers > 1 && (hasSubscriptionAccounts ? (x.GoodUntil > DateTimeOffset.UtcNow) : (x.GoodUntil > DateTimeOffset.UtcNow && !x.cc_only))).ToList();
                var stringArg = (string)arg.Data.Current.Value;
                if(!string.IsNullOrEmpty(stringArg) && stringArg != " ") contracts = contracts.Where(x => x.Name.Contains(stringArg) || x.ID.Contains(stringArg)).ToList(); //Filter by name
                if(guild is not null && !guild.DisableBG && !isStaff) {
                    //Limit contracts to those that have had longer than 16 hours to launch (i.e. all three boarding groups)
                    contracts = contracts.Where(x => (DateTimeOffset.UtcNow - x.Created).TotalHours > 17).ToList();
                }

                var contractObjs = contracts.Select(x => new { x.ID, x.Name }).ToList();
                return contractObjs.Select(c => new AutocompleteResult(c.Name, c.ID)).ToList();
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }
        #endregion

        #region CoopAutoCompletes
        public class MoveGradeAutoComplete(IDbContextFactory<ApplicationDbContext> dbFactory) : AutocompleteHandler {
            private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var coop = await db.Coops.FirstOrDefaultAsync(x => x.ThreadID == arg.Channel.Id);

                if(coop is null || coop.League == 0) {
                    return new List<AutocompleteResult>(); //Command only works in a co-op channel and where grade is known.
                }

                return Enumerable.Range(1, 5)
                    .Where(i => i != coop.League && Math.Abs(coop.League - i) < 2)
                    .Reverse()
                    .ToList()
                    .Select(x => new AutocompleteResult(PlayerGradeDetails.GetText((PlayerGrade)x), (uint)x))
                    .ToList();
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public class GradeAutoComplete() : AutocompleteHandler {
            private Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var result = Enumerable.Range(1, 5).Reverse().ToList()
                    .Select(x => new AutocompleteResult(PlayerGradeDetails.GetText((PlayerGrade)x), (uint)x))
                    .ToList();
                return Task.FromResult(result);
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public class RemoveFromCoopAutoComplete(IDbContextFactory<ApplicationDbContext> dbContextFactory) : AutocompleteHandler {

            private async Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                await using var db = await dbContextFactory.CreateDbContextAsync();
                var users = await db.UserCoopXrefs.Where(x => x.Coop.ThreadID == arg.Channel.Id).Select(x => new { x.UserId, x.EggIncId, x.User.DiscordUsername, x.User }).ToListAsync();
                if(users.Count == 0) return new List<AutocompleteResult>();
                if(!string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    users = users.Where(x => x.DiscordUsername.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                return users.DistinctBy(x => x.EggIncId).Take(25).Select(x => new AutocompleteResult(x.DiscordUsername + " - " + (x.User?.EggIncAccounts.FirstOrDefault(a => a.Id == x.EggIncId)?.Backup?.UserName ?? "(No Name)"), x.UserId.ToString())).ToList();
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public class MoveToCoopCoopNameAutoComplete(DatabaseCache dbCache) : AutocompleteHandler {
            private readonly DatabaseCache _dbCache = dbCache;

            private Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                if(arg.GuildId is not ulong guildId) return Task.FromResult(new List<AutocompleteResult>());
                var guild = guilds.FirstOrDefault(x => x.Id == guildId || x.OverflowServers.Contains(guildId));
                if(guild is null) return Task.FromResult(new List<AutocompleteResult>());

                var activeCoops = _dbCache.ActiveCoopsWithFiveMinuteDelay() ?? [];
                var filter = (string)arg.Data.Current.Value;
                List<CoopMin> coops;
                if(string.IsNullOrWhiteSpace(filter)) {
                    coops = activeCoops
                        .Take(25).Select(x => new CoopMin { Name = x.Name, Id = x.Id, Contract = x.Contract?.Name, League = x.League }).ToList();
                } else {
                    coops = activeCoops
                        .Where(x => x.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true && !x.ThreadArchived && x.GuildId == guild.Id && !x.DeletedChannel)
                        .Take(25).Select(x => new CoopMin { Name = x.Name, Id = x.Id, Contract = x.Contract?.Name, League = x.League }).ToList();
                }

                return Task.FromResult(coops.DistinctBy(x => x.Id).ToList().Select(c => new AutocompleteResult($"{c.Name} - {c.Contract} - {PlayerGradeDetails.GetNameFromLeague(c.League)}", c.Id.ToString())).ToList());
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var guilds = await ResolveGuilds(services);
                var results = await ComputeResults(arg, guilds);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }

            public class CoopMin {
                public string Name { get; set; }
                public Guid Id { get; set; }
                public string Contract { get; set; }
                public uint League { get; set; }
            }
        }
        #endregion

        #region ServicesAutoCompletes
        private static List<AutocompleteResult> _allServicesAndJobs = null;
        public class ServiceNameAutoComplete(IServiceProvider serviceProvider) : AutocompleteHandler {
            private readonly IServiceProvider _serviceProvider = serviceProvider;

            private Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                if(_allServicesAndJobs == null) {
                    var services = _serviceProvider.GetServices<IHostedService>().Where(x => x is IUpdaterService).OrderBy(x => x.GetType().Name)
                        .Select(c => new AutocompleteResult($"{c.GetType().Name}", c.GetType().Name)).ToList();

                    var jobs = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(x => x.GetLoadableExportedTypes())
                        .SelectMany(t => t.GetMethods())
                        .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                        .Select(m => new AutocompleteResult($"Job.{m.DeclaringType?.Name}", m.Name))
                        .ToArray();

                    var discordHostedService = _serviceProvider.GetServices<DiscordHostedService>().Select(c => new AutocompleteResult("DiscordHostedService", c.GetType().Name)).ToList();

                    _allServicesAndJobs = [
                        .. services,
                        .. jobs,
                        .. discordHostedService,
                    ];
                }

                var results = _allServicesAndJobs;
                if(!string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    results = results.Where(x => x.Name.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                return Task.FromResult(results.OrderBy(x => x.Name).ToList());
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                var results = await ComputeResults(arg, null);
                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }
        #endregion

        #region AFXAutoCompletes
        public class ArtifactNameAutoComplete() : AutocompleteHandler {
            private readonly EiAfxDataRoot _eiAfxData = EggIncArtifacts.GetEiAfxData();

            private Task<List<AutocompleteResult>> ComputeResults(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                IEnumerable<ArtifactFamily> artifactFamilies = [.. _eiAfxData.artifact_families];
                if(!string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    artifactFamilies = artifactFamilies.Where(x => x.name.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase));
                }
                return Task.FromResult(artifactFamilies.Select(c => new AutocompleteResult($"{c.name}", c.id)).Take(25).ToList());
            }

            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, Discord.IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                var arg = (SocketAutocompleteInteraction)autocompleteInteraction;
                try {
                    var results = await ComputeResults(arg, null);
                    return AutocompletionResult.FromSuccess(results.Take(25));
                } catch(TimeoutException) {
                    return AutocompletionResult.FromSuccess(System.Array.Empty<AutocompleteResult>());
                }
            }
        }
        #endregion

    }
}
