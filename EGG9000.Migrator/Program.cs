using EGG9000.Common.Database;
using EGG9000.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var sourceCs = config.GetConnectionString("SourceConnection")
    ?? throw new InvalidOperationException(
        "SourceConnection (SQL Server) not set. " +
        "Add it via: dotnet user-secrets set ConnectionStrings:SourceConnection \"<mssql-cs>\"");

var targetCs = config.GetConnectionString("TargetConnection")
    ?? throw new InvalidOperationException(
        "TargetConnection (Postgres) not set. " +
        "Add it via: dotnet user-secrets set ConnectionStrings:TargetConnection \"<pg-cs>\"");

Console.WriteLine("=== EGG9000 DB Migrator ===");
Console.WriteLine($"Source (SQL Server): {Mask(sourceCs)}");
Console.WriteLine($"Target (Postgres):   {Mask(targetCs)}");
Console.WriteLine();

// --- Quick path: pgLoader ---
// Use this for the initial bulk migration. Run the command below on any Linux
// machine (or WSL) that has pgloader installed.
Console.WriteLine("pgLoader command (quick path):");
Console.WriteLine("  pgloader mssql://<user>:<pass>@aws1.sglade.com/EGG9000Dev postgresql://<user>:<pass>@<pg-host>/EGG9000");
Console.WriteLine();
Console.WriteLine("Flags:");
Console.WriteLine("  --verify   Compare source vs target row counts (safe, read-only)");
Console.WriteLine("  --ef       Run the full EF data migration");
Console.WriteLine();

if (args.Contains("--verify")) {
    await RunVerify(sourceCs, targetCs);
} else if (args.Contains("--ef")) {
    await RunEfMigration(sourceCs, targetCs);
} else {
    Console.WriteLine("No flag provided. Use --verify or --ef.");
}

// ---------------------------------------------------------------------------

static async Task RunEfMigration(string sourceCs, string targetCs) {
    var sourceOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(sourceCs)
        .Options;

    var targetOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(targetCs, x => x.MigrationsAssembly("EGG9000.Common"))
        .Options;

    await using var src = new ApplicationDbContext(sourceOptions);
    await using var tgt = new ApplicationDbContext(targetOptions);
    tgt.ChangeTracker.AutoDetectChangesEnabled = false;

    Console.WriteLine("Clearing target tables (reverse FK order)...");
    // Reverse of the migration order so FK constraints are not violated.
    await tgt.UserCoopStatuses.ExecuteDeleteAsync();
    await tgt.UserCoopXrefs.ExecuteDeleteAsync();
    await tgt.Merit.ExecuteDeleteAsync();
    await tgt.Demerit.ExecuteDeleteAsync();
    await tgt.GuildContracts.ExecuteDeleteAsync();
    await tgt.Coops.ExecuteDeleteAsync();
    await tgt.UserCsHistoryEntries.ExecuteDeleteAsync();
    await tgt.UserSnapShots.ExecuteDeleteAsync();
    await tgt.NasaApods.ExecuteDeleteAsync();
    await tgt.ResearchCostSubmissions.ExecuteDeleteAsync();
    await tgt.FAQTopics.ExecuteDeleteAsync();
    await tgt.UpcomingContracts.ExecuteDeleteAsync();
    await tgt.AutomationLogs.ExecuteDeleteAsync();
    await tgt.ExpiringShells.ExecuteDeleteAsync();
    await tgt.TemporaryRoles.ExecuteDeleteAsync();
    await tgt.GlobalLeaderboardUsers.ExecuteDeleteAsync();
    await tgt.GlobalLeaderboardCoops.ExecuteDeleteAsync();
    await tgt.CustomEggs.ExecuteDeleteAsync();
    await tgt.Donations.ExecuteDeleteAsync();
    await tgt.EventCustomizations.ExecuteDeleteAsync();
    await tgt.Events.ExecuteDeleteAsync();
    await tgt.DBUsers.ExecuteDeleteAsync();
    await tgt.Contracts.ExecuteDeleteAsync();
    await tgt.Guilds.ExecuteDeleteAsync();
    Console.WriteLine("Target cleared.");
    Console.WriteLine();

    Console.WriteLine("Starting EF migration...");
    Console.WriteLine();

    // Migrate in FK-safe order. Each group must complete before the next
    // because later groups contain rows that reference rows in earlier groups.
    //
    // Note on ulong columns (DiscordId, channel IDs, etc.):
    //   SQL Server stores them as decimal(20,0); Npgsql maps them to numeric.
    //   All current Discord snowflakes fit in int64, so this is safe in practice.
    //
    // Note on Identity tables (AspNetUsers, AspNetRoles, DataProtectionKeys):
    //   Not migrated - Discord OAuth re-creates user records on first login,
    //   and DataProtectionKeys should be generated fresh per environment.

    // --- Group 1: no inbound FKs ---
    await EntityMigrator.Migrate(src.Guilds,                  tgt, tgt.Guilds,                  "Guilds");
    await EntityMigrator.Migrate(src.Contracts,               tgt, tgt.Contracts,               "Contracts");
    await EntityMigrator.Migrate(src.DBUsers,                 tgt, tgt.DBUsers,                 "DBUsers");
    await EntityMigrator.Migrate(src.Events,                  tgt, tgt.Events,                  "Events");
    await EntityMigrator.Migrate(src.EventCustomizations,     tgt, tgt.EventCustomizations,     "EventCustomizations");
    await EntityMigrator.Migrate(src.Donations,               tgt, tgt.Donations,               "Donations");
    await EntityMigrator.Migrate(src.CustomEggs,              tgt, tgt.CustomEggs,              "CustomEggs");
    await EntityMigrator.Migrate(src.GlobalLeaderboardCoops,  tgt, tgt.GlobalLeaderboardCoops,  "GlobalLeaderboardCoops");
    await EntityMigrator.Migrate(src.GlobalLeaderboardUsers,  tgt, tgt.GlobalLeaderboardUsers,  "GlobalLeaderboardUsers");
    await EntityMigrator.Migrate(src.TemporaryRoles,          tgt, tgt.TemporaryRoles,          "TemporaryRoles");
    await EntityMigrator.Migrate(src.ExpiringShells,          tgt, tgt.ExpiringShells,          "ExpiringShells");
    await EntityMigrator.Migrate(src.AutomationLogs,          tgt, tgt.AutomationLogs,          "AutomationLogs");
    await EntityMigrator.Migrate(src.UpcomingContracts,       tgt, tgt.UpcomingContracts,       "UpcomingContracts");
    await EntityMigrator.Migrate(src.FAQTopics,               tgt, tgt.FAQTopics,               "FAQTopics");
    await EntityMigrator.Migrate(src.ResearchCostSubmissions, tgt, tgt.ResearchCostSubmissions, "ResearchCostSubmissions");
    await EntityMigrator.Migrate(src.NasaApods,               tgt, tgt.NasaApods,               "NasaApods");
    await EntityMigrator.Migrate(src.UserSnapShots,           tgt, tgt.UserSnapShots,           "UserSnapShots");
    await EntityMigrator.Migrate(src.UserCsHistoryEntries,    tgt, tgt.UserCsHistoryEntries,    "UserCsHistoryEntries");

    // --- Group 2: FK to Contracts ---
    await EntityMigrator.Migrate(src.Coops,                   tgt, tgt.Coops,                   "Coops");
    await EntityMigrator.Migrate(src.GuildContracts,          tgt, tgt.GuildContracts,          "GuildContracts");

    // --- Group 3: FK to DBUsers ---
    await EntityMigrator.Migrate(src.Demerit,                 tgt, tgt.Demerit,                 "Demerits");
    await EntityMigrator.Migrate(src.Merit,                   tgt, tgt.Merit,                   "Merits");

    // --- Group 4: FK to both Coops and DBUsers ---
    await EntityMigrator.Migrate(src.UserCoopXrefs,           tgt, tgt.UserCoopXrefs,           "UserCoopXrefs");
    await EntityMigrator.Migrate(src.UserCoopStatuses,        tgt, tgt.UserCoopStatuses,        "UserCoopStatuses");

    Console.WriteLine();
    Console.WriteLine("EF migration complete.");
}

