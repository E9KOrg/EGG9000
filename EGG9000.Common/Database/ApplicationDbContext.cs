using EGG9000.Common.Database.Entities;

using Npgsql;

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
        public ApplicationDbContext CreateDbContext(string[] args) {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Development";

            var currentDirectory = Directory.GetCurrentDirectory();
            var basePath = ResolveConfigurationBasePath(currentDirectory);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var userSecretsId = "dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a";
            var userSecretsPath = ResolveUserSecretsPath(userSecretsId);
            var userSecretsConfigurationBuilder = new ConfigurationBuilder();
            if(File.Exists(userSecretsPath))
                userSecretsConfigurationBuilder.AddJsonFile(userSecretsPath, optional: true, reloadOnChange: false);

            var userSecretsConfiguration = userSecretsConfigurationBuilder.Build();

            var userSecretsConnectionString = userSecretsConfiguration.GetConnectionString("DefaultConnection")
                ?? userSecretsConfiguration["ConnectionStrings:DefaultConnection"];

            var configurationConnectionString = configuration.GetConnectionString("DefaultConnection")
                ?? configuration["ConnectionStrings:DefaultConnection"];

            var hasUserSecretsConnection = !string.IsNullOrWhiteSpace(userSecretsConnectionString);
            var connectionString = hasUserSecretsConnection ? userSecretsConnectionString : configurationConnectionString;
            var connectionSource = hasUserSecretsConnection ? "UserSecrets" : "Configuration";

            if(string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException($"Connection string 'DefaultConnection' was not found for environment '{environment}'.");

            LogResolvedConnection(connectionString, environment, basePath, connectionSource);

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString, options => {
                options.MigrationsAssembly("EGG9000.Common");
                options.EnableRetryOnFailure();
                options.CommandTimeout(30);
            });

            return new ApplicationDbContext(optionsBuilder.Options);
        }

        private static void LogResolvedConnection(string connectionString, string environment, string basePath, string connectionSource) {
            try {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                Console.WriteLine($"[EF DesignTime] Using DefaultConnection from '{connectionSource}'. Environment='{environment}', BasePath='{basePath}', Host='{builder.Host}', Database='{builder.Database}', SslMode='{builder.SslMode}'.");
            } catch(Exception ex) {
                Console.WriteLine($"[EF DesignTime] Using DefaultConnection from '{connectionSource}'. Environment='{environment}', BasePath='{basePath}'. Connection string parse failed ({ex.GetType().Name}).");
            }
        }

        private static string ResolveUserSecretsPath(string userSecretsId) {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
        }

        private static string ResolveConfigurationBasePath(string startDirectory) {
            var directory = new DirectoryInfo(startDirectory);

            while(directory is not null) {
                var directAppSettings = Path.Combine(directory.FullName, "appsettings.json");
                if(File.Exists(directAppSettings))
                    return directory.FullName;

                var siteDirectory = Path.Combine(directory.FullName, "EGG9000.Site");
                var siteAppSettings = Path.Combine(siteDirectory, "appsettings.json");
                if(File.Exists(siteAppSettings))
                    return siteDirectory;

                directory = directory.Parent;
            }

            throw new InvalidOperationException($"Could not locate 'appsettings.json' starting from '{startDirectory}'.");
        }
    }

    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext {
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
        public DbSet<Guild> Guilds { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<Coop> Coops { get; set; }
        public DbSet<DBUser> DBUsers { get; set; }
        public DbSet<UserCoopXref> UserCoopXrefs { get; set; }
        public DbSet<UserCoopStatus> UserCoopStatuses { get; set; }
        public DbSet<GuildContract> GuildContracts { get; set; }
        public DbSet<Demerit> Demerit { get; set; }
        public DbSet<Merit> Merit { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<EventCustomization> EventCustomizations { get; set; }
        public DbSet<Donation> Donations { get; set; }
        public DbSet<DBCustomEgg> CustomEggs { get; set; }

        public DbSet<GlobalLeaderboardCoop> GlobalLeaderboardCoops { get; set; }
        public DbSet<GlobalLeaderboardUser> GlobalLeaderboardUsers { get; set; }
        public DbSet<UserSnapShot> UserSnapShots { get; set; }

        public DbSet<TemporaryRole> TemporaryRoles { get; set; }
        public DbSet<ExpiringShell> ExpiringShells { get; set; }
        public DbSet<AutomationLog> AutomationLogs { get; set; }
        public DbSet<UpcomingContract> UpcomingContracts { get; set; }
        public DbSet<UserCsHistoryEntry> UserCsHistoryEntries { get; set; }
        public DbSet<FAQTopic> FAQTopics { get; set; }
        public DbSet<RankupMessage> RankupMessages { get; set; }
        public DbSet<ResearchCostSubmission> ResearchCostSubmissions { get; set; }
        public DbSet<NasaApod> NasaApods { get; set; }
        public DbSet<SeasonInfo> SeasonInfos { get; set; }
        public DbSet<UserSeasonProgress> UserSeasonProgresses { get; set; }
        public DbSet<ShadowAssignmentDiff> ShadowAssignmentDiffs { get; set; }

        public FrozenSet<Guild> CachedGuilds {
            get {
                return _cache.GetOrCreate("DbContext-Guilds", entry => {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return Guilds.ToFrozenSet();
                });
            }
        }

        public async Task<FrozenSet<Ei.Contract>> CachedEiContractsAsync() {
            return await _cache.GetOrCreateAsync("DbContext-EiContracts", async entry => {
                var dbcontracts = await Contracts.ToListAsync();
                var (eiContracts, _) = await EggIncAPI.EggIncApi.GetContractsArchive(EggIncAPI.EggIncApi.UserId);

                var contracts = eiContracts?.Archive?.Select(x => x.Contract).ToList() ?? [];
                // Archive fetch failed (e.g. API timeout) - fall back to DB contracts and retry soon instead of caching the degraded set for an hour.
                entry.AbsoluteExpirationRelativeToNow = contracts.Count > 0 ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1);
                contracts.AddRange(dbcontracts.Where(dbc => !contracts.Any(c => c.Identifier == dbc.ID)).Select(x => x.Details));
                return contracts.ToFrozenSet();
            });

        }

        public void ExpireCachedEiContracts() {
            _cache.Remove("DbContext-EiContracts");
        }

        // Registers contract definitions fetched by identifier (get_contracts_info) that the periodicals
        // feed never delivered to us (e.g. single-player contracts), so they exist in the DB and resolve
        // in CachedEiContractsAsync for everyone. Inserts the row only; fires no channel/coop automation.
        public async Task<int> RegisterMissingContractsAsync(System.Collections.Generic.IEnumerable<Ei.Contract> contractDefs, System.Threading.CancellationToken ct = default) {
            var defs = contractDefs
                .Where(c => c is not null && !string.IsNullOrEmpty(c.Identifier))
                .GroupBy(c => c.Identifier)
                .Select(g => g.First())
                .ToList();
            if(defs.Count == 0) return 0;

            var existingIds = (await Contracts.Select(c => c.ID).ToListAsync(ct)).ToHashSet();
            var missing = defs.Where(d => !existingIds.Contains(d.Identifier)).ToList();
            if(missing.Count == 0) return 0;

            foreach(var def in missing) {
                Contracts.Add(new Contract {
                    ID = def.Identifier,
                    Created = DateTimeOffset.UtcNow,
                    Description = def.Description,
                    Name = def.Name,
                    goals = Newtonsoft.Json.JsonConvert.SerializeObject(def.Goals),
                    GoodUntil = DateTimeOffset.FromUnixTimeSeconds((long)def.ExpirationTime),
                    MaxUsers = (int)def.MaxCoopSize,
                    coop_allowed = def.CoopAllowed,
                    max_boosts = (int)def.MaxBoosts,
                    max_soul_eggs = def.MaxSoulEggs,
                    min_client_version = (int)def.MinClientVersion,
                    debug = def.Debug,
                    length_seconds = def.LengthSeconds,
                    egg = def.Egg.ToString(),
                    cc_only = def.CcOnly,
                    _response = Newtonsoft.Json.JsonConvert.SerializeObject(def)
                });
            }

            await SaveChangesAsync(ct);
            ExpireCachedEiContracts();
            return missing.Count;
        }

        public readonly IMemoryCache _cache;
#nullable enable
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMemoryCache? cache = null) : base(options) {
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
            ChangeTracker.Tracked += OnEntityTracked;
            ChangeTracker.StateChanged += OnEntityStateChanged;
        }
#nullable disable

        void OnEntityTracked(object sender, EntityTrackedEventArgs e) {
            if(!e.FromQuery && e.Entry.State == EntityState.Added && e.Entry.Entity is ILastModified entity)
                entity.LastModified = DateTimeOffset.UtcNow;
        }

        void OnEntityStateChanged(object sender, EntityStateChangedEventArgs e) {
            if(e.NewState == EntityState.Modified && e.Entry.Entity is ILastModified entity)
                entity.LastModified = DateTimeOffset.UtcNow;
        }

        // AdminUserId is a nullable FK to a DBUser (the staff member who issued a merit/demerit).
        // Automated merits/demerits have no admin and historically used Guid.Empty as a sentinel.
        // SQL Server did not enforce the FK on that value, but Postgres does: inserting Guid.Empty
        // references a non-existent user and the whole SaveChanges fails, which (because the Discord
        // message is sent first) causes infinite re-sends. Normalize the sentinel to null at the
        // boundary so the FK is satisfied, mirroring the same conversion the data migrator applies.
        private void NormalizeAdminUserIds() {
            foreach(var entry in ChangeTracker.Entries()) {
                if(entry.State is not (EntityState.Added or EntityState.Modified)) continue;
                switch(entry.Entity) {
                    case Demerit { AdminUserId: { } d } when d == Guid.Empty:
                        ((Demerit)entry.Entity).AdminUserId = null; break;
                    case Merit { AdminUserId: { } m } when m == Guid.Empty:
                        ((Merit)entry.Entity).AdminUserId = null; break;
                }
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess) {
            NormalizeAdminUserIds();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) {
            NormalizeAdminUserIds();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }


        // Npgsql 'timestamp with time zone' only accepts UTC (offset 0). This converter normalizes
        // every mapped DateTimeOffset column write to UTC, so a stray local-offset value such as
        // DateTimeOffset.Now can never crash a write. It does NOT cover query parameters that are
        // not tied to a converted column (a literal in a Where/OrderBy, raw SQL) -
        // UtcDateTimeOffsetCommandInterceptor is the runtime safety net for those. The instant is
        // preserved. Reads keep Npgsql's default materialization (local offset, same instant); the
        // app compares DateTimeOffsets by instant, so the offset representation is irrelevant.
        private sealed class UtcDateTimeOffsetConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset> {
            public UtcDateTimeOffsetConverter() : base(v => v.ToUniversalTime(), v => v) { }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            base.OnConfiguring(optionsBuilder);
            // Last-line normalization of any non-UTC DateTimeOffset command parameter.
            // Registered here so every consumer gets it.
            optionsBuilder.AddInterceptors(UtcDateTimeOffsetCommandInterceptor.Instance);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        }

        protected override void OnModelCreating(ModelBuilder builder) {
            base.OnModelCreating(builder);
            builder.Entity<UserCoopXref>().HasKey(x => new { x.UserId, x.CoopId, x.EggIncId });
            builder.Entity<UserSnapShot>().HasKey(x => new { x.UserId, x.Date, x.EggIncID });
            builder.Entity<UserSeasonProgress>().HasKey(x => new { x.EggIncId, x.SeasonId });
            builder.Entity<GuildContract>().HasKey(x => new { x.ContractID, x.GuildID, x.League });
            builder.Entity<TemporaryRole>().HasKey(x => new { x.UserId, x.RoleId, x.Created });
            builder.Entity<UserCsHistoryEntry>().HasKey(x => new { x.CoopIdentifier, x.ContractIdentifier, x.EggIncId });
            builder.Entity<DBCustomEgg>().HasKey(x => new { x.Identifier });

            builder.Entity<Demerit>().HasOne(x => x.User).WithMany(x => x.Demerits).HasForeignKey(x => x.UserId);
            builder.Entity<Demerit>().HasOne(x => x.AdminUser).WithMany(x => x.DemeritsGiven).OnDelete(DeleteBehavior.ClientSetNull).HasForeignKey(x => x.AdminUserId);
            builder.Entity<Merit>().HasOne(x => x.User).WithMany(x => x.Merits).OnDelete(DeleteBehavior.ClientCascade).HasForeignKey(x => x.UserId);
            builder.Entity<Merit>().HasOne(x => x.AdminUser).WithMany(x => x.MeritsGiven).IsRequired(false).OnDelete(DeleteBehavior.ClientSetNull).HasForeignKey(x => x.AdminUserId);

            builder.Entity<NasaApod>().HasKey(x => x.ID);
            builder.Entity<NasaApod>().Property(x => x.DateString).HasDefaultValueSql("TO_CHAR(NOW() AT TIME ZONE 'UTC', 'YYYY-MM-DD')");

            builder.Entity<DBUser>().HasIndex(x => x.DiscordId);
            builder.Entity<UserCoopXref>().HasIndex(x => new { x.CreatedOn, x.JoinedCoop });
            builder.Entity<Guild>().HasIndex(x => x.DiscordSeverId);
            builder.Entity<GuildContract>().HasIndex(x => x.DiscordChannelId);

            builder.Entity<Coop>().HasIndex(x => new { x.GuildId, x.ContractID, x.League })
                .HasFilter("NOT \"Finished\" AND NOT \"DeletedChannel\" AND NOT \"ThreadArchived\"");

            // DEV test-harness coops are excluded from every Coop query by default so they can never
            // trigger real thread/API creation or status polling. The harness opts back in where it
            // needs to see them (stats/assignment refresh, cleanup) via IgnoreQueryFilters().
            builder.Entity<Coop>().HasQueryFilter(c => c.CreatorID != Coop.TestSeedCreatorId);
        }
    }
}
