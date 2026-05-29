using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Services;
using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiAfxData;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Ei.Contract.Types;

namespace EGG9000.Bot.Commands.DiscordEnums {
    public class AutoCompleteHandlers {

        // The synthetic "force a cache refresh" choice appended to cache-backed autocompletes.
        private static AutocompleteResult RefreshOption() => new(AutoCompleteRefresh.Label, AutoCompleteRefresh.Sentinel);

        // Caps real results at 24 and appends the refresh option, keeping under Discord's 25 limit.
        private static AutocompleteResult[] WithRefresh(IEnumerable<AutocompleteResult> results) =>
            [.. results.Take(24), RefreshOption()];

        #region UserAutoCompletes
        public class UserAccountAutoComplete(ApplicationDbContext db, DatabaseCache cache) : IAutoCompleteHandler, IRefreshableAutoComplete {
            private readonly ApplicationDbContext _db = db;
            private readonly DatabaseCache _cache = cache;

            public string CacheName => "users";
            public Task RefreshAsync() => _cache.RefreshUserCache();

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var guild = guilds.FirstOrDefault(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                if(guild is null) return;
                var allusers = _cache.GetCachedUsers();
                var users = allusers
                    .Where(
                        x => x.GuildId == guild.Id && (
                            (x.DiscordUsername?.Contains(arg.Data.Current.Value.ToString(), StringComparison.OrdinalIgnoreCase) ?? false) || // EF.Functions.Like(x.DiscordUsername, $"%{(string)arg.Data.Current.Value}%") || //Match discord username
                            (x.Usernames?.Contains((string)arg.Data.Current.Value) ?? false) //Or match egg inc username
                        )
                    )
                    .Take(10);

                var accounts = users.SelectMany(x => x.EggIncAccounts.Select(y => new { User = x, Account = y })).OrderBy(x => x.Account.Backup?.EarningsBonus);

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts.DistinctBy(x => x.Account.Id)) {
                    var name = account.Account.Backup?.UserName;
                    results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Backup?.UserName ?? "(No Name)"} ({account.Account.Backup?.EarningsBonus.ToEggString() ?? "?"})", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                }
                await arg.RespondAsync(null, WithRefresh(results));
            }
        }

        public class UserAccountChannelSpecificAutoComplete(ApplicationDbContext db) : IAutoCompleteHandler {
            private readonly ApplicationDbContext _db = db;

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var coop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == arg.Channel.Id);

