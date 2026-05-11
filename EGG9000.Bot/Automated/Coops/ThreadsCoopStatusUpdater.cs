using Discord;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiStatics;
using EGG9000.Common.Services;

using Ei;

using Humanizer;

using MassTransit.Testing;
using MassTransit.Util;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using Polly;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.DiscordHelpersExt;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public class ThreadsCoopStatusUpdater(IServiceProvider provider) : _UpdaterBase<ThreadsCoopStatusUpdater>(interval, delay, provider) {
#if DEBUG
        private static readonly TimeSpan delay = TimeSpan.FromMinutes(0);
        private static readonly TimeSpan interval = TimeSpan.FromMinutes(20);
#else
        private static readonly TimeSpan delay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan interval = TimeSpan.FromMinutes(15);
#endif
        private readonly Dictionary<ulong, SocketTextChannel> _demeritChannels = [];
        private static Random rand = new Random();

        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public async override Task Run(object state, CancellationToken cancellationToken) {
            using var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = (await _db.DBUsers.Where(x => x.GuildId > 0).AsQueryable().ToListAsync(CancellationToken.None)).SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
            var coops = await _db.Coops.AsQueryable().Where(x => x.ThreadID != 0 && x.DiscordChannelId == 0 && !x.ThreadArchived && x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(7) > DateTimeOffset.Now).ToListAsync(CancellationToken.None);
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);

#if DEBUG
            //coops = [.. coops.Where(x => x.Id == Guid.Parse("eb1353a9-32ae-4c03-e379-08de4a63aaaf"))];
            //coops = coops.Where(x => x.Created > DateTimeOffset.Now.AddDays(-1) && x.GuildId == 656455567858073601 && x.OverflowGuildId == 1147264073659064420).ToList();
            //coops = coops.Where(x => x.GuildId == 1094314306767695984).ToList();
            //coops = coops.Where(x => x.Id == Guid.Parse("867c05a4-c7cd-420d-17c5-08dd4d5c76be")).ToList();
            //coops = coops.Take(20).ToList();
            coops = [.. coops.Where(x => x.Name == "PaperFetch64")];
            //coops = [.. coops.Where(x => x.Name.EndsWith("fix") && x.League == 4)];
            //coops = [.. coops.Where(x => !x.SuccessfullyStarted)];
#endif


            var completedCoops = 0;
#if DEBUG
            var throttler = new SemaphoreSlim(1);
#else
            var throttler = new SemaphoreSlim(10);
#endif
            var guildCoopGroups = coops.GroupBy(x => x.OverflowGuildId > 0 ? x.OverflowGuildId : x.GuildId).OrderBy(x => x.Count());
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
                foreach(var coop in guildCoops.OrderBy(a => rng.Next())) {
                    if(cancellationToken.IsCancellationRequested) break;
                    await WaitOnCoopsBeingCreated(cancellationToken);

                    while(!await throttler.WaitAsync(20000, cancellationToken)) {
                        var incompleteTasks = tasks.Where(x => !x.task.IsCompletedSuccessfully);

                        _logger.LogInformation("Waiting on throttle, {info}", string.Join(", ", incompleteTasks.Select(x => $"{x.task.Id} {x.task.Status} {x.task.Exception?.Message} {x.task.IsCanceled} {x.task.IsFaulted} {x.task.IsCompleted} {(DateTimeOffset.Now - x.started).Humanize()}")));

                    }
                    tasks.Add((Task.Run(async () => {
                        try {
                            var sw = new Stopwatch();
                            sw.Start();

                            var ct = new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;

                            await ProcessCoop(coop.Id, guild, parentGuild, users, dbguild, cancellationToken).WaitAsync(ct);
                            sw.Stop();
                            var completed = Interlocked.Increment(ref completedCoops);
                            //_logger.LogInformation("Finished processing {coopName}, Time: {time} ({completed} of {total})", coop.Name, sw.Elapsed.Humanize(), completed, coops.Count);
                        } finally {
                            throttler.Release();
                        }
                    }, cancellationToken), DateTimeOffset.Now));

                    StillAlive();
                    await Task.Delay(5000, cancellationToken);
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



                _logger.LogInformation("Co-op Count: {count}, Successful: {successful}, Error: {errors}, Guild: {guild}", guildCoops.Count(), tasks.Count(x => !x.task.IsFaulted), tasks.Count(x => x.task.IsFaulted), guild.Name);
            }
        }

        public async Task ProcessCoop(Guid coopId, SocketGuild guild, SocketGuild parentGuild, List<UserWithBackup> users, Guild dbGuild, CancellationToken cancellationToken) {
            var timings = new TimingsFactory(null);
            timings.Start();
            string coopName = null;
            try {
                timings.Set("Pre-Setup");
                using var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var slashCommands = await guild.GetCachedApplicationCommands();

                //** Get Coop
                var coop = await _db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).FirstOrDefaultAsync(x => x.Id == coopId, cancellationToken);
                if(coop == null) {
                    _logger.LogWarning("Unable to find co-op with id {coopid}", coopId);
                    return;
                }


                if(coop.ContractID == "test-contract") {
                    return;
                }

                //** Get Coop Thread
                IThreadChannel coopThread = guild.ThreadChannels.FirstOrDefault(x => x.Id == coop.ThreadID);

                if(coopThread == null) {
                    var restguild = await _client.Rest.GetGuildAsync(guild.Id);
                    try {

                        var coopHeaderChannel = await restguild.GetTextChannelAsync(coop.ThreadParentChannel);
                        if(coopHeaderChannel != null) {
                            coopThread = (await coopHeaderChannel.GetActiveThreadsAsync()).FirstOrDefault(t => t.Id == coop.ThreadID);
                        }
                    } catch(Exception) { }
                }


                if(coopThread == null) {
                    coopThread = guild.ThreadChannels.FirstOrDefault(x => x.Name.EndsWith(coop.Name));
                    _logger.LogWarning("Co-op thread ID has changed for {coop}", coop.Name);
                }

                if(coopThread == null) {
                    _logger.LogWarning("ERROR FINDING THREAD FOR CO-OP: {coopName}", coop.Name);
                    return;
                }


                //** Send Co-op has been created DM
                foreach(var xref in coop.UserCoopsXrefs) {
                    var user = users.FirstOrDefault(x => x.User.Id == xref.UserId);
                    if(xref.CoopSetting is null && user is not null) {
                        xref.CoopSetting = new CoopSetting(xref, user.User, dbGuild);
                        if(xref.CoopSetting.PingOnCoopCreated && !xref.JoinedCoop) {
                            await SendDMWarning(_db, parentGuild.GetUser(user.User.DiscordId), coopThread, "Co-op has been created", coop);
                            xref.CoopSetting.PingOnCoopCreated = false;
                        }
                        xref.UpdateCoopSetting();
                    }
                }
                await _db.SaveChangesAsync(CancellationToken.None);

                //Make sure the thread isn't archived before continuing
                if(coopThread.IsArchived) {
                    try {
                        await coopThread.ModifyAsync(t => t.Archived = false);
                    } catch(Exception) {
                        _logger.LogError("Could not un-archive thread for {coop}.", coop.Name);
                        return;
                    }
                }

                var coopDiscordUsers = coopThread is SocketTextChannel channel ? channel.Users.ToList().Select(x => (IGuildUser)x).Select(u => u.Id).Distinct().ToList() : coop.UserCoopsXrefs.Where(u => u.AddedToChannel).Select(u => u.User.DiscordId).Distinct().ToList();


                timings.Set("GetStatus");

                var statusReponse = new StatusResponse();
                try {
                    statusReponse = await GetStatus(coop, coopThread, cancellationToken);
                } catch(TaskCanceledException) {
                    _logger.LogWarning("Timeout getting status for {coopName}", coop.Name);
                    return;
                }

                timings.Set("Got status");


                ////** Handle coop bot being started
                //if(!coop.AddedFromBackup && !coop.SuccessfullyStarted && (statusReponse.Status is null || statusReponse.Status.ResponseStatus == Ei.ContractCoopStatusResponse.Types.ResponseStatus.CoopNotFound)) {
                //    var messages = await (coopThread as SocketTextChannel).GetMessagesAsync().FlattenAsync();
                //    _logger.LogCritical("Status is null and there are no channel messages for co-op: {coopName}, attempting to start.", coop.Name);
                //    string EIID = null;
                //    var random = new Random();
                //    foreach(var account in coop.UserCoopsXrefs.OrderBy(x => random.Next())) {
                //        var r = await ContractsAPI.Post<Ei.ContractPlayerInfo, Ei.BasicRequestInfo>(new Ei.BasicRequestInfo(), account.EggIncId);
                //        if(r.Grade == (Ei.Contract.Types.PlayerGrade)coop.League) {
                //            EIID = account.EggIncId;
                //            break;
                //        }
                //    }

                //    var result = await CreateCoopsV2.CreateCoopViaApi(coop.ContractID, (Ei.Contract.Types.PlayerGrade)coop.League, coop.Name, coop.Contract.Details.LengthSeconds, EIID, coop.AnyLeague);
                //    _logger.LogInformation($"Attempting to create coop for {coop.Name}, Result: {result}");
                //    return;
                //}
                var status = statusReponse.Status;

                if(status is null) {
                    _logger.LogWarning($"Status for {coop.Name} is null");
                    return;
                }

                if(!coop.SuccessfullyStarted && statusReponse.Status.Success) {
                    coop.SuccessfullyStarted = true;
                    await _db.SaveChangesAsync(CancellationToken.None);
                }

                await CheckForCoopCreatorStillIn(coop, status);


                if(coop.League != (uint)status.Grade) {
                    _logger.LogInformation("Updating co-op league: {coopName} from {oldLeague} to {newLeague}", coop.Name, (Ei.Contract.Types.PlayerGrade)coop.League, status.Grade);
                    coop.League = (uint)status.Grade;
                }
                if(coop.League == 0) {
                    _logger.LogWarning("{coopName} is returning Grade as 0", coopName);
                    return;
                } else if(status.SecondsRemaining == coop.Contract.Details.GradeSpecs[(int)coop.League - 1].LengthSeconds) {
                    //Attempt to fix not started co-op
                    _logger.LogInformation("Attempting to start co-op: {coopName}", coop.Name);

                    var joinResponse = await ContractsAPI.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest {
                        ContractIdentifier = coop.ContractID,
                        CoopIdentifier = coop.Name.ToLower(),
                        UserId = coop.CreatorID, ClientVersion = ContractsAPI.ClientVersion, Eop = 1, SoulPower = 24, Grade = (Ei.Contract.Types.PlayerGrade)coop.League, Platform = Ei.Platform.Droid, SecondsRemaining = coop.Contract.Details.LengthSeconds, PointsReplay = false, UserName = "."
                    }, coop.CreatorID, false);


                    var statusUpdate = new Ei.ContractCoopStatusUpdateRequest {
                        ContractIdentifier = coop.ContractID,
                        CoopIdentifier = coop.Name.ToLower(),
                        Eop = 1, SoulPower = 24, UserId = coop.CreatorID, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = coop.CreatorID, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                        ProductionParams = new Ei.FarmProductionParams {
                            FarmPopulation = 1, Delivered = 1, Elr = 1, FarmCapacity = 1, Ihr = 1, Sr = 1
                        }
                    };

                    var response = await ContractsAPI.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(statusUpdate, statusUpdate.UserId, false);


                    await Task.Delay(1000, cancellationToken);
                    var checkStatus = await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name.ToLower(), coop.CreatorID, cancellationToken: cancellationToken);


                    var kickPlayer = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
                        ClientVersion = ContractsAPI.ClientVersion,
                        ContractIdentifier = coop.ContractID,
                        CoopIdentifier = coop.Name.ToLower(),
                        PlayerIdentifier = coop.CreatorID,
                        Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                        RequestingUserId = coop.CreatorID
                    }, coop.CreatorID);
                }

                var finalChannelUpdate = false;

                if(cancellationToken.IsCancellationRequested) return;

                var customEggs = await _db.GetCustomEggsAsync();

                if(coop.League == 0) {
                    //Fix if grade is set to 0
                    coop.League = (uint)status.Grade;
                }

                var coopDetails = new CoopDetails(coop, coop.Contract, coop.League, users, customEggs, _client, statusReponse.Status);


                if(CheckForCreator(coop, coopDetails)) {
                    await _db.SaveChangesAsync();
                }


                //** Verify if people have access to the parent channel
                var gradeRoleEnum = coop.League switch {
                    5 => GuildChannelType.GradeAAA,
                    4 => GuildChannelType.GradeAA,
                    3 => GuildChannelType.GradeA,
                    2 => GuildChannelType.GradeB,
                    1 => GuildChannelType.GradeC,
                    _ => GuildChannelType.General,
                };
                SocketRole gradeRole = null;
                if(gradeRoleEnum != GuildChannelType.General) {
                    gradeRole = await _client.GetRoleAsync(gradeRoleEnum, guild);
                }
                foreach(var participant in coopDetails.CoopParticipants.Where(x => x.DBUser is not null)) {
                    if(gradeRole is null) continue;
                    var overflowGuildUser = guild.GetUser(participant.DBUser.DiscordId);
                    if(overflowGuildUser is not null && !overflowGuildUser.Roles.Any(x => x.Id == gradeRole.Id || x.Name.Contains("ULTRA")) && participant.CoopStatus?.UserName != "[departed]") {
                        var headChannel = guild.GetTextChannel(coopThread.CategoryId.Value);
                        if(!headChannel.PermissionOverwrites.Any(x => x.TargetId == overflowGuildUser.Id)) {
                            await headChannel.AddPermissionOverwriteAsync(overflowGuildUser, new OverwritePermissions(viewChannel: PermValue.Allow));

                            if(!coop.FinishedOrFailedOrExpired()) {
                                await coopThread.SendMessageAsync($"Fixing permission for {overflowGuildUser.Mention}");
                            }
                        }

                    }

                }

                //** Handle creation account not being kicked from co-op
                if(coopDetails.CoopParticipants.Any(x => x.Account?.Id == ContractsAPI.UserId) && !coop.FinishedOrFailedOrExpired()) {
                    var success = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest { Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private, ClientVersion = ContractsAPI.ClientVersion, ContractIdentifier = coop.ContractID, CoopIdentifier = coop.Name, PlayerIdentifier = ContractsAPI.UserId, RequestingUserId = ContractsAPI.UserId, Rinfo = ContractsAPI.GetInfo(ContractsAPI.UserId) }, ContractsAPI.UserId);
                    _logger.LogInformation("Attempted to kick co-op creator to free up spot for {co-op}, it returned {status}", coop.Name, success.ToString());
                }

                var participantsInCoopButWithoutXref = coopDetails.CoopParticipants.Where(x =>
                    x.DBUser is not null &&
                    x.Xref is null &&
                    x.CoopStatus is not null &&
                    x.Backup.Farms.Any(f => f.CoopId is not null && f.CoopId.Equals(coop.Name, StringComparison.CurrentCultureIgnoreCase))
                ).ToList();
                foreach(var participant in participantsInCoopButWithoutXref) {
                    var xref = new UserCoopXref {
                        EggIncId = participant.Backup.EggIncId,
                        CreatedOn = DateTimeOffset.Now,
                        JoinedCoop = true,
                        UserId = participant.DBUser.Id,
                        WasAssigned = false,
                        CoopId = coop.Id
                    };
                    _db.Add(xref);
                    participant.AddXref(xref);
                }
                if(participantsInCoopButWithoutXref.Count > 0)
                    await _db.SaveChangesAsync(CancellationToken.None);
                foreach(var participant in participantsInCoopButWithoutXref) {
                    if(coop.UserCoopsXrefs.Any(x => x.UserId == participant.DBUser.Id && x.WasAssigned && !x.JoinedCoop)) {
                        await coopThread.SendMessageAsync($"<@{participant.DBUser.DiscordId}>, it looks like you might have joined the coop with the wrong account.");
                        await BoolSendDm(participant.DiscordUser, $"It looks like you might have joined the coop with the wrong account in {coopThread.Mention}.", _db);
                    } else {
                        await coopThread.SendMessageAsync($"<@{participant.DBUser.DiscordId}> has joined the co-op");
                    }
                }



                timings.Set(1);


                // Set time joined so we can later track and alert when a new BG might be worth it.
                coopDetails.CoopParticipants.Where(x => x.Xref is not null && x.Xref?.Joined is null && x.CoopStatus is not null).ToList().ForEach(x => x.Xref.Joined = DateTimeOffset.UtcNow);


                var usersWithStatus = coopDetails.CoopParticipants.Select(x => new UserWithStatus {
                    Status = x.CoopStatus,
                    Xref = x.Xref,
                    User = x.DBUser,
                    Backup = x.Backup,
                    DiscordUser = x.DBUser is not null ? parentGuild.GetUser(x.DBUser.DiscordId) : null
                }).ToList();


                await CheckDeflectorChange(coop.LastStatusUpdate, status, coop, usersWithStatus, coopThread, _db);

                timings.Set("1.1");
                var usersNotJoined = coopDetails.CoopParticipants.Where(x => x.CoopStatus is null).ToList();

                foreach(var user in usersWithStatus) {
                    if(user.Backup != null) {
                        var awayTime = Research.GetTotalSiloCapacity(user.Backup);
                        var farm = user.Backup?.Farms?.FirstOrDefault(x => x.CoopId == coop.Name.ToLower());
                        if(farm != null) {
                            _bugSnag.Breadcrumbs.Leave($"User: {user.DiscordUser?.Id}, {user.Backup?.EggIncId}");
                            user.FarmStats = farm.WithStats(user.Backup, coop, customEggs);
                            user.SiloTime = awayTime * farm.SilosOwned;
                            var siloTimeHours = user.SiloTime / 60;
                            if(user.Xref is not null && user.Xref.SiloTimeHours != siloTimeHours) {
                                user.Xref.SiloTimeHours = (float)siloTimeHours;
                            }
                        }
                    }

                    if(user.Xref != null) {
                        user.Xref.LastStatus = user.Status is not null ? new ContributionInfoCompact(user.Status) : null;
                    }

                }


                timings.Set(2);


                //Handle User Joining Without Xref
                var usersWithoutXref = coopDetails.CoopParticipants.Where(x => x.DBUser is not null && x.Xref is null);
                List<ulong> usersNeedingChannelPermissions = [];
                foreach(var user in usersWithoutXref) {
                    if(!coopDiscordUsers.Any(x => x == user.DBUser.DiscordId)) {
                        usersNeedingChannelPermissions.Add(user.DBUser.DiscordId);
                    } else {
                        var xref = new UserCoopXref {
                            WaitingOnStarter = false,
                            UserId = user.DBUser.Id,
                            EggIncId = user.Backup.EggIncId,
                            AddedToChannel = false,
                            CoopId = coop.Id,
                            CreatedOn = DateTimeOffset.UtcNow,
                            JoinedCoop = true,
                            Starter = false,
                            LastStatus = user.CoopStatus is not null ? new ContributionInfoCompact(user.CoopStatus) : null,
                            WasAssigned = false
                        };
                        _db.UserCoopXrefs.Add(xref);
                        if(coop.UserCoopsXrefs.Any(x => x.UserId == user.DBUser.Id && x.WasAssigned && !x.JoinedCoop)) {
                            await WrongAccountWarning(user, coopThread, _db, user.Backup.EggIncId);
                        } else {
                            await coopThread.SendMessageAsync($"<@{user.DBUser.DiscordId}> has joined the co-op");
                        }
                    }
                }

                timings.Set(3);

                foreach(var participant in coopDetails.CoopParticipants) {
                    await HandleSleeping(participant, coopThread, coop, _db, dbGuild);
                }

                var league = (int?)coop.League ?? 0;
                var targetAmount = coop.Contract.Details.GetGoals(league).Max(x => x.TargetAmount);
                var amountWithOffline = coopDetails.CoopParticipants.Where(x => x.CoopStatus is not null).Sum(x => x.EggsShipped + x.OfflineEggs);
                var remainingAmount = targetAmount - amountWithOffline;
                var totalRate = status.Participants.Sum(x => x.ContributionRate);

                var timeRemaining = GetTimeRemainingValue(targetAmount, totalRate, amountWithOffline);


                var waitingOn = usersWithStatus.Where(x => !x.Status?.Finalized ?? false);
                //var hasDuplicate = status.Contributors.Count > coop.Contract.MaxUsers;
                if(!coop.FinalizedFinishedOrFailed()) {
                    await CheckHighestEBJoined(coop, usersWithStatus, coopDetails, coopThread, _db, usersNotJoined);

                    if(!coop.ProjectedToFinish && coopDetails.PercentProjectedForJoined >= 100 && coop.CoopEnds > DateTimeOffset.Now) {
                        coop.ProjectedToFinish = true;
                        await coopThread.SendMessageAsync($"Coop {coop.Name} is now projected to finish!");
                        await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                    }

                    if(status.SecondsRemaining > 1 && coop.ProjectedToFinish && coopDetails.PercentProjectedForJoined < 100 && coop.CoopEnds > DateTimeOffset.Now) {
                        coop.ProjectedToFinish = false;
                        await coopThread.SendMessageAsync($"Coop {coop.Name} is **no longer** projected to finish.");
                        await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                    }

                    if(!coop.Finished && status.Finished()) {
                        if(waitingOn.Any()) {
                            coop.Status = CoopStatusEnum.Completed;
                            await coopThread.SendMessageAsync($"Coop {coop.Name} is finished, and is waiting for users to check-in!");
                        } else {
                            finalChannelUpdate = true;
                            coop.Status = CoopStatusEnum.CompletedAllCheckIn;
                            coop.ThreadArchived = true;
                            await coopThread.ModifyAsync(t => t.AutoArchiveDuration = ThreadArchiveDuration.OneDay);
                            await coopThread.SendMessageAsync($"Coop {coop.Name} is finished!");
                        }
                        coop.CoopCompleted = DateTimeOffset.UtcNow;
                        coop.Finished = true;

                        await _db.SaveChangesAsync(CancellationToken.None);
                        await HandleUnjoins(usersNotJoined, users, dbGuild, coop, _db, coopThread);
                    }

                    if(coop.Finished && coop.Status != CoopStatusEnum.CompletedAllCheckIn && !waitingOn.Any()) {
                        finalChannelUpdate = true;
                        coop.Status = CoopStatusEnum.CompletedAllCheckIn;
                        coop.ThreadArchived = true;
                        await coopThread.ModifyAsync(t => t.AutoArchiveDuration = ThreadArchiveDuration.OneDay);
                        await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                    }
                }


                timings.Set(4);


                if(coop.CurrentUsers != status.Contributors.Count) {
                    var hadDuplicate = coop.CurrentUsers > coop.MaxUsers;
                    coop.CurrentUsers = status.Contributors.Count;
                    coop.MaxUsers = coop.Contract.MaxUsers;
                }


                var msgs = GetStatusStringAsync(coopDetails, coop.Contract);
                var lastMessage = "";

                timings.Set(5);

                var threadObj = coopThread as SocketThreadChannel;
                //var currentUsers = coop.UserCoopsXrefs.Where(u => u.JoinedCoop).Select(u => u.User.DiscordId).Distinct().ToList();
                var currentUserDiscordIds = coop.UserCoopsXrefs.Where(x => x.JoinedCoop).Select(x => users.FirstOrDefault(u => u.User.Id == x.UserId)).Where(x => x is not null).Select(x => x.User.DiscordId);
                foreach(var userStatus in coopDetails.CoopParticipants.Where(x => x.Xref != null && x.DiscordUser is not null)) {
                    if(!userStatus.Xref.AddedToChannel) {
                        usersNeedingChannelPermissions.Add(userStatus.DiscordUser.Id);
                    } else if(userStatus.DiscordUser is not null && !threadObj.Users.Any(x => x.Id == userStatus.DiscordUser.Id) && !currentUserDiscordIds.Any(u => u == userStatus.DiscordUser.Id)) {
                        usersNeedingChannelPermissions.Add(userStatus.DiscordUser.Id);
                    }

                    if(!userStatus.Xref.JoinedCoop && userStatus.CoopStatus is not null) {
                        userStatus.Xref.JoinedCoop = true;
                        var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
                        if(unjoinedRole != null) {
                            await userStatus.DiscordUser.RemoveRoleAsync(unjoinedRole);
                        }
                        await _db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                timings.Set(5.1);
                var pingsLeft = usersNeedingChannelPermissions.Distinct().Select(id => $"<@{id}>").ToList() ?? [];

                ////var inThread = (await coopThread.GetUsersAsync().FlattenAsync()).Any(x => x.Id == 861607701493448704);
                //var threadUsers = (await coopThread.GetUsersAsync().FlattenAsync()).ToList();

                //var role = guild.Roles.FirstOrDefault(x => x.Name.Contains("Farm Hand"));

                //var ow = role is null ? null : coopThread.GetPermissionOverwrite(role);
                //if(!coop.RolesAddedToThread || ow is null  || ow.Value.ViewChannel == PermValue.Inherit || ow.Value.ViewChannel == PermValue.Deny) {
                if(!coop.RolesAddedToThread) {
                List<ulong> roleMembersCaught = [];
                    try {
                        (await coopThread.GetParentChannelAsync())?.Category?.PermissionOverwrites?
                            .Where(p => p.Permissions.ViewChannel == PermValue.Allow && p.TargetType == PermissionTarget.Role).ToList()
                            .Select(ow => guild.GetRole(ow.TargetId)).Where(r => r != null).ToList()
                            .ForEach(role => {
                                if(role.Members.Any(m => !currentUserDiscordIds.Any(u => u == m.Id) && !roleMembersCaught.Contains(m.Id))) {
                                    pingsLeft.Add(role.Mention);
                                    roleMembersCaught.AddRange(role.Members.Select(m => m.Id).ToList());
                                }
                            });
                        coop.RolesAddedToThread = true;
                        await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                    } catch(Exception) {
                        _logger.LogInformation("Failed to compile role pings for {coop}", coopName);
                    }
                }

                timings.Set(5.2);
                if(pingsLeft.Any()) {
                    var currentContent = "";
                    var pingsPerCycle = 1500 / 22;
                    IUserMessage editPingsInto = null;
                    var deleteAfter = false;


                    try {
                        var pins = await coopThread.GetPinnedMessagesAsync();
                        IUserMessage existingBotMessage = pins.Where(m => m.Author.IsBot && m.Content != "\u17B5").LastOrDefault() as IUserMessage;

                        if(existingBotMessage != null) {
                            editPingsInto = existingBotMessage;
                            currentContent = existingBotMessage.Content;
                            pingsPerCycle = (1500 - currentContent.Length) / 22;
                        } else {
                            editPingsInto = await coopThread.SendMessageAsync("[Ping into]");
                            deleteAfter = true;
                        }
                        while(pingsLeft.Count > 0) {
                            await editPingsInto.ModifyAsync(m => m.Content = currentContent + " " + string.Join(" ", pingsLeft.Take(pingsPerCycle).ToList()));
                            // Remove pingsPerCycle entries from pingsLeft
                            pingsLeft.RemoveRange(0, Math.Min(pingsPerCycle, pingsLeft.Count));
                        }
                        if(deleteAfter) await editPingsInto.DeleteAsync();
                    } catch {
                        _logger.LogWarning("Failed to send/coalesce pings for {coop}", coopName);
                    }
                }
                timings.Set(5.3);
                var usersAdded = usersNeedingChannelPermissions.Distinct().ToList();
                foreach(var userAdded in usersAdded) {
                    var xref = coopDetails.CoopParticipants.FirstOrDefault(x => x.DiscordUser?.Id == userAdded);
                    if(xref?.Xref != null) {
                        xref.Xref.AddedToChannel = true;
                    }
                }
                if(usersAdded.Count > 0) {
                    await _db.SaveChangesAsync(CancellationToken.None);
                }

                //Handle waiting on assigned
                var missingFromServer = false;
                timings.Set(5.4);
                if(usersNotJoined.Count == 0 && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed && coop.Status != CoopStatusEnum.CompletedAllCheckIn) {
                    coop.Status = CoopStatusEnum.AllAssignedJoined;
                } else {
                    var userList = new List<string>();
                    foreach(var userFarmDetails in usersNotJoined) {
                        try {
                            //var xref = userFarmDetails.Xref;
                            var user = users.FirstOrDefault(x => x.User.Id == userFarmDetails.Xref.GetID())?.User;
                            user ??= await _db.DBUsers.FirstOrDefaultAsync(x => x.Id == userFarmDetails.Xref.UserId, cancellationToken);

                            var discordUser = user == null ? null : parentGuild.GetUser(user.DiscordId);

                            var mention = "";

                            if(discordUser == null) {
                                mention = $"{user.DiscordUsername} (Missing from server)";
                                missingFromServer = true;
                            } else if(user.EggIncAccounts.Count > 1) {
                                var eggaccount = user.EggIncAccounts.FirstOrDefault(x => x.Id == userFarmDetails.Xref.EggIncId);
                                if(eggaccount != null)
                                    mention = $"{discordUser.Mention} ({eggaccount.Backup?.UserName ?? "No Name"})";
                            } else {
                                mention = discordUser?.Mention;
                            }

                            if(userFarmDetails.Account is not null || userFarmDetails.Backup is not null) {
                                var grade = userFarmDetails.Account?.GetGrade() ?? userFarmDetails.Backup.Grade;
                                if((uint)grade != coop.League && !(coop.Contract.cc_only || coop.AnyLeague)) {
                                    mention += $" (Wrong {grade})";
                                }
                            }

                            userList.Add(mention);

                            if(discordUser != null && !coop.Finished && coop.Status != CoopStatusEnum.Failed && coop.CoopEnds > DateTimeOffset.Now) {
                                if(!userFarmDetails.Xref.JoinWarning24TillFinish && timeRemaining.TotalHours < 24 && userFarmDetails.Xref.CreatedOn < DateTimeOffset.Now.AddHours(-1)) {
                                    userFarmDetails.Xref.JoinWarning24TillFinish = true;
                                    await _db.SaveChangesAsync(CancellationToken.None);
                                    await SendDMWarning(_db, discordUser, coopThread, $"reminder to join - co-op will be finished in under {Math.Ceiling(timeRemaining.TotalHours)} hours", coop);
                                } else if(!userFarmDetails.Xref.JoinWarning24h && userFarmDetails.Xref.CreatedOn < DateTimeOffset.Now.AddHours(-24)) {
                                    userFarmDetails.Xref.JoinWarning24h = true;
                                    userFarmDetails.Xref.JoinWarning12h = true;
                                    await _db.SaveChangesAsync(CancellationToken.None);
                                    await SendDMWarning(_db, discordUser, coopThread, $"reminder to join - 24h since added to co-op", coop);
                                } else if(!userFarmDetails.Xref.JoinWarning12h && userFarmDetails.Xref.CreatedOn < DateTimeOffset.Now.AddHours(-12)) {
                                    userFarmDetails.Xref.JoinWarning12h = true;
                                    await _db.SaveChangesAsync(CancellationToken.None);
                                    await SendDMWarning(_db, discordUser, coopThread, $"reminder to join - 12h since added to co-op", coop);
                                }


                                var hoursToKick = coop.Contract.cc_only ? 24 : 18;
                                if(userFarmDetails.Xref.CreatedOn < DateTimeOffset.Now.AddHours(-hoursToKick)) {
                                    var accountName = userFarmDetails.DBUser.EggIncAccounts.Count > 1 ? $" ({userFarmDetails.DBUser.EggIncAccounts.Where(a => a.Id == userFarmDetails.Xref.EggIncId).FirstOrDefault().Backup?.UserName})" : "";
                                    await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name} within {hoursToKick} hours{accountName}, you have been removed from the co-op and your space might be filled.", user, _db, userFarmDetails.Xref, discordUser, coopThread, dbGuild, coop, false);
                                }
                            }

                            if(!userFarmDetails.Xref.OutsideCoop && coop.GuildId == _CPGuildId && !coop.FinishedOrFailedOrExpired() && userFarmDetails.Farm is not null) {
                                var farm = userFarmDetails.Farm;
                                if(farm.CoopId.Equals(coop.Name, StringComparison.OrdinalIgnoreCase)) {
                                    await coopThread.SendMessageAsync($"{discordUser?.Mention ?? user.DiscordUsername}, it looks like your game thinks you have joined the co-op but the game's servers don't see you in the co-op. Please check with the other members of the co-op to verify they don't see you, if they don't then you will need to restart the contract and join again. After you do make sure the bot can see you in the co-op.");
                                    userFarmDetails.Xref.OutsideCoop = true;
                                    await _db.SaveChangesAsync(CancellationToken.None);
                                } else if(farm.CoopId.Length > 0 && farm.FarmType == Ei.FarmType.Contract) {
                                    // This should always happen so that no matter what, we're only sending one message
                                    userFarmDetails.Xref.OutsideCoop = true;

                                    // Calculate a similarity scoreing to weed out typos
                                    var similarityScoring = FuzzySharp.Fuzz.Ratio(farm.CoopId.ToLower(), coop.Name.ToLower());
                                    if(similarityScoring >= 80) { // Almost certainly a typo
                                        var typoMessage = $"It looks like you may have typo-ed when joining your co-op <#{coop.ThreadID}>.\n\n" +
                                            $"The co-op code is `{coop.Name}`, but your backup shows an entered code of `{farm.CoopId}`.";
                                        await SendDMWarning(_db, discordUser, coopThread, typoMessage, coop);
                                    } else {
                                        // Check if they used 'another' co-op code (from a different contract, etc.)
                                        var otherContractXref = await _db.UserCoopXrefs
                                            .Include(c => c.Coop)
                                                .ThenInclude(c => c.Contract)
                                            .FirstOrDefaultAsync(
                                                x => x.User.DiscordId == discordUser.Id &&
                                                x.Coop.Name.ToLower() == farm.CoopId.ToLower(),
                                                cancellationToken: CancellationToken.None
                                            );
                                        if(otherContractXref != null) {
                                            var otherCoopMessage = $"It looks like you may have used the wrong co-op code for {coop.Contract.Name}.\n\n" +
                                                $"Your co-op code is `{coop.Name}, but your backup shows an entered code of `{farm.CoopId}`, which is the code for {otherContractXref.Coop.Contract.Name}";
                                            await SendDMWarning(_db, discordUser, coopThread, otherCoopMessage, coop);
                                        } else {
                                            var findGuild = await _db.Guilds.FirstOrDefaultAsync(g => g.Id == guild.Id || g.OverflowServersJson.Contains(guild.Id.ToString()), CancellationToken.None);

                                            // In the case this is 'coming from' an overflow server, and the user is not in the server, we want the mention to stick regardless
                                            discordUser ??= _client.Guilds.First(g => g.Id == findGuild.Id).GetUser(userFarmDetails.Xref.User.DiscordId);

                                            var message = $"It looks like {discordUser?.Mention ?? user.DiscordUsername} has joined another co-op named {farm.CoopId}.";
                                            await coopThread.SendMessageAsync(message);
                                            var logMessage = $"Outside co-op detected for {discordUser?.Mention ?? user.DiscordUsername} they joined *{farm.CoopId}*, but were assigned to <#{coopThread.Id}>";
                                            var response = ChannelHelper.DetermineAndSend(_client, findGuild, GuildChannelType.OutsideCoopLog, new() { Text = logMessage });
                                        }
                                    }

                                    // And we always want to save the DB
                                    await _db.SaveChangesAsync(CancellationToken.None);
                                }
                            }
                        } catch(Exception e) {
                            _bugSnag.Notify(e);
                        }
                    }
                    lastMessage += $"Coop **{coop.Name}** is ready for the following to join: {string.Join(", ", userList)}\n";
                }
                timings.Set(5.5);
                var giftInfos = usersWithStatus.Where(x => x.Status is not null && x.Status.FarmInfo is not null && x.FarmStats is not null).Select(x => new {
                    Shipping = x.Status.ContributionRate / x.FarmStats.MaxShippingRate * 100,
                    Habs = x.Status.ProductionParams.FarmPopulation / x.Status.ProductionParams.FarmCapacity * 100,
                    x.Status.UserName,
                    x.Status.ProductionParams.FarmPopulation
                });
                var personToGiftTo = giftInfos
                    .Where(x =>
                        x.Shipping < 97 &&
                        x.Habs < 97
                    )
                    .OrderByDescending(x => x.FarmPopulation).Take(10);
                if(personToGiftTo.Any()) {
                    List<List<FixedWidthCell>> table = [
                        [
                            new(""),
                            new($"🐔", CellAlignment.Center),
                            new($"🏠", CellAlignment.Center),
                            new($"🚚", CellAlignment.Center),
                        ],
                        .. personToGiftTo.Select(x => new List<FixedWidthCell> {
                            new(Truncate(x.UserName, 11)),
                            new($"{x.FarmPopulation.ToEggString()}", CellAlignment.Right),
                            new($"{Math.Round(x.Habs)}%", CellAlignment.Right),
                            new($"{Math.Round(x.Shipping)}%", CellAlignment.Right),
                        }).ToList(),
                    ];
                    lastMessage += $"\nFarms that would benefit from gifting chickens: \n```{string.Join("\n", GetTable(table))}```\n\n";
                } else if(coopDetails.CoopParticipants.Any(y => y.CoopStatus is not null && y.FarmStats is not null)) {
                    lastMessage += "\nLooks like everyone's shipping and/or habs are full or they haven't joined yet, so gifting chickens isn't useful.\n\n";
                }
                timings.Set(5.53);
                //New commands list, each is a quick-link to start using the command
                lastMessage += "__Co-op Commands (click to use):__\n";

                timings.Set(5.54);
                if(_client.GetChannelAsync(GuildChannelType.CallStaffChannel, guild) != null) {
                    lastMessage += $"\n</callstaff:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "callstaff")?.Id ?? 0}> Use this command if you joined a co-op for the wrong contract, or have other questions or concerns";
                }
                lastMessage += $"\n</coopsettings:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "coopsettings")?.Id ?? 0}> Receive DM pings for various events in the co-op";
                lastMessage += $"\n</fixfullcooperror:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "fixfullcooperror")?.Id ?? 0}> If you get the error co-op is full, try running this command to free up the space.";

                timings.Set(5.6);

                var userWithDifferentGrade = usersWithStatus.FirstOrDefault(x => x.Backup is not null && x.Backup.Farms.Any(y => y.CoopId is not null && y.CoopId.Equals(coop.Name, StringComparison.CurrentCultureIgnoreCase) && (uint)y.Grade != coop.League));
                if(!coop.FinishedOrFailed() && userWithDifferentGrade is not null) {
                    var farm = userWithDifferentGrade.Backup.Farms.FirstOrDefault(x => x.CoopId is not null && x.CoopId.ToLower() == coop.Name.ToLower());
                    lastMessage += $" Warning! Looks like this co-op is the wrong grade and is actually {farm.Grade}";
                }
;
                if(status.AllGoalsAchieved && status.Participants.Any(y => !y.Finalized)) {
                    lastMessage += $"\n\nWaiting on the following users to check-in: {string.Join(", ", waitingOn.Select(x => x.DiscordUser?.Mention ?? x.Status.UserName))}";
                }

                //Checking if users are gusset glitching
                var afCheaterChannel = ChannelHelper.DetermineChannelType(dbGuild, guild, GuildChannelType.CheaterThread);
                if(afCheaterChannel != null && !status.AllGoalsAchieved) {
                    //var contractScalar = coop.Contract.Details?.GradeSpecs[((int)coop.League) - 1]?.Modifiers?.FirstOrDefault(m => m.Dimension == Ei.GameModifier.Types.GameDimension.HabCapacity)?.Value ?? 1;
                    //foreach(var u in usersWithStatus.Where(x => x.Xref is not null && !x.Xref.GussetCheatDetected)) {
                    //    var farm = u.Backup.Farms.FirstOrDefault(x => x.CoopId is not null && x.CoopId.ToLower() == coop.Name.ToLower());
                    //    if(farm is null) continue;
                    //    /* AFFECT ALL HABS */
                    //    double allScalar = 1;
                    //    allScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "hab_capacity1")?.Level * 0.05 ?? 0); //5% per level
                    //    allScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "microlux")?.Level * 0.05 ?? 0); //5% per level
                    //    allScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "grav_plating")?.Level * 0.02 ?? 0); //2% per level
                    //    allScalar *= contractScalar; // Indeterminate before runtime

                    //    /* AFFECT PORTAL HABS */
                    //    double portalScalar = 1;
                    //    portalScalar *= 1 + (farm.CommonResearch.FirstOrDefault(c => c.Id == "wormhole_dampening")?.Level * 0.02 ?? 0); //2% per level

                    //    var currentChickens = farm.NumChickens;

                    //    var farmWithStats = farm.WithStats(u.Backup, coop, new List<DBCustomEgg>());
                    //    var scaledMaxChickens = farmWithStats.HabSpace + 0.01; //0.01 offset, again for rounding

                    //    //If they aren't surpassing the scaled limit, they aren't cheating
                    //    if(currentChickens <= (scaledMaxChickens * 1.01)) continue; //1% offset for rounding errors

                    //    var gusset = farm.Artifacts.FirstOrDefault(a => a.Artifact.ToLower().Contains("gusset"));
                    //    if(gusset is null) {
                    //        await ChannelHelper.DetermineAndSend(_client, dbGuild, GuildChannelType.CheaterThread,
                    //            new() {
                    //                Text =
                    //                $"User <@{u.User.DiscordId}> ({u.Backup?.UserName ?? "_No Username_"}) may have glitched to remove a gusset after boosting, in the coop <#{coop.ThreadID}> (`{coop.Name}`):\n" +
                    //                $"```\nMax hab space:\t   {(ulong)farmWithStats.HabSpace:n0}\nCurrent chickens:\t{currentChickens:n0}\n```"
                    //            });
                    //        u.Xref.GussetCheatDetected = true;
                    //    }
                    //}
                    foreach(var u in usersWithStatus.Where(u => u.Status is not null && u.Status.TimeCheatDetected && u.Xref is not null && !u.Xref.TimeCheatReported).ToList()) {
                        var account = u.User?.EggIncAccounts?.FirstOrDefault(a => a.Id.ToLower() == u.Backup?.EggIncId.ToLower());
                        if(account is null || account.TimeCheatsMarkedClean) continue;
                        await ChannelHelper.DetermineAndSend(_client, dbGuild, GuildChannelType.CheaterThread,
                            new() { Text = $"Time cheat detected for <@{u.User.DiscordId}> ({u.Backup?.UserName ?? "_No Username_"}) in the coop <#{coop.ThreadID}> (`{coop.Name}`)" }
                        );
                        u.Xref.TimeCheatReported = true; //Set the flag to prevent repetition
                    }
                }


                foreach(var u in usersWithStatus.Where(x => x.Xref is not null)) {
                    u.Xref.HasTachyonDeflector = u.Xref.HasTachyonDeflector || (u.Backup?.GetAvailableArtifacts().Any(a => a.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) ?? false);
                    var farm = u.Backup?.Farms.FirstOrDefault(x => x.ContractId == coop.ContractID);
                    if(farm == null)
                        continue;
                    u.Xref.EquipedTachyonDeflector = u.Xref.EquipedTachyonDeflector || farm.Artifacts.Any(a => a.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates);
                }

                var usersToCheckDeflector = usersWithStatus.Where(x => x.Status is not null && !x.Status.BuffHistory.Any(y => y.EggLayingRate > 0) && x.Backup is not null && x.Backup.ArtifactHall is not null && x.Status.Projected < usersWithStatus.Where(y => y.Status is not null).Max(y => y.Status.Projected) / 2);
                var usersNeedToAddDeflector = new List<UserWithStatus>();
                if(!coop.FinishedOrFailed() && coop.CoopEnds > DateTimeOffset.Now) {
                    foreach(var user in usersToCheckDeflector) {
                        var farm = user.Backup.Farms.FirstOrDefault(x => x.ContractId == coop.ContractID);
                        if(farm is not null && !farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) && user.Backup.GetAvailableArtifacts().Any(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)) {
                            usersNeedToAddDeflector.Add(user);
                        }
                    }
                }



                if(usersNeedToAddDeflector.Any()) {
                    lastMessage += $"\n\n**The following users have a Tachyon Deflector they should equip:** {string.Join(", ", usersNeedToAddDeflector.Select(y => y.DiscordUser?.Mention ?? $"<@{y.User?.DiscordId}>"))}";
                }


                if(status.Contributors.Count == coop.MaxUsers && coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                    coop.Status = CoopStatusEnum.Full;
                }

                if(coop.Status != CoopStatusEnum.Failed && status.Failed()) {
                    if(coop.Contract.GoodUntil > DateTimeOffset.UtcNow) {
                        await coopThread.SendMessageAsync($"Co-op {coop.Name} failed to reach all the goals and the contract is still available for {(coop.Contract.GoodUntil - DateTimeOffset.UtcNow).Humanize()} if you want to restart and try again.");
                    } else {
                        await coopThread.SendMessageAsync($"Co-op {coop.Name} failed to reach all the goals and the contract is no longer available.");
                    }
                    coop.Status = CoopStatusEnum.Failed;
                    finalChannelUpdate = true;
                    coop.ThreadArchived = true;
                    await _db.SaveChangesAsync(CancellationToken.None);

                    await HandleUnjoins(usersNotJoined, users, dbGuild, coop, _db, coopThread);
                }

                timings.Set(6);


                var emojis = "";




                var missingCount = coopDetails.CoopParticipants.Count(x => x.Xref is not null && x.CoopStatus is null);

                if(missingCount == 0) {
                    await HandlePingOnFull(_db, coopDetails.CoopParticipants, coopThread);
                }

                if(status.ClearedForExit) {
                    await HandlePingOnCheckedIn(_db, coopDetails.CoopParticipants, coopThread);
                }

                if(coop.FinishedOrFailed()) {
                    await HandleFinished(_db, coopDetails.CoopParticipants, coopThread);
                }

                timings.Set(7);

                coop.LastStatusUpdate = status;
                if(!coop.FinalizedFinishedOrFailed() || finalChannelUpdate) {

                    var color = Color.DarkGrey;
                    if(coop.Status == CoopStatusEnum.Failed) {
                        emojis += "🚩";
                    } else if(coop.Finished) {
                        emojis += "🏁";
                    } else {

                        if(missingCount > 0) {
                            if(DateTimeOffset.Now > (coop.Created + TimeSpan.FromHours(12))) {
                                if(missingCount <= 20) {
                                    emojis += Convert.ToChar(9311 + missingCount);
                                } else if(missingCount <= 35) {
                                    emojis += Convert.ToChar(12881 + (missingCount - 21));
                                } else if(missingCount <= 50) {
                                    emojis += Convert.ToChar(12977 + (missingCount - 36));
                                } else {
                                    emojis += "❌";
                                }
                            } else {
                                emojis += "📶";
                            }

                            if(
                                !coop.Finished && (
                                    timeRemaining.TotalHours < 24
                                    || status.SecondsRemaining > 0 && status.SecondsRemaining < TimeSpan.FromHours(24).TotalSeconds
                                )
                            ) {
                                emojis += "🔺";
                            }
                        }
                        //} else if(
                        //        !coop.FinishedOrFailed() && (
                        //            timeRemaining.TotalHours < 3
                        //            || status.SecondsRemaining > 0 && status.SecondsRemaining < TimeSpan.FromHours(6).TotalSeconds
                        //        ) && (coop.LastStatusUpdate?.Participants.Count ?? 0) < coop.Contract.Details.MaxCoopSize && !status.Public
                        //    ) {
                        //    emojis += "🔘";
                        //}


                        var percent = coopDetails.PercentProjectedForJoined;

                        if(percent < 60) {
                            color = Color.Red;
                            emojis += "🔴";
                        } else if(percent < 90) {
                            color = new Color(139, 69, 19);
                            emojis += "🤎";
                        } else if(percent < 100) {
                            color = Color.Orange;
                            emojis += "🧡";
                        } else if(percent < 105) {
                            color = new Color(255, 255, 0);
                            emojis += "💛";
                        } else {
                            color = Color.Green;
                            emojis += "💚";
                        }

                        if(percent < 100 && coopDetails.PercentProjected >= 100) {
                            emojis += "💹";
                        }

                        if(missingFromServer) {
                            emojis += "👻";
                        }

                        if(coopDetails.CoopParticipants.Any(x => x.Xref is null) && !status.Public && !coop.Finished) {
                            emojis += "👽";
                        }

                        if(coopDetails.CoopParticipants.Count > coop.Contract.MaxUsers) {
                            emojis += "🤢";
                        }

                    }


                    var coopname = emojis + coop.Name;
                    if(coopThread.Name != coopname) {
                        for(var i = 0; i < 5; i++) {
                            try {
                                await coopThread.ModifyAsync(x => x.Name = coopname);
                                break;
                            } catch(Exception) {
                                _logger.LogInformation("Error updating thread name for {coopName}, delaying...", coop.Name);
                                await Task.Delay(new Random().Next(500), cancellationToken);
                            }
                        }
                    }

                    timings.Set(8);

                    if(lastMessage != "")
                        msgs.AddRange(DiscordMessageSplitter.SplitMessage(lastMessage, "\n"));

                    var gradeMessage = $"**Co-op Grade**: {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)(int)coop.League)}{(coop.AnyLeague ? " (<:ultra:1131045418319495369> **Any-Grade**)" : "")}";

                    var highestEB = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                    var highestEBMessage = "";
                    if(highestEB != null)
                        highestEBMessage = $"**\nHighest EB**: {highestEB.DBUser.DiscordUsername} at {highestEB.Backup.EarningsBonus.ToEggString()} {(usersNotJoined.Any(x => x?.EggIncId == highestEB.Backup.EggIncId) ? "has not joined yet." : "**has joined!**")}";

                    var createdByMessage = "";
                    if(!string.IsNullOrEmpty(coop.CreatorID) && !ContractsAPI.CoopCreatorIds.Any(x => x.EggIncId == coop.CreatorID)) {
                        var creator = users.FirstOrDefault(x => x.Backup?.EggIncId == coop.CreatorID);
                        if(creator != null) {
                            var account = creator.User.EggIncAccounts.First(x => x.Id == coop.CreatorID);
                            createdByMessage += $"\n**Created By**: {creator.User.DiscordUsername} {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)(int)account.LastGrade)}";
                        }
                    }

                    var publicMessage = status.Public ? $"\n**This co-op is public**." : "";

                    var embedBuilder = new EmbedBuilder()
                    .WithDescription($"{gradeMessage}{highestEBMessage}{createdByMessage}{publicMessage}\n" +
                    (
                        (status.Finished()
                        ? "\nThis co-op is finished!"
                        : coopDetails.PercentProjectedForJoined >= 100 && !coop.FinishedOrFailed()
                        ? "\nThis co-op is projected to succeed without growth as long as there are no sleepers!"
                        : "") + $"\n[View on egg9000.com](https://egg9000.com/coop/{coop.ContractID}/{coop.Name})"
                    ))
                    .WithColor(color)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    ;


                    embedBuilder.WithAuthor(new EmbedAuthorBuilder().WithName($"{coop.Contract.Name} - Coop Code: {coop.Name}").WithIconUrl(EggIncStatics.GetEggByContract(coop.Contract, customEggs).image));


                    var updates = UpdateInterval.TotalMinutes;
                    if(finalChannelUpdate) {
                        embedBuilder.WithFooter($"Final Update");
                    } else {
                        embedBuilder.WithFooter($"Updates Every {updates} Minute{(updates > 1 ? "s" : "")} - Last Updated");
                    }

                    var ends = DiscordHelpers.TimeStamper(TimeSpan.FromSeconds(status.SecondsRemaining));
                    if(status.SecondsRemaining <= 0) {
                        ends = $"Expired {ends}";
                        if(!coop.PseudoExpired) coop.PseudoExpired = true;
                    }

                    for(var i = 0; i < 3; i++) {
                        if(coop.Contract.Details.GetGoals(league).Count > i) {
                            var goal = coop.Contract.Details.GetGoals(league)[i];
                            var title = $"Goal {i + 1} ";
                            var time = "";
                            var goalRemaingAmount = goal.TargetAmount - amountWithOffline;
                            var goalRemaingTime = goalRemaingAmount / totalRate;
                            time = $"\nTime: {GetTimeRemaining(goal.TargetAmount, totalRate, amountWithOffline)}";
                            if(status.TotalAmount > goal.TargetAmount) {
                                title += "✅";
                                time = "";
                            } else if(coop.Status == CoopStatusEnum.Failed) {
                                title += "❌";
                                time = "";
                            } else if(coopDetails.PercentProjectedForJoined > goal.TargetAmount) {
                                title += "☑";
                            }
                            embedBuilder.AddField(title, $"Target: {goal.TargetAmount.ToEggString()}\nReward: {EggIncStatics.GetReward(goal)}{time}", true);
                        } else {
                            embedBuilder.AddField("\u17B5", "\u17B5", true);
                        }
                    }

                    //Estimate the time the coop is projected to finish
                    try {
                        coop.ProjectedFinish = DateTimeOffset.Now.AddSeconds(Math.Min(TimeSpan.FromDays(365).TotalSeconds, GetTimeRemainingValue(targetAmount, totalRate, amountWithOffline).TotalSeconds));
                    } catch(ArgumentOutOfRangeException) {
                        coop.ProjectedFinish = DateTimeOffset.Now.AddYears(1);
                    }

                    var totalRatePerHour = totalRate * 60 * 60;
                    if(coop.Status != CoopStatusEnum.Completed && coop.Status != CoopStatusEnum.Failed) {
                        embedBuilder.AddField("Co-op Expires", ends, inline: true);

                        if(remainingAmount > 0) {
                            var remainingTime = remainingAmount / totalRate;
                            if(remainingTime < TimeSpan.MaxValue.TotalSeconds) {
                                try {
                                    embedBuilder.AddField("Time To Complete", GetTimeRemaining(targetAmount, totalRate, amountWithOffline), inline: true);
                                    if(status.SecondsRemaining > remainingTime) {
                                        embedBuilder.AddField("Ahead By", TimeSpan.FromSeconds(status.SecondsRemaining - remainingTime).Humanize(2).ShortenTime(), inline: true);
                                    } else {
                                        embedBuilder.AddField("Behind By", TimeSpan.FromSeconds(status.SecondsRemaining - remainingTime).Humanize(2).ShortenTime(), inline: true);
                                    }
                                } catch(OverflowException) {

                                }
                            } else {
                                embedBuilder.AddField("Time To Complete", "**\u221E**", inline: true);
                                embedBuilder.AddField("\u17B5", "\u17B5");
                            }
                        } else if(!status.Finished()) {
                            await CheckCompleteOnCheckIn(coop, usersWithStatus, coopThread, _db);
                            embedBuilder.AddField("Time To Complete", "Once everyone checks in", inline: true);
                        }

                        embedBuilder.AddField("Projected Amount", $"{coopDetails.Projected.ToEggString()} of {targetAmount.ToEggString()} {Math.Round(coopDetails.PercentProjectedForJoined)}%", inline: true);
                        embedBuilder.AddField("Current Amount", status.TotalAmount.ToEggString(), inline: true);
                        embedBuilder.AddField("Current With Offline", amountWithOffline.ToEggString(), inline: true);
                    } else if(coop.Status == CoopStatusEnum.Completed) {
                        embedBuilder.AddField("Final Amount", status.TotalAmount.ToEggString(), inline: true);
                        embedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                    } else if(coop.Status == CoopStatusEnum.Failed) {
                        embedBuilder.AddField("Final Amount", status.TotalAmount.ToEggString(), inline: true);
                        embedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
                    }
                    timings.Set(9);
                    await UpdateChannel(msgs, embedBuilder.Build(), coopThread, coop, statusReponse.DiscordMessages);
                }

                await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);


                var times = timings.Finished();

                _logger.LogTrace("Co-op timings {timings} - {coop}", string.Join(",", times.Select(x => $"{x.name}:{x.time.Humanize().ShortenTime()}")), coop.Name);
            } catch(Exception e) {
                _logger.LogError(e, "Error in co-op {coopid}", coopName ?? coopId.ToString());
                _bugSnag.Notify(e);
            }
        }

        private async Task CheckForCoopCreatorStillIn(Coop coop, Ei.ContractCoopStatusResponse status) {
            if(!ContractsAPI.CoopCreatorIds.Any(x => x.EggIncId == coop.CreatorID))
                return;
            if(status.Contributors.Any(x => x.UserId == coop.CreatorID)) {
                _logger.LogError("Coop creator {creator} is still in coop {coop}", coop.CreatorID, coop.Name);
            }
        }

        public class UserWithStatus {
            public CustomBackup Backup { get; set; }
            public Ei.ContractCoopStatusResponse.Types.ContributionInfo Status { get; set; }
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




        private async Task UpdateChannel(List<string> msgs, Embed embed, IThreadChannel coopChannel, Coop coop, List<IMessage> existingMessages) {
            var sw = new Stopwatch();
            sw.Restart();
            var times = new List<long>();

            msgs = msgs.Where(x => x != "").ToList();

            msgs.Insert(0, "@@@EMBED");

            //Reserve up to 5 msgs
            for(var i = msgs.Count; i < (coop.MaxUsers > 40 ? 5 : 4); i++) {
                msgs.Add("\u17B5");
            }
            if(string.IsNullOrWhiteSpace(coop.UpdateMessagesId)) {
                var UpdateMessagesID = new List<ulong>();
                foreach(var msg in msgs) {
                    IUserMessage post;
                    if(msg == "@@@EMBED") {
                        post = await coopChannel.SendMessageAsync(embed: embed);
                    } else {
                        post = await coopChannel.SendMessageAsync(msg);
                    }
                    UpdateMessagesID.Add(post.Id);
                    await post.PinAsync();
                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(UpdateMessagesID);
                try {
                    var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                    await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                } catch(TimeoutException) {
                    var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                    await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                }
            } else {
                var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);
                var NewUpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);

                if(coopChannel != null) {

                    var pinnedMessages = false;
                    for(var i = 0; i < msgs.Count; i++) {
                        if(UpdateMessageIDs.Count > i) {
                            try {
                                var post = (RestUserMessage)existingMessages.FirstOrDefault(x => x.Id == UpdateMessageIDs[i]);
                                if(post == null) {
                                    if(msgs[i] == "@@@EMBED") {
                                        post = (RestUserMessage)await coopChannel.SendMessageAsync(embed: embed);
                                    } else {
                                        post = (RestUserMessage)await coopChannel.SendMessageAsync(msgs[i]);
                                    }
                                    NewUpdateMessageIDs.Remove(UpdateMessageIDs[i]);
                                    NewUpdateMessageIDs.Add(post.Id);
                                } else {
                                    if(msgs[i] == "@@@EMBED") {
                                        await post.ModifyWithTimeoutAsync(msg => { msg.Embed = embed; msg.Content = null; });
                                    } else {
                                        var changes = post.Content.CompareChanges(msgs[i]);
                                        if(changes > 0) {
                                            await post.ModifyWithTimeoutAsync(msg => msg.Content = msgs[i]);
                                        } else {
                                        }
                                    }
                                }
                                if(!post.IsPinned) {
                                    try {
                                        await post.PinAsync();
                                        pinnedMessages = true;
                                    } catch(JsonReaderException) {
                                        _logger.LogWarning("JsonReaderException when pinning message in coop {coop}", coop.Name);
                                    }
                                }
                            } catch(Exception e) {
                                _logger.LogError(e, "Error updating messages");
                                _bugSnag.Notify(e);
                            }
                        } else {
                            if(msgs[i] == "@@@EMBED") {
                                var post = await coopChannel.SendMessageAsync(embed: embed);
                                NewUpdateMessageIDs.Add(post.Id);
                                pinnedMessages = true;
                                await post.PinAsync();
                            } else {
                                var post = await coopChannel.SendMessageAsync(msgs[i]);
                                NewUpdateMessageIDs.Add(post.Id);
                                pinnedMessages = true;
                                await post.PinAsync();
                            }
                        }

                    }
                    if(pinnedMessages) {
                        try {
                            var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                            await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                        } catch(TimeoutException) {
                            var messages = await coopChannel.GetMessagesAsync().FlattenAsync();
                            await coopChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                        }
                    }

                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(NewUpdateMessageIDs);
            }
        }

        public static List<string> GetStatusStringAsync(CoopDetails coopDetails, EGG9000.Common.Database.Entities.Contract contract) {
            var table = new List<List<FixedWidthCell>> {new () {
                new($"{coopDetails.CoopParticipants.Count}/{contract.MaxUsers}"),
                new("Discord", CellAlignment.Center),
                new("EB", CellAlignment.Center),
                new("Total", CellAlignment.Center),
                new("Rate", CellAlignment.Center),
                new("📈", CellAlignment.Center),
                new("%", CellAlignment.Center),
                new("🟡", CellAlignment.Center, true),
                new("⏲️", CellAlignment.Center, true),
                new("Silo"),
                new(""),
            }};
            var everyoneJoined = coopDetails.CoopParticipants.All(x => x.CoopStatus is not null);

            table.AddRange(coopDetails.CoopParticipants.OrderByDescending(x => x.Projected).Select(x => {
                var sleeping = x.OfflineTime.TotalMinutes > x.SiloTimeMinutes ? "💤" : "";

                if(x.OfflineTime.TotalMinutes > x.SiloTimeMinutes) {
                    sleeping = $"💤 Empty Silos {x.OfflineTime.Add(TimeSpan.FromMinutes(0 - x.SiloTimeMinutes)).Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()}";
                }

                if(coopDetails.Coop.FinishedOrFailed())
                    sleeping = "";

                if(x.CoopStatus?.TimeCheatDetected ?? false)
                    sleeping += " ⏱️";


                //var eb = Math.Pow(10, x.Status.SoulPower) * 100;
                var percent = coopDetails.GetProjectedShare(x);

                if(x.DBUser is null) {

                }

                return new List<FixedWidthCell> {
                    new(Truncate((everyoneJoined || x.DBUser is null ? "" : x.CoopStatus is not null ? "✅" : "❌") + (x.DBUser is null ? "👽" : "") + Regex.Replace(x.CoopStatus?.UserName ?? x.Backup?.UserName, @"\p{Cs}", ""), 11)),
                    new(Truncate(Regex.Replace(x.DiscordUser?.GetCleanName() ?? "", @"\p{Cs}", ""), 11)),
                    new(x.EarningsBonus.ToEggString(), CellAlignment.Right),
                    new(x.EggsShipped.ToEggString(), CellAlignment.Right),
                    new($"{(x.Rate * 3600).ToEggString()}/h", CellAlignment.Right),
                    new(x.Projected.ToEggString(), CellAlignment.Right),
                    new($"{Math.Round(percent)}%", CellAlignment.Right),
                    new(x.BoostTokens.ToString()),
                    new(x.OfflineTime.Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()),
                    new(TimeSpan.FromMinutes((double)x.SiloTimeMinutes).Humanize(2, maxUnit: Humanizer.Localisation.TimeUnit.Hour).ShortenTime()),
                    new(sleeping),
                };
            }));



            var lstr = new List<string>();



            var tableString = $"```{GetTable(table)}```";

            var msgs = new List<string>();

            while(tableString.Length > 2000) {
                var index = tableString.LastIndexOf('\n', 1997);

                msgs.Add(tableString[..index] + "```");
                tableString = "```" + tableString[index..];
            }

            msgs.Add(tableString);

            return msgs;
        }

        private class StatusResponse {
            public Ei.ContractCoopStatusResponse Status { get; set; }
            public List<IMessage> DiscordMessages { get; set; }
        }

        private static async Task<List<IMessage>> GetDiscordMessages(ITextChannel coopChannel, Coop coop, CancellationToken cancellationToken) {
            var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId ?? "[]");

            IEnumerable<IMessage> discordMessages;
            try {

                discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : [];
            } catch(Exception) {
                try {
                    await Task.Delay(100, cancellationToken);
                    discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : [];

                } catch(Exception) {
                    await Task.Delay(100, cancellationToken);
                    discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : [];
                }
            }

            var messages = new List<IMessage>();
            foreach(var id in UpdateMessageIDs) {
                var message = discordMessages.FirstOrDefault(x => x.Id == id);
                if(message == null) {
                    for(var i = 0; i < 10; i++) {
                        try {
                            message = await coopChannel.GetMessageAsync(id, options: new RequestOptions { CancelToken = cancellationToken });
                            break;
                        } catch(Exception) {
                            await Task.Delay(500, cancellationToken);
                        }
                    }
                    message ??= await coopChannel.GetMessageAsync(id, options: new RequestOptions { CancelToken = cancellationToken });
                }
                if(message != null)
                    messages.Add(message);
            }

            return messages;
        }

        private async Task<StatusResponse> GetStatus(Coop coop, ITextChannel channel, CancellationToken cancellationToken) {
            var policy = Policy
               .Handle<Exception>()
               .WaitAndRetry(
               [
                 TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(3),
                            TimeSpan.FromSeconds(7)
               ]);
            Task<ContractCoopStatusResponse> statusTask;


            if(!coop.UserCoopsXrefs.Any(x => x.JoinedCoop)) {
                statusTask = policy.Execute(async () => await ContractsAPI.GetCoopStatusBot(coop.ContractID, coop.Name, cancellationToken: cancellationToken));
                //statusTask = policy.Execute(async () => await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name, EIID: coop.CreatorID, cancellationToken: cancellationToken));
            } else {
                var joinedUsers = coop.UserCoopsXrefs.Where(x => x.JoinedCoop).ToList(); 
                statusTask = policy.Execute(async () => await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name, EIID: joinedUsers.ElementAt(rand.Next(joinedUsers.Count)).EggIncId, cancellationToken: cancellationToken));
            }
            var messageTask = GetDiscordMessages(channel, coop, cancellationToken);

            await Task.WhenAll(statusTask, messageTask);
            if(statusTask.Result is null) {

            }
            return new StatusResponse {
                Status = statusTask.Result,
                DiscordMessages = messageTask.Result
            };
        }

        public static int GetDigit(int number, int digit) {
            for(var i = 0; i < digit - 1; i++)
                number /= 10;
            return number % 10;
        }

        public async Task HandleSleeping(UserFarmDetails user, ITextChannel coopChannel, Coop coop, ApplicationDbContext _db, Guild dbGuild) {
            if(user.Xref is null || coop.CoopEnds < DateTimeOffset.Now || coop.FinishedOrFailed() || user.CoopStatus is null)
                return;

            var currentSleepStart = user.Joined ? DateTimeOffset.Now.Subtract(user.OfflineTime) : coop.Created;
            var hoursSleeping = (double)user.OfflineTime.TotalMinutes / 60.0;
            var siloTimeHours = (float)(user.SiloTimeMinutes / 60.0);
            var alertTime = (30.0 - siloTimeHours) / 2 + siloTimeHours;
            var needsAlert = hoursSleeping >= alertTime;
            var timeEmpty = Math.Round(hoursSleeping - siloTimeHours, 2);

            var sleepTracking = user.Xref.SleepTracking.ToList();

            var currentSleep = sleepTracking.FirstOrDefault(x => !x.WokeUp);

            if(currentSleep == null && needsAlert) {
                currentSleep = new SleepTracking { SleepStart = currentSleepStart, LastChecked = DateTimeOffset.Now, Silos = siloTimeHours, EggsShipped = user.EggsShipped, Rate = user.Rate };

                var messages = BotText.SleepingMessages;
                var random = new Random();
                var index = random.Next(messages.Count);

                if(user.DiscordUser != null) {
                    var warningText = messages[index].Replace("@name", user.DiscordUser.Mention + (timeEmpty < 0 ? $" [Empty silos in {timeEmpty} hours {coopChannel.Mention}]" : $" [Silos have been empty for {timeEmpty} hours {coopChannel.Mention}]"));
                    var dmResult = await BoolSendDm(user.DiscordUser, warningText, _db);
                    if(dmResult != DMResult.Success) {
                        await coopChannel.SendMessageAsync($"{warningText} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                    }
                }
                sleepTracking.Add(currentSleep);
            }

            if(currentSleep != null) {
                if(currentSleepStart > currentSleep.SleepStart.AddMinutes(10)) { //Adding 10 mins to account for weird time stuff
                                                                                 //No longer sleeping
                    currentSleep.WokeUp = true;
                    currentSleep.TotalHoursEmpty = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours - (currentSleep.Silos > 0 ? currentSleep.Silos : siloTimeHours);
                    currentSleep.Expected = currentSleep.EggsShipped + currentSleep.Silos * currentSleep.Rate;
                    currentSleep.Actual = user.EggsShipped;
                    user.Xref.TotalHoursSleeping = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours;
                    user.Xref.HoursSleeping = 0;
                } else {
                    var nextDemeritAt = (currentSleep.DemeritsGiven + 1) * 18;
                    var demeritChannel = await GetDemeritChannel(dbGuild);
                    var needsDemerit = timeEmpty > nextDemeritAt && demeritChannel is not null && !user.Xref.NoDemerit;
                    if(needsDemerit && user.DBUser is not null) {
                        currentSleep.DemeritsGiven++;
                        if(user.DBUser.IsFreshEgg()) {
                            await coopChannel.SendMessageAsync($"{user.DiscordUser?.Mention ?? user.DBUser.DiscordUsername}: You will start receiving demerits for this 7 days after joining the server. Your silos have been empty for {nextDemeritAt} hours.");
                        } else {
                            var demerit = new Demerit {
                                When = DateTimeOffset.Now,
                                AdminUserId = Guid.Empty,
                                UserId = user.DBUser.Id,
                                Id = Guid.NewGuid(),
                                Reason = $"Empty silos for {nextDemeritAt} hours in {coop.Contract.Name}",
                                Details = JsonConvert.SerializeObject(new { FarmTimestemp = user.CoopStatus?.FarmInfo?.Timestamp, Silos = siloTimeHours })
                            };
                            _db.Demerit.Add(demerit);
                            await _db.SaveChangesAsync();
                            var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.DBUser.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();
                            var demeritText = $"Demerit added to {user.DiscordUser?.Mention ?? user.DBUser.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                            if(count >= 3) {
                                demeritText = $"**{demeritText}**";
                            }
                            await coopChannel.SendMessageAsync(demeritText);
                            await demeritChannel.SendMessageAsync($"{demeritText} {coopChannel.Mention}");
                        }
                    }
                    user.Xref.HoursSleeping = (int)Math.Floor((DateTimeOffset.Now - currentSleep.SleepStart).TotalHours);
                }

                if(!currentSleep.WokeUp) {
                    currentSleep.LastChecked = DateTimeOffset.Now;
                }
            }
            user.Xref.SleepTracking = sleepTracking;
        }

        // Ignore Spelling: unjoins
        public async Task HandleUnjoins(List<UserFarmDetails> usersNotJoined, List<UserWithBackup> users, Guild dbGuild, Coop coop, ApplicationDbContext _db, IThreadChannel coopChannel) {
            var demeritChannel = await GetDemeritChannel(dbGuild);
            if(demeritChannel is null) {
                return;
            }
            foreach(var userFarmDetail in usersNotJoined) {
                var user = users.FirstOrDefault(x => x.User.Id == userFarmDetail.Xref.GetID()).User;
                if(user == null || userFarmDetail.Xref.NoDemerit)
                    continue;

                if(userFarmDetail.Xref.CreatedOn > DateTimeOffset.Now.AddHours(-18)) {
                    _db.Remove(userFarmDetail.Xref);
                    await _db.SaveChangesAsync();
                    await coopChannel.SendMessageAsync($"Removed {userFarmDetail.DiscordUser?.GetCleanName() ?? user.DiscordUsername} without a demerit since they were added less than 18 hours before the co-op finished.");
                    continue;
                }

                if(user.Registered > DateTimeOffset.Now.AddDays(-7)) {
                    await coopChannel.SendMessageAsync($"{userFarmDetail.DiscordUser?.Mention ?? user.DiscordUsername}, you failed to join this co-op. After your first week in this server you will get a demerit for failing to join an assigned co-op. Ask staff if you have any questions.");
                    continue;
                }


                await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name}", user, _db, userFarmDetail.Xref, userFarmDetail.DiscordUser, coopChannel, dbGuild, coop, true);
            }
        }

        public static async Task HandlePingOnFull(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, IThreadChannel coopChannel) {
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnFull ?? false)) {
                userStatus.Xref.CoopSetting.PingOnFull = false;
                userStatus.Xref.UpdateCoopSetting();

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"All users have joined the co-op {coopChannel.Mention}", db);
                if(dmResult != DMResult.Success) {
                    await coopChannel.SendMessageAsync($"{userStatus.DiscordUser.Mention} All users have joined the co-op {coopChannel.Mention} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }
        public static async Task HandlePingOnCheckedIn(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, IThreadChannel coopChannel) {
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnEveryoneCheckedIn ?? false)) {
                userStatus.Xref.CoopSetting.PingOnEveryoneCheckedIn = false;
                userStatus.Xref.UpdateCoopSetting();

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"The co-op {coopChannel.Mention} has finished and you are able to exit the co-op.", db);
                if(dmResult != DMResult.Success) {
                    await coopChannel.SendMessageAsync($"{userStatus.DiscordUser.Mention} The co-op {coopChannel.Mention} has finished and everyone is checked in. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }

        public static async Task HandleFinished(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, IThreadChannel coopChannel) {
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnFinished ?? false)) {
                userStatus.Xref.CoopSetting.PingOnFinished = false;
                userStatus.Xref.UpdateCoopSetting();

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"The co-op {coopChannel.Mention} has finished.", db);
                if(dmResult != DMResult.Success) {
                    await coopChannel.SendMessageAsync($"{userStatus.DiscordUser.Mention} The co-op {coopChannel.Mention} has finished. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }

        public async Task WrongAccountWarning(UserFarmDetails user, IThreadChannel coopThread, ApplicationDbContext _db, string WrongEIID) {

            await coopThread.SendMessageAsync($"<@{user.DBUser.DiscordId}>, it looks like you might have joined the coop with the wrong account.");
            await BoolSendDm(user.DiscordUser, $"It looks like you might have joined the coop with the wrong account in {coopThread.Mention}.", _db);

        }
        public async Task SendDMWarning(ApplicationDbContext db, SocketGuildUser discordUser, IThreadChannel coopChannel, string Message, Coop coop) {
            if(discordUser is null)
                return;

            var dmResult = await BoolSendDm(discordUser, $"{Message}: {coop.Name} for {EggIncStatics.GetEggByContract(coop.Contract, await db.GetCustomEggsAsync()).emoji} {coop.Contract.Name} - {coopChannel.Mention}", db);
            if(dmResult != DMResult.Success) {
                await coopChannel.SendMessageAsync($"{discordUser.Mention} {Message}: {coop.Name} for {EggIncStatics.GetEggByContract(coop.Contract, await db.GetCustomEggsAsync()).emoji} {coop.Contract.Name} - {coopChannel.Mention} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
            }
        }

        public async Task AddDemeritAndRemoveFromCoop(string reason, DBUser user, ApplicationDbContext _db, UserCoopXref xref, SocketGuildUser discordUser, IThreadChannel coopChannel, Guild dbGuild, Coop coop, bool alwaysRemove) {
            var demeritChannel = await GetDemeritChannel(dbGuild);
            if(demeritChannel is null) {
                if(alwaysRemove) {
                    _db.Remove(xref);
                }
                return;
            }
            var existingDemerit = await _db.Demerit.AnyAsync(x => x.ContractID == coop.ContractID && x.UserId == user.Id);
            if(existingDemerit || xref.JoinedCoop) {
                await coopChannel.SendMessageAsync($"Removing {discordUser?.Mention ?? user.DiscordUsername} due to: {reason}");
                _db.Remove(xref);
                await _db.SaveChangesAsync();
            } else {
                _db.Remove(xref);
                if(user.IsFreshEgg()) {
                    await coopChannel.SendMessageAsync($"{discordUser.Mention ?? user.DiscordUsername}: You will start receiving demerits for this 7 days after joining the server. {reason} ");
                } else {
                    var demerit = new Demerit {
                        When = DateTimeOffset.Now,
                        AdminUserId = Guid.Empty,
                        UserId = user.Id,
                        Id = Guid.NewGuid(),
                        Reason = reason
                    };
                    _db.Demerit.Add(demerit);
                    await _db.SaveChangesAsync();
                    var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();
                    var demeritText = $"Demerit added to {discordUser?.Mention ?? user.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                    await coopChannel.SendMessageAsync(demeritText);
                    if(count >= 3)
                        demeritText = $"**{demeritText}**";
                    await demeritChannel.SendMessageAsync(demeritText + $" {coopChannel.Mention}");
                }
            }

        }

        public async Task CheckHighestEBJoined(Coop coop, List<UserWithStatus> usersWithStatus, CoopDetails coopDetails, IThreadChannel coopChannel, ApplicationDbContext _db, List<UserFarmDetails> usersNotJoined) {
            if(usersWithStatus.Any(x => x.Xref?.CoopSetting?.PingOnHighestEB ?? false)) {
                var highestEB2 = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                if(highestEB2 != null && !usersNotJoined.Any(x => x?.EggIncId == highestEB2.Backup.EggIncId)) {
                    foreach(var user in usersWithStatus.Where(x => x.Xref?.CoopSetting?.PingOnHighestEB ?? false)) {
                        if(user.User.DiscordId == highestEB2.DBUser.DiscordId) continue; //Don't ping them if they are the highest EB
                        user.Xref.CoopSetting.PingOnHighestEB = false;
                        user.Xref.UpdateCoopSetting();
                        await _db.SaveChangesAsync();
                        await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Highest EB ({highestEB2.DiscordUser?.GetCleanName()} at {highestEB2.Backup.EarningsBonus.ToEggString()}) has joined", coop);
                    }
                }
            }
        }

        public async Task CheckCompleteOnCheckIn(Coop coop, List<UserWithStatus> usersWithStatus, IThreadChannel coopChannel, ApplicationDbContext _db) {
            var anybodyWithPingSetting = usersWithStatus.Where(x => x.Xref?.CoopSetting?.PingOnCompleteOnCheckIn ?? false);

            if(anybodyWithPingSetting.Any()) {
                foreach(var user in anybodyWithPingSetting) {
                    user.Xref.CoopSetting.PingOnCompleteOnCheckIn = false;
                    user.Xref.UpdateCoopSetting();
                    await _db.SaveChangesAsync();
                    await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Your co-op will complete once everyone checks in.", coop);
                }
            }
        }

        public async Task CheckDeflectorChange(Ei.ContractCoopStatusResponse prevStatus, Ei.ContractCoopStatusResponse newStatus, Coop coop, List<UserWithStatus> usersWithStatus, IThreadChannel coopChannel, ApplicationDbContext _db) {
            if(prevStatus == null || coop.FinishedOrFailed() || coop.CoopEnds < DateTimeOffset.Now) {
                return;
            }
            foreach(var user in usersWithStatus.Where(x => x.Status is not null && (x.Xref?.CoopSetting?.PingOnTachyonChange ?? false))) {
                var oldTachyon = GetTachyonAmount(prevStatus.Contributors, user.Status.Uuid);
                var newTachyon = GetTachyonAmount(newStatus.Contributors, user.Status.Uuid);
                if(oldTachyon != newTachyon) {
                    var oldVal = oldTachyon * 100;
                    var newVal = newTachyon * 100;
                    await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Tachyon Deflector amount changed from {oldVal:F0}% to {newVal:F0}%", coop);
                }
            }
        }

        private static decimal GetTachyonAmount(IEnumerable<Ei.ContractCoopStatusResponse.Types.ContributionInfo> contributions, string currentUserUuid) {
            var matches = contributions.Where(x => x.Uuid != currentUserUuid && x.BuffHistory.Count > 0);
            var histories = matches.Select(x => x.BuffHistory.Last());
            return histories.Sum(x => (decimal)x.EggLayingRate - 1);
        }

        public async Task<SocketTextChannel> GetDemeritChannel(Guild dbGuild) {
            if(_demeritChannels.ContainsKey(dbGuild.Id)) return _demeritChannels[dbGuild.Id];

            var channel = await _client.GetChannelAsync(GuildChannelType.DemeritLogChannel, dbGuild);
            if(channel is not null) {
                try {
                    _demeritChannels.Add(dbGuild.Id, channel);
                } catch(ArgumentException) {

                }
                return channel;
            }

            return null;
        }

        public bool CheckForCreator(Coop coop, CoopDetails coopDetails) {
            if(String.IsNullOrEmpty(coop.CreatorID)) {
                var creator = coopDetails.CoopParticipants.FirstOrDefault(x => x.Backup is not null && x.Backup.Farms.Any(y => y.Creator && y.CoopId == coop.Name.ToLower() && y.ContractId == coop.ContractID));
                if(creator != null) {
                    coop.CreatorID = creator.EggIncId;
                    return true;
                }
            }
            return false;
        }
    }
}