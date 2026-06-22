using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Interactions;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public partial class AdminModule {

        [SlashCommand("botstatus", "One-look bot/DB/deploy/load status")]
        public async Task BotStatus(CoopStatsCache stats) {
            await Context.Interaction.DeferAsync(ephemeral: true);

#if DEBUG
            var buildConfig = "Debug";
#elif DEV9001
            var buildConfig = "DEV9001";
#elif DEV9002
            var buildConfig = "DEV9002";
#elif RELEASE
            var buildConfig = "Release";
#else
            var buildConfig = "Unknown";
#endif

            var proc = Process.GetCurrentProcess();
            var uptime = (DateTime.Now - proc.StartTime).Humanize();

            var updaters = serviceProvider.GetServices<IHostedService>().OfType<IUpdaterService>().ToList();
            var runningServices = updaters.Count(x => x.Running());

            var sw = Stopwatch.StartNew();
            await Db.Database.ExecuteSqlRawAsync("SELECT 1");
            var pingMs = sw.ElapsedMilliseconds;

            var trackerEntries = Db.ChangeTracker.Entries().ToList();
            var pending = trackerEntries.Count(e => e.State is Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);

            var workingMb = proc.WorkingSet64 / 1_048_576.0;
            var gcHeapMb = GC.GetTotalMemory(false) / 1_048_576.0;
            var cacheCount = Db._cache is MemoryCache mc ? mc.Count : -1;

            var statsAge = stats.LastRefresh is { } t ? (DateTimeOffset.UtcNow - t).Humanize().ShortenTime() : "never";
            var server = Context.Guild?.Id is ulong gid ? stats.GetServerStats(gid) : null;

            var rows = new List<List<FixedWidthCell>> {
                new() { new("Build"), new(buildConfig, CellAlignment.Right) },
                new() { new("Uptime"), new(uptime, CellAlignment.Right) },
                null,
                new() { new("Services Up"), new($"{runningServices}/{updaters.Count}", CellAlignment.Right) },
                null,
                new() { new("DB Ping"), new($"{pingMs} ms", CellAlignment.Right) },
                new() { new("Tracked"), new($"{trackerEntries.Count}", CellAlignment.Right) },
                new() { new("Pending"), new($"{pending}", CellAlignment.Right) },
                null,
                new() { new("Working Set"), new($"{workingMb:F1} MB", CellAlignment.Right) },
                new() { new("GC Heap"), new($"{gcHeapMb:F1} MB", CellAlignment.Right) },
                new() { new("GC (0/1/2)"), new($"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}", CellAlignment.Right) },
                new() { new("Cache"), new(cacheCount >= 0 ? $"{cacheCount}" : "n/a", CellAlignment.Right) },
                new() { new("Stats Age"), new(statsAge, CellAlignment.Right) },
                null,
                new() { new("Active Contracts"), new($"{server?.ActiveContracts ?? 0}", CellAlignment.Right) },
                new() { new("Active Coops"), new($"{server?.ActiveCoops ?? 0}", CellAlignment.Right) },
                new() { new("Pending Threads"), new($"{server?.PendingThreads ?? 0}", CellAlignment.Right) },
                new() { new("Players In Coops"), new($"{server?.UsersAssigned ?? 0}", CellAlignment.Right) },
            };

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = $"```\n{GetTable(rows)}```"; });
        }

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
            if(h >= 0.5) { r = (int)Math.Round((1 - h) * 2 * 220); g = 200; }
            else { r = 220; g = (int)Math.Round(h * 2 * 200); }
            return new Color(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), 0);
        }

        private static string HealthDot(double h) => h >= 0.8 ? "\U0001F7E2" : h >= 0.5 ? "\U0001F7E1" : "\U0001F534";
        private static int HealthPct(double h) => (int)Math.Round(Math.Clamp(h, 0, 1) * 100);

        private sealed record SysLoadSnapshot(
            long Ping, double WorkingMb, double GcHeapMb, int Threads, double CpuMin, int CacheCount,
            int Tracked, int Pending, int ActiveCoops, int DbUsers, int Contracts, int Events, int AutoLogs,
            long ApiCalls, long ApiFails, long DbQueries, long Commands, long CmdFails, long DiscordOps,
            int Latency, int Guilds, int QHigh, int QLow, int QHighW, int QLowW,
            double RuntimeHealth, double DiscordHealth, double ProcessHealth, double DbHealth,
            long StartedUnix, long NowUnix) {
            public double Worst => Math.Min(Math.Min(RuntimeHealth, DiscordHealth), Math.Min(ProcessHealth, DbHealth));
        }

        private static async Task<SysLoadSnapshot> GatherSysLoad(ApplicationDbContext db, DiscordSocketClient client, IDiscordQueue queue) {
            var sw = Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            var pingMs = sw.ElapsedMilliseconds;

            var proc = Process.GetCurrentProcess();
            var workingMb = proc.WorkingSet64 / 1_048_576.0;
            var gcHeapMb = GC.GetTotalMemory(false) / 1_048_576.0;
            var cacheCount = db._cache is MemoryCache mc ? mc.Count : -1;
            var pending = db.ChangeTracker.Entries().Count(e => e.State is Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);
            var tracked = db.ChangeTracker.Entries().Count();

            var activeCoops = await db.Coops.CountAsync(x => !x.Finished && x.CoopEnds > DateTimeOffset.UtcNow);
            var dbUsers = await db.DBUsers.CountAsync();
            var contracts = await db.Contracts.CountAsync();
            var events = await db.Events.CountAsync();
            var autoLogs = await db.AutomationLogs.CountAsync(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-1));

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
            var dbHealth = Math.Min(HealthRange(pingMs, 50, 500), HealthRange(pending, 25, 250));

            return new SysLoadSnapshot(pingMs, workingMb, gcHeapMb, proc.Threads.Count, proc.TotalProcessorTime.TotalMinutes, cacheCount,
                tracked, pending, activeCoops, dbUsers, contracts, events, autoLogs,
                apiCalls, apiFails, RuntimeMetrics.DbQueries, commands, cmdFails, RuntimeMetrics.DiscordOps,
                latency, guilds, qHigh, qLow, queue?.HighWorkers ?? 0, queue?.LowWorkers ?? 0,
                runtimeHealth, discordHealth, processHealth, dbHealth,
                RuntimeMetrics.StartedAt.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        private static string SysLoadContent(SysLoadSnapshot s) =>
            $"-# Counters since <t:{s.StartedUnix}:R> · updated <t:{s.NowUnix}:R>";

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
                    .AddField("DB Ping", $"`{s.Ping}` ms", inline: true)
                    .AddField("Tracked", $"`{s.Tracked}`", inline: true)
                    .AddField("Pending", $"`{s.Pending}`", inline: true)
                    .AddField("Mem Cache", s.CacheCount >= 0 ? $"`{s.CacheCount}`" : "`n/a`", inline: true)
                    .AddField("DBUsers", $"`{s.DbUsers:N0}`", inline: true)
                    .AddField("Active Coops", $"`{s.ActiveCoops:N0}`", inline: true)
                    .AddField("Contracts", $"`{s.Contracts:N0}`", inline: true)
                    .AddField("Events", $"`{s.Events:N0}`", inline: true)
                    .AddField("AutoLogs 24h", $"`{s.AutoLogs:N0}`", inline: true)
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
                .AddOption("Database", "database", isDefault: section == "database");
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
        public async Task SysLoad(
            [Summary("refreshseconds", "Auto-refresh every N seconds (1-30, stops after 30s total)")] int refreshseconds = 0,
            [Summary("showinchannel", "Post visibly in the channel instead of only to you")] bool showinchannel = false) {

            await Context.Interaction.DeferAsync(ephemeral: !showinchannel);

            var queue = serviceProvider.GetService<IDiscordQueue>();
            var snap = await GatherSysLoad(Db, gateway, queue);
            var refreshing = refreshseconds > 0;
            var interval = Math.Clamp(refreshseconds, 1, 30);

            var interactionStart = (SocketInteraction)Context.Interaction;
            var message = await interactionStart.RespondAsyncGettingMessage(content: SysLoadContent(snap), embed: SysLoadSection("overview", snap),
                ephemeral: !showinchannel, components: SysLoadComponents("overview", refreshing, !showinchannel));
            if(!refreshing || message is null)
                return;

            var cts = new CancellationTokenSource();
            var session = new SysLoadSession { Cts = cts, Section = "overview" };
            _sysLoad[message.Id] = session;
            var factory = serviceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext>>();
            var gatewayCapt = gateway;
            var interaction = Context.Interaction;

            _ = Task.Run(async () => {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
                try {
                    while(!cts.IsCancellationRequested && DateTimeOffset.UtcNow < deadline) {
                        await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
                        if(cts.IsCancellationRequested) break;
                        await using var tickDb = await factory.CreateDbContextAsync();
                        var fresh = await GatherSysLoad(tickDb, gatewayCapt, queue);
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

        [ComponentInteraction("SysLoadNav", ignoreGroupNames: true)]
        public async Task SysLoadNav(string[] values) {
            await Context.Interaction.DeferAsync();
            var component = (SocketMessageComponent)Context.Interaction;
            var section = values.FirstOrDefault() ?? "overview";
            var refreshing = _sysLoad.TryGetValue(component.Message.Id, out var session);
            if(refreshing) session.Section = section;

            var snap = await GatherSysLoad(Db, gateway, serviceProvider.GetService<IDiscordQueue>());
            await component.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(snap); x.Embed = SysLoadSection(section, snap); x.Components = SysLoadComponents(section, refreshing, IsEphemeral(component.Message)); });
        }

        [ComponentInteraction("SysLoadRefresh:*", ignoreGroupNames: true)]
        public async Task SysLoadRefresh(string data) {
            await Context.Interaction.DeferAsync();
            var component = (SocketMessageComponent)Context.Interaction;
            var section = string.IsNullOrEmpty(data) ? "overview" : data;
            var refreshing = _sysLoad.ContainsKey(component.Message.Id);
            var snap = await GatherSysLoad(Db, gateway, serviceProvider.GetService<IDiscordQueue>());
            await component.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(snap); x.Embed = SysLoadSection(section, snap); x.Components = SysLoadComponents(section, refreshing, IsEphemeral(component.Message)); });
        }

        [ComponentInteraction("SysLoadStop", ignoreGroupNames: true)]
        public async Task SysLoadStop() {
            await Context.Interaction.DeferAsync();
            var component = (SocketMessageComponent)Context.Interaction;
            if(_sysLoad.TryGetValue(component.Message.Id, out var session))
                session.Cts.Cancel();
        }

        [ComponentInteraction("SysLoadDismiss", ignoreGroupNames: true)]
        public async Task SysLoadDismiss() {
            var component = (SocketMessageComponent)Context.Interaction;
            if(_sysLoad.TryGetValue(component.Message.Id, out var session))
                session.Cts.Cancel();
            try {
                await component.Message.DeleteAsync();
            } catch {
                try { await component.DeferAsync(); } catch { }
            }
        }
    }
}
