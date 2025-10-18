using EGG9000.Common.Database.Entities;

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Frozen;

namespace EGG9000.Common.Database {
    //public class ApplicationDbContext : IDesignTimeDbContextFactory<ApplicationDbContext> {
    //    public ApplicationDbContext CreateDbContext(string[] args) {
    //        //Console.WriteLine("Creating DB Context");
    //        // Get environment
    //        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    //        // Build config
    //        var Configuration = new ConfigurationBuilder()
    //            .AddUserSecrets<Secrets>()
    //            .Build();

    //        //var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
    //        var connectionString = Configuration["ConnectionStrings:DefaultConnection"];
    //        //optionsBuilder.UseSqlServer(connectionString, b => { b.MigrationsAssembly("EGG9000.Common"); b.CommandTimeout(120); });


    //        return new ApplicationDbContext(connectionString);
    //    }
    //}

    public class ApplicationDbContext : IdentityDbContext<IdentityUser>, IDataProtectionKeyContext {
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
        public DbSet<Guild> Guilds { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<Coop> Coops { get; set; }
        //public DbSet<CoopStatus> CoopStatuses { get; set; }
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
        public DbSet<NasaApod> NasaApods { get; set; }

        public FrozenSet<Guild> CachedGuilds {
            get {
                return _cache.GetOrCreate<FrozenSet<Guild>>("DbContext-Guilds", entry => {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return Guilds.ToFrozenSet();
                });
            }
        }

        //    private IConfiguration _configuration;
        //    public ApplicationDbContext(DbContextOptions options, IConfiguration configuration) : base(options) {
        //        connstring
        //            }
        //    //    public ApplicationDbContext() : base(GetOptions())
        //    //    {
        //    //    }

        public readonly IMemoryCache _cache;
        [ActivatorUtilitiesConstructor]
        public ApplicationDbContext(IConfiguration configuration, IMemoryCache cache) : base(GetOptions(configuration)) {
            _cache = cache;
        }

        private static DbContextOptions GetOptions(IConfiguration configuration) {
            //        var Configuration = new ConfigurationBuilder()
            //.AddUserSecrets<Secrets>()
            //.Build();
            //Console.WriteLine(Configuration["ConnectionStrings:DefaultConnection"]);
            //Console.WriteLine(Configuration.GetConnectionString("DefaultConnection"));
            return SqlServerDbContextOptionsExtensions.UseSqlServer(new DbContextOptionsBuilder(), configuration["ConnectionStrings:DefaultConnection"], options => { options.EnableRetryOnFailure(); options.CommandTimeout(120); }).Options;
        }


        protected override void OnModelCreating(ModelBuilder builder) {
            base.OnModelCreating(builder);
            builder.Entity<UserCoopXref>().HasKey(x => new { x.UserId, x.CoopId, x.EggIncId });
            builder.Entity<UserSnapShot>().HasKey(x => new { x.UserId, x.Date, x.EggIncID });
            builder.Entity<GuildContract>().HasKey(x => new { x.ContractID, x.GuildID, x.League });
            builder.Entity<TemporaryRole>().HasKey(x => new { x.UserId, x.RoleId, x.Created });
            builder.Entity<UserCsHistoryEntry>().HasKey(x => new { x.CoopIdentifier, x.ContractIdentifier, x.EggIncId });
            builder.Entity<DBCustomEgg>().HasKey(x => new { x.Identifier });

            builder.Entity<Demerit>().HasOne(x => x.User).WithMany(x => x.Demerits).HasForeignKey(x => x.UserId);
            builder.Entity<Demerit>().HasOne(x => x.AdminUser).WithMany(x => x.DemeritsGiven).OnDelete(DeleteBehavior.ClientSetNull).HasForeignKey(x => x.AdminUserId);
            builder.Entity<Merit>().HasOne(x => x.User).WithMany(x => x.Merits).OnDelete(DeleteBehavior.ClientCascade).HasForeignKey(x => x.UserId);
            builder.Entity<Merit>().HasOne(x => x.AdminUser).WithMany(x => x.MeritsGiven).IsRequired(false).OnDelete(DeleteBehavior.ClientSetNull).HasForeignKey(x => x.AdminUserId);

            builder.Entity<NasaApod>().Property(x => x.DateString).HasDefaultValueSql("CURRENT_DATE");

            //builder.Entity<IdentityRole>().HasData(
            //    new IdentityRole { Id = "c1dd39e4-dbe5-48a4-b0c6-897c5b3db799", Name = "LesserGuildAdmin", NormalizedName = "GUILDLESSERADMIN" },
            //    new IdentityRole { Id = "d5cfa96d-1cde-49bb-87a4-95c8e2923b46", Name = "GuildAdmin", NormalizedName = "GUILDADMIN" },
            //    new IdentityRole { Id = "ef4c281d-0ec5-4e70-b027-181e8eed8c54", Name = "Admin", NormalizedName = "ADMIN" }
            //);
            //builder.Entity<User>().Property(x => x.LastBackup).HasField("_LastBackup");

            builder.Entity<DBUser>().HasIndex(x => x.DiscordId);
            builder.Entity<UserCoopXref>().HasIndex(x => new { x.CreatedOn, x.JoinedCoop });
        }
    }
}
