using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EGG9000.Common.Database {
    public class ApplicationDbFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
        public ApplicationDbContext CreateDbContext(string[] args) {
            //Console.WriteLine("Creating DB Context");
            // Get environment
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            // Build config
            var Configuration = new ConfigurationBuilder()
                .AddUserSecrets<Secrets>()
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            var connectionString = Configuration["ConnectionStrings:DefaultConnection"];
            optionsBuilder.UseSqlServer(connectionString, b => { b.MigrationsAssembly("EGG9000.Common"); b.CommandTimeout(120); });
            

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }

    public class ApplicationDbContext : IdentityDbContext<IdentityUser> {
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

        public DbSet<GlobalLeaderboardCoop> GlobalLeaderboardCoops { get; set; }
        public DbSet<GlobalLeaderboardUser> GlobalLeaderboardUsers { get; set; }
        public DbSet<UserSnapShot> UserSnapShots { get; set; }

        public DbSet<TemporaryRole> TemporaryRoles { get; set; }
        public DbSet<ExpiringShell> ExpiringShells { get; set; }

        public ApplicationDbContext(DbContextOptions options) : base(options) {
                }
        //    public ApplicationDbContext() : base(GetOptions())
        //    {
        //    }

        public ApplicationDbContext(string connString) : base(GetOptions(connString)) {
        }

        private static DbContextOptions GetOptions() {
            var Configuration = new ConfigurationBuilder()
    .AddUserSecrets<Secrets>()
    .Build();
            //Console.WriteLine(Configuration["ConnectionStrings:DefaultConnection"]);
            //Console.WriteLine(Configuration.GetConnectionString("DefaultConnection"));
            return SqlServerDbContextOptionsExtensions.UseSqlServer(new DbContextOptionsBuilder(), Configuration["ConnectionStrings:DefaultConnection"], options => { options.EnableRetryOnFailure(); options.CommandTimeout(120); }).Options;
        }

        private static DbContextOptions GetOptions(string connString) {
            if (connString == null)
                return GetOptions();
            return SqlServerDbContextOptionsExtensions.UseSqlServer(new DbContextOptionsBuilder(), connString, options => { options.EnableRetryOnFailure(); options.CommandTimeout(120); }).Options;
        }




        protected override void OnModelCreating(ModelBuilder builder) {
            base.OnModelCreating(builder);
            builder.Entity<UserCoopXref>().HasKey(x => new { x.UserId, x.CoopId, x.EggIncId });
            builder.Entity<UserSnapShot>().HasKey(x => new { x.UserId, x.Date, x.EggIncID });
            builder.Entity<GuildContract>().HasKey(x => new { x.ContractID, x.GuildID, x.Elite });
            builder.Entity<TemporaryRole>().HasKey(x => new { x.UserId, x.RoleId, x.Created });

            builder.Entity<Demerit>().HasOne(x => x.User).WithMany(x => x.Demerits).HasForeignKey(x => x.UserId);
            builder.Entity<Demerit>().HasOne(x => x.AdminUser).WithMany(x => x.DemeritsGiven).OnDelete(DeleteBehavior.ClientSetNull).HasForeignKey(x => x.AdminUserId);
            builder.Entity<Merit>().HasOne(x => x.User).WithMany(x => x.Merits).OnDelete(DeleteBehavior.ClientCascade).HasForeignKey(x => x.UserId);
            builder.Entity<Merit>().HasOne(x => x.AdminUser).WithMany(x => x.MeritsGiven).OnDelete(DeleteBehavior.ClientSetNull).HasForeignKey(x => x.AdminUserId);

            //builder.Entity<User>().Property(x => x.LastBackup).HasField("_LastBackup");

        }
    }
}
