using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public partial class BotGroupModule {

        // Live /a sysload sessions, keyed by message id. Tracks current section + cancellation
        // source so refresh re-renders the right section and Stop / dismiss / 30s cap can cancel.
        private sealed class SysLoadSession {
            public CancellationTokenSource Cts;
            public string Section = "overview";
        }
        private static readonly ConcurrentDictionary<ulong, SysLoadSession> _sysLoad = new();

        private static string FormatUptime(TimeSpan u) =>
            u.TotalDays >= 1 ? $"{(int)u.TotalDays}d {u.Hours}h {u.Minutes}m"
            : u.TotalHours >= 1 ? $"{u.Hours}h {u.Minutes}m"
            : u.TotalMinutes >= 1 ? $"{u.Minutes}m {u.Seconds}s"
            : $"{u.Seconds}s";

        private static double HealthRange(double v, double good, double bad) =>
            v <= good ? 1 : v >= bad ? 0 : 1 - (v - good) / (bad - good);

        private static Color HealthColor(double h) {
            h = Math.Clamp(h, 0, 1);
            int r, g;
            if(h >= 0.5) { r = (int)Math.Round((1 - h) * 2 * 220); g = 200; } else { r = 220; g = (int)Math.Round(h * 2 * 200); }
            return new Color(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), 0);
        }

        private static string HealthDot(double h) => h >= 0.8 ? "\U0001F7E2" : h >= 0.5 ? "\U0001F7E1" : "\U0001F534";
        private static int HealthPct(double h) => (int)Math.Round(Math.Clamp(h, 0, 1) * 100);

        private sealed record ServiceRow(string Name, string Avg, string Last, int Attempts, string Status);

        private sealed record PgStats(
            long Commits, long Rollbacks, long BlksHit, long BlksRead,
            long TupReturned, long TupFetched, long TupInserted, long TupUpdated, long TupDeleted,
            long Deadlocks, long Backends, long Conflicts, long TempFiles, long LocksWaiting, DateTimeOffset At);

        private sealed record PgRates(
            double Commits, double Rollbacks, double Returned, double Fetched,
            double Inserted, double Updated, double Deleted, double RollbackRatio);

        private sealed record SysLoadSnapshot(
            long Ping, double WorkingMb, double GcHeapMb, int Threads, double CpuMin,
            long ApiCalls, long ApiFails, long DbQueries, long Commands, long CmdFails, long DiscordOps,
            long DbReads, long DbWrites, long DbFailures, long DbRetries, long DbSlow,
            double DbMeanMs, double DbP50, double DbP95, double DbP99, double DbMax,
            PgStats Pg, PgRates Rates,
            int Latency, int Guilds, int QHigh, int QLow, int QHighW, int QLowW,
            double RuntimeHealth, double DiscordHealth, double ProcessHealth, double DbHealth,
            long StartedUnix, long NowUnix, IReadOnlyList<ServiceRow> Services) {
            public double Worst {
                get {
                    return Math.Min(Math.Min(RuntimeHealth, DiscordHealth), Math.Min(ProcessHealth, DbHealth));
                }
            }
        }

        private const string PgStatsSql = @"
            SELECT d.xact_commit, d.xact_rollback, d.blks_hit, d.blks_read,
                   d.tup_returned, d.tup_fetched, d.tup_inserted, d.tup_updated, d.tup_deleted,
                   d.deadlocks, d.numbackends, d.conflicts, d.temp_files,
                   (SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock') AS locks_waiting
            FROM pg_stat_database d
            WHERE d.datname = current_database()";

        private static PgStats _prevPg;

        private static double PgCacheHit(PgStats p) {
            var total = p.BlksHit + p.BlksRead;
            return total <= 0 ? 100 : 100.0 * p.BlksHit / total;
        }

        private static async Task<PgStats> ReadPgStats(ApplicationDbContext db) {
            try {
                await db.Database.OpenConnectionAsync();
                try {
                    await using var cmd = db.Database.GetDbConnection().CreateCommand();
                    cmd.CommandText = PgStatsSql;
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if(!await reader.ReadAsync()) return null;
                    long L(int i) => reader.IsDBNull(i) ? 0 : Convert.ToInt64(reader.GetValue(i));
                    return new PgStats(L(0), L(1), L(2), L(3), L(4), L(5), L(6), L(7), L(8), L(9), L(10), L(11), L(12), L(13), DateTimeOffset.UtcNow);
                } finally {
                    await db.Database.CloseConnectionAsync();
                }
            } catch(Exception) {
                return null;
            }
        }

        private static PgRates ComputePgRates(PgStats now) {
            if(now is null) return null;
            var prev = Interlocked.Exchange(ref _prevPg, now);
            if(prev is null) return null;
            var secs = (now.At - prev.At).TotalSeconds;
            if(secs <= 0 || now.Commits < prev.Commits) return null;
            double Rate(long a, long b) => Math.Max(a - b, 0) / secs;
            var commits = Math.Max(now.Commits - prev.Commits, 0);
            var rollbacks = Math.Max(now.Rollbacks - prev.Rollbacks, 0);
            var txns = commits + rollbacks;
            return new PgRates(Rate(now.Commits, prev.Commits), Rate(now.Rollbacks, prev.Rollbacks),
                Rate(now.TupReturned, prev.TupReturned), Rate(now.TupFetched, prev.TupFetched),
                Rate(now.TupInserted, prev.TupInserted), Rate(now.TupUpdated, prev.TupUpdated),
                Rate(now.TupDeleted, prev.TupDeleted), txns == 0 ? 0 : (double)rollbacks / txns);
        }

        private static async Task<SysLoadSnapshot> GatherSysLoad(ApplicationDbContext db, DiscordSocketClient client, IDiscordQueue queue, IServiceProvider serviceProvider) {
            var sw = Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            var pingMs = sw.ElapsedMilliseconds;

            var proc = Process.GetCurrentProcess();
            var workingMb = proc.WorkingSet64 / 1_048_576.0;
            var gcHeapMb = GC.GetTotalMemory(false) / 1_048_576.0;

            var pg = await ReadPgStats(db);
            var rates = ComputePgRates(pg);

            var latency = client?.Latency ?? -1;
            var guilds = client?.Guilds?.Count ?? 0;
            var qHigh = queue?.HighDepth ?? 0;
            var qLow = queue?.LowDepth ?? 0;
            var backlog = qHigh + qLow;

            var apiCalls = RuntimeMetrics.ApiCalls;
            var apiFails = RuntimeMetrics.ApiFailures;
            var commands = RuntimeMetrics.Commands;
            var cmdFails = RuntimeMetrics.CommandFailures;

            var runtimeHealth = Math.Min(apiCalls == 0 ? 1 : 1 - (double)apiFails / apiCalls, commands == 0 ? 1 : 1 - (double)cmdFails / commands);
            var discordHealth = Math.Min(latency < 0 ? 1 : HealthRange(latency, 150, 1000), HealthRange(backlog, 25, 500));
            var processHealth = Math.Min(HealthRange(workingMb, 1200, 4000), HealthRange(gcHeapMb, 500, 3000));
            var dbOps = RuntimeMetrics.DbQueries;
            var dbFailures = RuntimeMetrics.DbFailures;
            var (p50, p95, p99, pMax) = RuntimeMetrics.DbLatencyPercentiles();
            var failPct = dbOps == 0 ? 0 : 100.0 * dbFailures / dbOps;
            var rollbackPct = 100.0 * (rates?.RollbackRatio ?? 0);
            var locksWaiting = pg?.LocksWaiting ?? 0;
            var dbHealth = Math.Min(
                Math.Min(HealthRange(p95, 50, 500), HealthRange(failPct, 0.1, 5)),
                Math.Min(HealthRange(rollbackPct, 1, 25), HealthRange(locksWaiting, 0, 10)));

            var lastComplete = await db.AutomationLogs.Where(x => x.EndTime.HasValue).GroupBy(x => x.Type).Select(g => g.OrderByDescending(y => y.EndTime).First()).ToListAsync();
            var recentLogs = await db.AutomationLogs.Where(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-1)).ToListAsync();
            var serviceAverages = recentLogs.Where(x => x.EndTime.HasValue).GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.Average(y => y.EndTime.Value.ToUnixTimeSeconds() - y.StartTime.ToUnixTimeSeconds()));
            var updaterServices = serviceProvider.GetServices<IHostedService>().ToList();
            var serviceRows = new List<ServiceRow>();
            foreach(var log in lastComplete.OrderBy(x => x.Type)) {
                if(updaterServices.FirstOrDefault(x => x.GetType().Name == log.Type) is not IUpdaterService updater) continue;
                var incompletes = recentLogs.Where(x => x.Type == log.Type && x.StartTime > log.EndTime).ToList();
                var avg = serviceAverages.TryGetValue(log.Type, out var a) ? TimeSpan.FromSeconds(a).Humanize().ShortenTime() : "";
                var last = (DateTimeOffset.UtcNow - log.EndTime.Value).Humanize().ShortenTime();
                var status = updater.Running()
                    ? (incompletes.Any(x => !x.Skipped) ? $"Run {(DateTimeOffset.UtcNow - incompletes.Last(x => !x.Skipped).StartTime).Humanize().ShortenTime()}" : "Started")
                    : "Stopped";
                serviceRows.Add(new ServiceRow(log.Type, avg, last, incompletes.Count, status));
            }

            return new SysLoadSnapshot(pingMs, workingMb, gcHeapMb, proc.Threads.Count, proc.TotalProcessorTime.TotalMinutes,
                apiCalls, apiFails, dbOps, commands, cmdFails, RuntimeMetrics.DiscordOps,
                RuntimeMetrics.DbReads, RuntimeMetrics.DbWrites, dbFailures, RuntimeMetrics.DbRetries, RuntimeMetrics.DbSlow,
                RuntimeMetrics.DbMeanMs, p50, p95, p99, pMax, pg, rates,
                latency, guilds, qHigh, qLow, queue?.HighWorkers ?? 0, queue?.LowWorkers ?? 0,
                runtimeHealth, discordHealth, processHealth, dbHealth,
                RuntimeMetrics.StartedAt.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), serviceRows);
        }

        private static string SysLoadContent(SysLoadSnapshot s) =>
            $"-# Counters since <t:{s.StartedUnix}:R> · updated <t:{s.NowUnix}:R>";

        private static List<List<FixedWidthCell>> BuildServiceTable(IReadOnlyList<ServiceRow> services) {
            var table = new List<List<FixedWidthCell>> { new() { new("Name"), new("Avg"), new("Last"), new("Att"), new("Status") } };
            foreach(var svc in services)
                table.Add([new(svc.Name), new(svc.Avg), new(svc.Last), new(svc.Attempts.ToString()), new(svc.Status)]);
            return table;
        }

        private static Embed SysLoadSection(string section, SysLoadSnapshot s) {
            string Metric(long total, double perMin) => $"`{total:N0}` total\n`{perMin:F1}`/min";

            return section switch {
                "runtime" => new EmbedBuilder()
                    .WithAuthor($"Runtime Usage  -  {HealthPct(s.RuntimeHealth)}% healthy")
                    .WithColor(HealthColor(s.RuntimeHealth))
                    .AddField("Egg Inc API", Metric(s.ApiCalls, RuntimeMetrics.PerMinute(s.ApiCalls)) + (s.ApiFails > 0 ? $"\n`{s.ApiFails:N0}` failed" : ""), inline: true)
                    .AddField("DB Queries", Metric(s.DbQueries, RuntimeMetrics.PerMinute(s.DbQueries)), inline: true)
                    .AddField("Commands", Metric(s.Commands, RuntimeMetrics.PerMinute(s.Commands)) + (s.CmdFails > 0 ? $"\n`{s.CmdFails:N0}` failed" : ""), inline: true)
                    .AddField("Discord Ops", Metric(s.DiscordOps, RuntimeMetrics.PerMinute(s.DiscordOps)), inline: true)
                    .Build(),
                "discord" => new EmbedBuilder()
                    .WithAuthor($"Discord  -  {HealthPct(s.DiscordHealth)}% healthy")
                    .WithColor(HealthColor(s.DiscordHealth))
                    .AddField("Gateway", $"`{s.Latency}` ms", inline: true)
                    .AddField("Guilds", $"`{s.Guilds}`", inline: true)
                    .AddField("Send Queue", $"H `{s.QHigh}` / `{s.QHighW}`w\nL `{s.QLow}` / `{s.QLowW}`w", inline: true)
                    .Build(),
                "process" => new EmbedBuilder()
                    .WithAuthor($"Process  -  {HealthPct(s.ProcessHealth)}% healthy")
                    .WithColor(HealthColor(s.ProcessHealth))
                    .AddField("Uptime", $"`{FormatUptime(RuntimeMetrics.Uptime)}`", inline: true)
                    .AddField("Working Set", $"`{s.WorkingMb:F1}` MB", inline: true)
                    .AddField("GC Heap", $"`{s.GcHeapMb:F1}` MB", inline: true)
                    .AddField("GC 0/1/2", $"`{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}`", inline: true)
                    .AddField("Threads", $"`{s.Threads}`", inline: true)
                    .AddField("CPU Time", $"`{s.CpuMin:F1}` min", inline: true)
                    .Build(),
                "database" => new EmbedBuilder()
                    .WithAuthor($"Database  -  {HealthPct(s.DbHealth)}% healthy")
                    .WithColor(HealthColor(s.DbHealth))
                    .AddField("Ops", Metric(s.DbQueries, RuntimeMetrics.PerMinute(s.DbQueries)) + $"\nR `{s.DbReads:N0}` / W `{s.DbWrites:N0}`", inline: true)
                    .AddField("Latency", $"mean `{s.DbMeanMs:F1}` ms\nmax `{s.DbMax:F1}` ms\nping `{s.Ping}` ms", inline: true)
                    .AddField("Percentiles", $"p50 `{s.DbP50:F1}` ms\np95 `{s.DbP95:F1}` ms\np99 `{s.DbP99:F1}` ms", inline: true)
                    .AddField("Errors", $"`{s.DbFailures:N0}` failed\n`{s.DbRetries:N0}` retries\n`{s.DbSlow:N0}` slow >{RuntimeMetrics.DbSlowThresholdMs:F0}ms", inline: true)
                    .AddField("Transactions", s.Pg is null ? "`n/a`" : s.Rates is null
                        ? $"`{s.Pg.Commits:N0}` commits total\n`{s.Pg.Rollbacks:N0}` rollbacks total"
                        : $"`{s.Rates.Commits:F1}`/s commits\n`{s.Rates.Rollbacks:F2}`/s rollbacks", inline: true)
                    .AddField("Cache Hit", s.Pg is null ? "`n/a`" : $"`{PgCacheHit(s.Pg):F2}`%\n`{s.Pg.BlksRead:N0}` disk reads", inline: true)
                    .AddField("Backends", s.Pg is null ? "`n/a`" : $"`{s.Pg.Backends}` active\n`{s.Pg.LocksWaiting}` lock waits", inline: true)
                    .AddField("Contention", s.Pg is null ? "`n/a`" : $"`{s.Pg.Deadlocks:N0}` deadlocks\n`{s.Pg.Conflicts:N0}` conflicts\n`{s.Pg.TempFiles:N0}` temp files", inline: true)
                    .AddField("Tuples", s.Pg is null ? "`n/a`" : s.Rates is null
                        ? $"`{s.Pg.TupFetched:N0}` fetched total\n`{s.Pg.TupInserted + s.Pg.TupUpdated + s.Pg.TupDeleted:N0}` written total"
                        : $"`{s.Rates.Returned:N0}`/s scanned\n`{s.Rates.Fetched:N0}`/s fetched\n`{s.Rates.Inserted:N0}`i `{s.Rates.Updated:N0}`u `{s.Rates.Deleted:N0}`d /s", inline: true)
                    .Build(),
                "services" => new EmbedBuilder()
                    .WithAuthor($"Automated Services  -  {(s.Services.Count == 0 ? 100 : HealthPct((double)s.Services.Count(x => x.Status != "Stopped") / s.Services.Count))}% up")
                    .WithColor(HealthColor(s.Services.Count == 0 ? 1 : (double)s.Services.Count(x => x.Status != "Stopped") / s.Services.Count))
                    .WithDescription(s.Services.Count == 0 ? "No service runs recorded yet." : $"```\n{GetTable(BuildServiceTable(s.Services))}```")
                    .Build(),
                _ => new EmbedBuilder()
                    .WithAuthor($"System Load  -  {HealthPct(s.Worst)}% healthy")
                    .WithColor(HealthColor(s.Worst))
                    .WithDescription("Pick a section below for details.")
                    .AddField($"{HealthDot(s.RuntimeHealth)} Runtime", $"{HealthPct(s.RuntimeHealth)}%\n`{RuntimeMetrics.PerMinute(s.ApiCalls):F1}` API/min", inline: true)
                    .AddField($"{HealthDot(s.DiscordHealth)} Discord", $"{HealthPct(s.DiscordHealth)}%\n`{s.Latency}` ms, `{s.QHigh + s.QLow}` queued", inline: true)
                    .AddField($"{HealthDot(s.ProcessHealth)} Process", $"{HealthPct(s.ProcessHealth)}%\n`{s.WorkingMb:F0}` MB", inline: true)
                    .AddField($"{HealthDot(s.DbHealth)} Database", $"{HealthPct(s.DbHealth)}%\n`{s.Ping}` ms ping", inline: true)
                    .Build()
            };
        }

        private static bool IsEphemeral(IMessage m) => m?.Flags?.HasFlag(MessageFlags.Ephemeral) ?? false;

        private static MessageComponent SysLoadComponents(string section, bool autoRefreshing, bool ephemeral) {
            var menu = new SelectMenuBuilder()
                .WithCustomId("SysLoadNav")
                .WithPlaceholder("View section...")
                .AddOption("Overview", "overview", isDefault: section == "overview")
                .AddOption("Runtime Usage", "runtime", isDefault: section == "runtime")
                .AddOption("Discord", "discord", isDefault: section == "discord")
                .AddOption("Process", "process", isDefault: section == "process")
                .AddOption("Database", "database", isDefault: section == "database")
                .AddOption("Services", "services", isDefault: section == "services");
            var cb = new ComponentBuilder().WithSelectMenu(menu);
            if(autoRefreshing)
                cb.WithButton("Stop refreshing", customId: "SysLoadStop", style: ButtonStyle.Secondary, row: 1);
            else
                cb.WithButton("Refresh", customId: $"SysLoadRefresh:{section}", style: ButtonStyle.Primary, row: 1);
            if(!ephemeral)
                cb.WithButton("Dismiss", customId: "SysLoadDismiss", style: ButtonStyle.Danger, row: 1);
            return cb.Build();
        }

        [SlashCommand("sysload", "System load: runtime, Discord, DB, process (health-colored)")]
        [StaffOnly(StaffTier.FarmHand)]
        public async Task SysLoad(
            [Summary("refreshseconds", "Auto-refresh every N seconds (1-30, stops after 30s total)")] int refreshseconds = 0,
            [Summary("showinchannel", "Post visibly in the channel instead of only to you")] bool showinchannel = false) {

            await Context.Interaction.DeferAsync(ephemeral: !showinchannel);

            var queue = serviceProvider.GetService<IDiscordQueue>();
            var snap = await GatherSysLoad(Db, gateway, queue, serviceProvider);
            var refreshing = refreshseconds > 0;
            var interval = Math.Clamp(refreshseconds, 1, 30);

            var interactionStart = Context.Interaction;
            var message = await interactionStart.RespondAsyncGettingMessage(content: SysLoadContent(snap), embed: SysLoadSection("overview", snap),
                ephemeral: !showinchannel, components: SysLoadComponents("overview", refreshing, !showinchannel));
            if(!refreshing || message is null)
                return;

            var cts = new CancellationTokenSource();
            var session = new SysLoadSession { Cts = cts, Section = "overview" };
            _sysLoad[message.Id] = session;
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            var gatewayCapt = gateway;
            var spCapt = serviceProvider;
            var interaction = Context.Interaction;

            _ = Task.Run(async () => {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
                try {
                    while(!cts.IsCancellationRequested && DateTimeOffset.UtcNow < deadline) {
                        await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
                        if(cts.IsCancellationRequested) break;
                        await using var tickDb = await factory.CreateDbContextAsync();
                        var fresh = await GatherSysLoad(tickDb, gatewayCapt, queue, spCapt);
                        var sec = session.Section;
                        await interaction.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(fresh); x.Embed = SysLoadSection(sec, fresh); x.Components = SysLoadComponents(sec, true, !showinchannel); });
                    }
                } catch(OperationCanceledException) {
                } catch(Exception) {
                } finally {
                    _sysLoad.TryRemove(message.Id, out _);
                    try { await interaction.ModifyOriginalResponseAsync(x => x.Components = SysLoadComponents(session.Section, false, !showinchannel)); } catch { }
                }
            }, cts.Token);
        }

        [SlashCommand("dumpperms", "Dump this server's command permission overrides to a file")]
        [StaffOnly(StaffTier.Admin)]
        public async Task DumpPerms() {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var report = await CommandPermissionDump.BuildReportAsync(gateway, Context.Guild.Id);
            var bytes = Encoding.UTF8.GetBytes(report);

            await Context.Interaction.FollowupWithFileAsync(
                new FileAttachment(new MemoryStream(bytes), $"command-perms-{Context.Guild.Id}.txt"),
                text: "Command permission overrides attached.", ephemeral: true);
        }

        [ComponentInteraction("SysLoadNav", ignoreGroupNames: true)]
        public async Task SysLoadNav(string[] values) {
            await Context.Interaction.DeferAsync();
            var component = (SocketMessageComponent)Context.Interaction;
            var section = values.FirstOrDefault() ?? "overview";
            var refreshing = _sysLoad.TryGetValue(component.Message.Id, out var session);
            if(refreshing) session.Section = section;

            var snap = await GatherSysLoad(Db, gateway, serviceProvider.GetService<IDiscordQueue>(), serviceProvider);
            await component.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(snap); x.Embed = SysLoadSection(section, snap); x.Components = SysLoadComponents(section, refreshing, IsEphemeral(component.Message)); });
        }

        [ComponentInteraction("SysLoadRefresh:*", ignoreGroupNames: true)]
        public async Task SysLoadRefresh(string data) {
            await Context.Interaction.DeferAsync();
            var component = (SocketMessageComponent)Context.Interaction;
            var section = string.IsNullOrEmpty(data) ? "overview" : data;
            var refreshing = _sysLoad.ContainsKey(component.Message.Id);
            var snap = await GatherSysLoad(Db, gateway, serviceProvider.GetService<IDiscordQueue>(), serviceProvider);
            await component.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(snap); x.Embed = SysLoadSection(section, snap); x.Components = SysLoadComponents(section, refreshing, IsEphemeral(component.Message)); });
        }

        [ComponentInteraction("SysLoadStop", ignoreGroupNames: true)]
        public async Task SysLoadStop() {
            await Context.Interaction.DeferAsync();
            var component = (SocketMessageComponent)Context.Interaction;
            if(_sysLoad.TryGetValue(component.Message.Id, out var session))
                await session.Cts.CancelAsync();
        }

        [ComponentInteraction("SysLoadDismiss", ignoreGroupNames: true)]
        public async Task SysLoadDismiss() {
            var component = (SocketMessageComponent)Context.Interaction;
            if(_sysLoad.TryGetValue(component.Message.Id, out var session))
                await session.Cts.CancelAsync();
            try {
                await component.Message.DeleteAsync();
            } catch {
                try { await component.DeferAsync(); } catch { }
            }
        }
    }
}