static async Task RunVerify(string sourceCs, string targetCs) {
    var sourceOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(sourceCs)
        .Options;
    var targetOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(targetCs, x => x.MigrationsAssembly("EGG9000.Common"))
        .Options;

    await using var src = new ApplicationDbContext(sourceOptions);
    await using var tgt = new ApplicationDbContext(targetOptions);

    Console.WriteLine($"{"Table",-30} {"Source",8} {"Target",8}  {"Status"}");
    Console.WriteLine(new string('-', 60));

    await Row("Guilds",                  src.Guilds,                  tgt.Guilds);
    await Row("Contracts",               src.Contracts,               tgt.Contracts);
    await Row("DBUsers",                 src.DBUsers,                 tgt.DBUsers);
    await Row("Events",                  src.Events,                  tgt.Events);
    await Row("EventCustomizations",     src.EventCustomizations,     tgt.EventCustomizations);
    await Row("Donations",               src.Donations,               tgt.Donations);
    await Row("CustomEggs",              src.CustomEggs,              tgt.CustomEggs);
    await Row("GlobalLeaderboardCoops",  src.GlobalLeaderboardCoops,  tgt.GlobalLeaderboardCoops);
    await Row("GlobalLeaderboardUsers",  src.GlobalLeaderboardUsers,  tgt.GlobalLeaderboardUsers);
    await Row("TemporaryRoles",          src.TemporaryRoles,          tgt.TemporaryRoles);
    await Row("ExpiringShells",          src.ExpiringShells,          tgt.ExpiringShells);
    await Row("AutomationLogs",          src.AutomationLogs,          tgt.AutomationLogs);
    await Row("UpcomingContracts",       src.UpcomingContracts,       tgt.UpcomingContracts);
    await Row("FAQTopics",               src.FAQTopics,               tgt.FAQTopics);
    await Row("ResearchCostSubmissions", src.ResearchCostSubmissions, tgt.ResearchCostSubmissions);
    await Row("NasaApods",               src.NasaApods,               tgt.NasaApods);
    await Row("UserSnapShots",           src.UserSnapShots,           tgt.UserSnapShots);
    await Row("UserCsHistoryEntries",    src.UserCsHistoryEntries,    tgt.UserCsHistoryEntries);
    await Row("Coops",                   src.Coops,                   tgt.Coops);
    await Row("GuildContracts",          src.GuildContracts,          tgt.GuildContracts);
    await Row("Demerits",                src.Demerit,                 tgt.Demerit);
    await Row("Merits",                  src.Merit,                   tgt.Merit);
    await Row("UserCoopXrefs",           src.UserCoopXrefs,           tgt.UserCoopXrefs);
    await Row("UserCoopStatuses",        src.UserCoopStatuses,        tgt.UserCoopStatuses);

    static async Task Row<T>(string label, IQueryable<T> source, IQueryable<T> target) where T : class {
        int s = await source.CountAsync();
        int t = await target.CountAsync();
        string status = t == 0 ? "EMPTY" : t == s ? "OK" : $"MISMATCH ({s - t} missing)";
        Console.WriteLine($"{label,-30} {s,8} {t,8}  {status}");
    }
}

static string Mask(string cs) =>
    Regex.Replace(cs, @"(?i)(Password|pwd|User Id|User)=([^;]+)", "$1=***");