                if(coop is null || coop.FinishedOrFailedOrExpired()) {
                    return; //Needs to be used in an active coop channel with users in it
                }
                var eidsIn = coop.UserCoopsXrefs.Select(x => x.EggIncId).ToList();
                if(eidsIn.Count == 0) {
                    return;
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

                await arg.RespondAsync(null, [..results]);
            }
        }

        public class PersonalUserAccountAutoComplete(ApplicationDbContext db, DatabaseCache cache) : IAutoCompleteHandler, IRefreshableAutoComplete {
            private readonly ApplicationDbContext _db = db;
            private readonly DatabaseCache _cache = cache;

            public string CacheName => "users";
            public Task RefreshAsync() => _cache.RefreshUserCache();

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var guild = guilds.FirstOrDefault(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                if(guild is null) return;
                var allusers = _cache.GetCachedUsers();
                var users = allusers
                    .Where(x => x.GuildId == guild.Id && x.DiscordId == arg.User.Id)
                    .Take(10);

                var accounts = users.SelectMany(x => x.EggIncAccounts.Select(y => new { User = x, Account = y })).OrderBy(x => x.Account.Backup?.EarningsBonus);

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts.DistinctBy(x => x.Account.Id)) {
                    if(account.User.EggIncAccounts.Count > 1) {
                        var name = account.Account.Backup?.UserName;
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Backup?.UserName ?? "(No Name)"} {account.Account.Backup?.EarningsBonus.ToEggString() ?? "?"}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    } else {
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    }
                }

                await arg.RespondAsync(null, WithRefresh(results));
            }
        }
        #endregion

        #region ContractAutoCompletes
        /*
         *  Clone of ContractAutoComplete with no limitation on who can select Ultra coops
         */
        public class StaffContractAutoComplete(ApplicationDbContext db) : IAutoCompleteHandler {
            private readonly ApplicationDbContext _db = db;

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var contracts = await _db.Contracts.Where(x => x.MaxUsers >  1 && x.GoodUntil > DateTimeOffset.Now.AddDays(-14)).Select(x => new { x.ID, x.Name }).ToListAsync();
                var stringArg = (string)arg.Data.Current.Value;
                if(!string.IsNullOrEmpty(stringArg) && stringArg != " ") contracts = contracts.Where(x => x.Name.Contains(stringArg) || x.ID.Contains(stringArg)).ToList(); //Filter by name
                await arg.RespondAsync(null, contracts.DistinctBy(x => x.Name).ToList().Select(c => new AutocompleteResult(c.Name, c.ID)).ToArray());
            }
        }

        public class CreateCoopContractAutoComplete(ApplicationDbContext db, DiscordSocketClient client) : IAutoCompleteHandler {
            private readonly ApplicationDbContext _db = db;
            private readonly DiscordSocketClient _discord = client;

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var guild = _db.Guilds.FirstOrDefault(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                var discordGuild = _discord.GetGuild(guild.Id);
                var discordUserPerms = discordGuild.GetUser(arg.User.Id).GuildPermissions.ToList();
                var isStaff = discordUserPerms.Contains(GuildPermission.Administrator) || discordUserPerms.Contains(GuildPermission.ManageChannels) || discordUserPerms.Contains(GuildPermission.CreatePrivateThreads) || discordUserPerms.Contains(GuildPermission.ModerateMembers);
                var dbUser = _db.DBUsers.FirstOrDefault(x => x.DiscordId == arg.User.Id);
                var hasSubscriptionAccounts = dbUser?.EggIncAccounts.Where(x => x.HasActiveSubscription()).Any() ?? false;

                var contracts = _db.Contracts.Where(x => x.MaxUsers > 1 && (hasSubscriptionAccounts ? (x.GoodUntil > DateTimeOffset.Now) : (x.GoodUntil > DateTimeOffset.Now && !x.cc_only))).ToList();
                var stringArg = (string)arg.Data.Current.Value;
                if(!string.IsNullOrEmpty(stringArg) && stringArg != " ") contracts = contracts.Where(x => x.Name.Contains(stringArg) || x.ID.Contains(stringArg)).ToList(); //Filter by name
                if(guild is not null && !guild.DisableBG && !isStaff) {
                    //Limit contracts to those that have had longer than 16 hours to launch (i.e. all three boarding groups)
                    contracts = contracts.Where(x => (DateTimeOffset.Now - x.Created).TotalHours > 17).ToList();
                }

                var contractObjs = contracts.Select(x => new { x.ID, x.Name }).ToList();
                await arg.RespondAsync(null, contractObjs.Select(c => new AutocompleteResult(c.Name, c.ID)).ToArray());
            }
        }
        #endregion

        #region CoopAutoCompletes
        public class MoveGradeAutoComplete(ApplicationDbContext db) : IAutoCompleteHandler {
            private readonly ApplicationDbContext _db = db;

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var coop = await _db.Coops.FirstOrDefaultAsync(x => x.ThreadID == arg.Channel.Id);

                if(coop is null || coop.League == 0) {
                    return; //Command only works in a co-op channel and where grade is known.
                }

                var result = Enumerable.Range(1, 5)
                    .Where(i => i != coop.League && Math.Abs(coop.League - i) < 2)
                    .Reverse()
                    .ToList()
                    .Select(x => new AutocompleteResult(PlayerGradeDetails.GetText((PlayerGrade)x), (uint)x));

                await arg.RespondAsync(
                    result
                );
            }
        }

        public class GradeAutoComplete() : IAutoCompleteHandler {
            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var result = Enumerable.Range(1, 5).Reverse().ToList()
                    .Select(x => new AutocompleteResult(PlayerGradeDetails.GetText((PlayerGrade)x), (uint)x));
                await arg.RespondAsync(
                    result
                );
            }
        }

        public class RemoveFromCoopAutoComplete(IDbContextFactory<ApplicationDbContext> dbContextFactory) : IAutoCompleteHandler {
            //private readonly ApplicationDbContext _db = db;

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var db = await dbContextFactory.CreateDbContextAsync();
                var users = await db.UserCoopXrefs.Where(x => x.Coop.ThreadID == arg.Channel.Id).Select(x => new { x.UserId, x.EggIncId, x.User.DiscordUsername, x.User }).ToListAsync();
                if(users.Count == 0) await arg.RespondAsync("Command only works in a co-op channel and where users are assigned.");
                if(!string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    users = users.Where(x => x.DiscordUsername.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                await arg.RespondAsync(users.DistinctBy(x => x.EggIncId).Take(25).Select(x => new AutocompleteResult(x.DiscordUsername + " - " + (x.User?.EggIncAccounts.FirstOrDefault(a => a.Id == x.EggIncId)?.Backup?.UserName ?? "(No Name)"), x.UserId.ToString())));
            }
        }

        public class MoveToCoopCoopNameAutoComplete(DatabaseCache dbCache) : IAutoCompleteHandler, IRefreshableAutoComplete {
            private readonly DatabaseCache _dbCache = dbCache;

            public string CacheName => "active-coops";
            public Task RefreshAsync() => _dbCache.RefreshActiveCoopsCache();

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                var guild = guilds.First(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                List<CoopMin> coops = null;
                if(string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    coops = _dbCache.ActiveCoopsWithFiveMinuteDelay()
                        .Take(25).Select(x => new CoopMin { Name = x.Name, Id = x.Id, Contract = x.Contract?.Name, League = x.League }).ToList();
                }

                coops ??= _dbCache.ActiveCoopsWithFiveMinuteDelay()
                    .Where(x => x.Name.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase) && !x.ThreadArchived && x.GuildId == guild.Id && !x.DeletedChannel)
                    .Take(25).Select(x => new CoopMin { Name = x.Name, Id = x.Id, Contract = x.Contract?.Name, League = x.League }).ToList();

                await arg.RespondAsync(null, WithRefresh(coops.DistinctBy(x => x.Id).Select(c => new AutocompleteResult($"{c.Name} - {c.Contract} - {PlayerGradeDetails.GetNameFromLeague(c.League)}", c.Id.ToString()))));
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
        public class ServiceNameAutoComplete(IServiceProvider serviceProvider) : IAutoCompleteHandler {
            private readonly IServiceProvider _serviceProvider = serviceProvider;

            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
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

                await arg.RespondAsync(null, [..results.OrderBy(x => x.Name)]);
            }
        }
        #endregion

        #region AFXAutoCompletes
        public class ArtifactNameAutoComplete() : IAutoCompleteHandler {
            private readonly EiAfxDataRoot _eiAfxData = EggIncArtifacts.GetEiAfxData();
            public async Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds) {
                IEnumerable<ArtifactFamily> artifactFamilies = [.. _eiAfxData.artifact_families];
                if(!string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    artifactFamilies = artifactFamilies.Where(x => x.name.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase));
                }

                try {
                    await arg.RespondAsync(null, artifactFamilies.Select(c => new AutocompleteResult($"{c.name}", c.id)).Take(25).ToArray());
                } catch(TimeoutException) { }
            }
        }
        #endregion

    }
}
