using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using MassTransit.Testing;
using MassTransit.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater(IServiceProvider provider) : _UpdaterBase<ThreadsCoopStatusUpdater>(interval, delay, provider) {
        private static readonly TimeSpan delay = BuildConfig.IsDebug ? TimeSpan.FromMinutes(0) : TimeSpan.FromMinutes(2);
        private static readonly TimeSpan interval = BuildConfig.IsDebug ? TimeSpan.FromMinutes(20) : TimeSpan.FromMinutes(15);
        private static readonly Random rand = new();
        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public async override Task Run(object state, CancellationToken cancellationToken) {
            using var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.AsQueryable().Where(x => x.ThreadID != 0 && !x.ThreadArchived && x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(7) > DateTimeOffset.UtcNow).ToListAsync(CancellationToken.None);

            // Users who leave their server have GuildId reset to 0, which would drop them from the
            // GuildId > 0 set below even while they are still in an active coop. Without their backup
            // the only handle on their coop-status entry is participant.UserId, which Egg Inc only
            // populates for the account that requested the status. GetStatus rotates that requester
            // randomly each cycle, so their xref match (and the 👽 emoji) flips on and off. Keep any
            // user still referenced by an active coop's xrefs so name/EB matching stays stable.
            var coopIds = coops.Select(x => x.Id).ToList();
            var activeCoopUserIds = await _db.UserCoopXrefs.Where(x => coopIds.Contains(x.CoopId)).Select(x => x.UserId).Distinct().ToListAsync(CancellationToken.None);

            var users = (await _db.DBUsers.Where(x => x.GuildId > 0 || activeCoopUserIds.Contains(x.Id)).AsQueryable().ToListAsync(CancellationToken.None)).SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
            var dbguilds = await _db.Guilds.AsNoTracking().ToListAsync(CancellationToken.None);

            if(BuildConfig.IsDebug) {
                //coops = [.. coops.Where(x => x.Name == "NapLure49")];
            }


            var completedCoops = 0;
            var throttler = new SemaphoreSlim(5);
            var guildCoopGroups = coops.GroupBy(x => x.OverflowGuildId > 0 ? x.OverflowGuildId : x.GuildId).OrderBy(x => rand.Next());
            foreach(var guildCoops in guildCoopGroups) {
                if(cancellationToken.IsCancellationRequested) break;
                var dbguild = dbguilds.FirstOrDefault(x => x.DiscordSeverId == guildCoops.Key || x.OverflowServers.Any(y => y == guildCoops.Key));
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == guildCoops.Key);
                var parentGuild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.Id);
                if(guild == null)
                    continue;
                await guild.DownloadUsersAsync();
                _logger.LogInformation("Coops for guild: {guildName}, Count {count}", guild.Name, guildCoops.Count());

                var tasks = new List<(Task task, DateTimeOffset started)>();

                var rng = new Random();
                var errors = 0;
                foreach(var coop in guildCoops.OrderBy(a => rng.Next())) {
                    while(_coopsBeingCreatedService.AreCoopsBeingCreated()) {
                        _logger.LogInformation("Sleeping while waiting on coop creation");
                        await Task.Delay(TimeSpan.FromMinutes(1));
                    }


                    if(cancellationToken.IsCancellationRequested) break;
                    await WaitOnCoopsBeingCreated(cancellationToken);

                    while(!await throttler.WaitAsync(20000, cancellationToken)) {
                        var incompleteTasks = tasks.Where(x => x.task.Status == TaskStatus.Running || x.task.Status == TaskStatus.WaitingForActivation || x.task.Status == TaskStatus.WaitingToRun);

                        _logger.LogInformation("Waiting on throttle, {info}", string.Join(", ", incompleteTasks.Select(x => $"{x.task.Id} {x.task.Status} {x.task.Exception?.Message} {x.task.IsCanceled} {x.task.IsFaulted} {x.task.IsCompleted} {(DateTimeOffset.UtcNow - x.started).Humanize()}")));

                    }

                    tasks.Add((Task.Run(async () => {
                        var status = false;
                        try {
                            var sw = new Stopwatch();
                            sw.Start();

                            using var perCoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            perCoopCts.CancelAfter(TimeSpan.FromMinutes(1));

                            status = await ProcessCoop(coop.Id, guild, parentGuild, users, dbguild, perCoopCts.Token);
                            sw.Stop();
                            var completed = Interlocked.Increment(ref completedCoops);
                        } finally {
                            throttler.Release();
                        }
                        if(!status)
                            errors++;
                    }, cancellationToken), DateTimeOffset.UtcNow));

                    StillAlive();
                    await Task.Delay(500, cancellationToken);
                }

                var watchdogCancellationSource = new CancellationTokenSource();
                var watchdogCancelToken = watchdogCancellationSource.Token;
                var watchdogTask = Task.Delay(TimeSpan.FromMinutes(10), watchdogCancelToken);
                var allTasks = Task.WhenAll(tasks.Select(x => x.task));
                var completedTask = await Task.WhenAny(allTasks, watchdogTask);

                if(completedTask == watchdogTask) { // Timeout occurred
                    watchdogCancellationSource.Cancel();
                    _logger.LogWarning("Watchdog Task Called");
                }



                _logger.LogInformation("Co-op Count: {count}, Successful: {successful}, Error: {errors}, Guild: {guild}", guildCoops.Count(), tasks.Count() - errors, errors, guild.Name);
            }
        }

        public async Task<bool> ProcessCoop(Guid coopId, SocketGuild guild, SocketGuild parentGuild, List<UserWithBackup> users, Guild dbGuild, CancellationToken cancellationToken) {
            var timings = new TimingsFactory(null);
            timings.Start();
            var ctx = new CoopProcessingContext(coopId, guild, parentGuild, users, dbGuild, timings, cancellationToken);
            try {
                timings.Set("Pre-Setup");
                using var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
                ctx.Db = db;

                if(!await LoadCoop(ctx)) return false;
                if(!await ResolveThread(ctx)) return false;
                if(!await FetchStatus(ctx)) return false;
                if(!await SyncLeagueAndAttemptRestart(ctx)) return false;

                if(cancellationToken.IsCancellationRequested) return false;

                await BuildCoopDetails(ctx);

                FixChannelPermissions(ctx);
                await HandleCreatorNotKicked(ctx);
                await ReconcileMissingXrefs(ctx);

                await PopulateUserStatuses(ctx);
                await ReconcileUsersWithoutXref(ctx);
                await ProcessSleepingParticipants(ctx);
                await UpdateCoopLifecycleStatus(ctx);
                UpdateCurrentUserCount(ctx);

                ctx.Msgs = GetStatusStringAsync(ctx.CoopDetails, ctx.Coop.Contract);
                ctx.LastMessage = "";
                ctx.Timings.Set(5);

                await SyncChannelMembership(ctx);
                await SendCoopCreatedDms(ctx);
                await CompileAndSendChannelPings(ctx);
                MarkChannelPermissionsAdded(ctx);

                await ProcessUnjoinedParticipants(ctx);

                AppendGiftingSuggestions(ctx);
                AppendCommandLinks(ctx);
                AppendGradeWarningAndCheckIn(ctx);
                await ReportTimeCheaters(ctx);

                ApplyDeflectorFlags(ctx);
                await ProcessTachyonSuggestions(ctx);

                ApplyFullStatus(ctx);
                await ApplyFailedStatus(ctx);

                await SendLifecycleNotifications(ctx);

                ctx.Coop.LastStatusUpdate = ctx.Status;
                if(!ctx.Coop.FinalizedFinishedOrFailed() || ctx.FinalChannelUpdate) {
                    BuildEmojisAndColor(ctx);
                    await UpdateThreadName(ctx);
                    await BuildStatusEmbed(ctx);
                    ctx.Timings.Set(9);
                    await UpdateChannel(ctx.Msgs, ctx.EmbedBuilder.Build(), ctx.CoopThread, ctx.Coop, ctx.StatusResponse.DiscordMessages);
                }

                ctx.Coop.LastUpdateToChannel = DateTimeOffset.UtcNow;
                await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);

                var times = ctx.Timings.Finished();

                _logger.LogTrace("Co-op timings {timings} - {coop}", string.Join(",", times.Select(x => $"{x.name}:{x.time.Humanize().ShortenTime()}")), ctx.Coop.Name);
            } catch(Exception e) {
                _logger.LogError(e, "Error in co-op {coopid}", ctx.CoopName ?? coopId.ToString());
                _bugSnag.Notify(e);
                return false;
            }

            return true;
        }

        private async Task CheckForCoopCreatorStillIn(Coop coop, ContractCoopStatusResponse status) {
            if(!EggIncApi.CoopCreatorIds.Any(x => x.EggIncId == coop.CreatorID))
                return;
            if(status.Contributors.Any(x => x.UserId == coop.CreatorID)) {
                _logger.LogError("Coop creator {creator} is still in coop {coop}", coop.CreatorID, coop.Name);
            }
        }

        public class UserWithStatus {
            public CustomBackup Backup { get; set; }
            public ContractCoopStatusResponse.Types.ContributionInfo Status { get; set; }
            public DBUser User { get; set; }
            public TimeSpan? Sleeping { get; set; }
            public UserCoopXref Xref { get; set; }
            public SocketGuildUser DiscordUser { get; set; }
            public double SiloTime { get; set; }
            public CustomFarmStats FarmStats { get; set; }
        }

        public static string Truncate(string value, int maxLength) {
            if(string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value[..maxLength];
        }




    }
}
