using Discord;
using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Coops;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.AfxSets;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.JsonData;
using EGG9000.Common.Services;
using Ei;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {

        private async Task<bool> LoadCoop(CoopProcessingContext ctx) {
            ctx.SlashCommands = await ctx.Guild.GetCachedApplicationCommands();

            ctx.Coop = await ctx.Db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).FirstOrDefaultAsync(x => x.Id == ctx.CoopId, ctx.Cancellation);
            if(ctx.Coop == null) {
                _logger.LogWarning("Unable to find co-op with id {coopid}", ctx.CoopId);
                return false;
            }
            ctx.CoopName = ctx.Coop.Name;

            if(ctx.Coop.ContractID == "test-contract") {
                return false;
            }

            return true;
        }

        private async Task<bool> ResolveThread(CoopProcessingContext ctx) {
            ctx.CoopThread = ctx.Guild.ThreadChannels.FirstOrDefault(x => x.Id == ctx.Coop.ThreadID);

            if(ctx.CoopThread == null) {
                var restguild = await _client.Rest.GetGuildAsync(ctx.Guild.Id);
                try {
                    var coopHeaderChannel = await restguild.GetTextChannelAsync(ctx.Coop.ThreadParentChannel);
                    if(coopHeaderChannel != null) {
                        ctx.CoopThread = (await coopHeaderChannel.GetActiveThreadsAsync()).FirstOrDefault(t => t.Id == ctx.Coop.ThreadID);
                    }
                } catch(Exception) { }
            }

            if(ctx.CoopThread == null) {
                ctx.CoopThread = ctx.Guild.ThreadChannels.FirstOrDefault(x => x.Name.EndsWith(ctx.Coop.Name));
                _logger.LogWarning("Co-op thread ID has changed for {coop}", ctx.Coop.Name);
            }

            if(ctx.CoopThread == null) {
                _logger.LogWarning("ERROR FINDING THREAD FOR CO-OP: {coopName}", ctx.Coop.Name);
                return false;
            }

            if(ctx.CoopThread.IsArchived) {
                try {
                    await _queue.EnqueueLowAsync(async () => { await ctx.CoopThread.ModifyAsync(t => t.Archived = false); return true; });
                } catch(Exception) {
                    _logger.LogError("Could not un-archive thread for {coop}.", ctx.Coop.Name);
                    return false;
                }
            }

            ctx.CoopDiscordUsers = ctx.CoopThread is SocketTextChannel channel ? channel.Users.ToList().Select(x => (IGuildUser)x).Select(u => u.Id).Distinct().ToList() : [.. ctx.Coop.UserCoopsXrefs.Where(u => u.AddedToChannel).Select(u => u.User.DiscordId).Distinct()];

            ctx.Timings.Set("GetStatus");
            return true;
        }

        private async Task<bool> FetchStatus(CoopProcessingContext ctx) {
            // GetStatus and the EggIncApi calls further below (join/kick retry, creator-still-in
            // check) are network-only stretches with no _db access in between. Release the pooled
            // connection for their duration instead of holding it open-but-idle; EF reopens it
            // lazily on the next _db access (GetCustomEggsAsync below).
            await ctx.Db.Database.CloseConnectionAsync();

            ctx.StatusResponse = new StatusResponse();
            try {
                ctx.StatusResponse = await GetStatus(ctx.Coop, ctx.CoopThread, ctx.Cancellation);
            } catch(TaskCanceledException) {
                _logger.LogWarning("Timeout getting status for {coopName}", ctx.Coop.Name);
                return false;
            }

            ctx.Timings.Set("Got status");

            ctx.Status = ctx.StatusResponse.Status;

            if(ctx.Status is null) {
                _logger.LogWarning($"Status for {ctx.Coop.Name} is null");
                return false;
            }

            if(!ctx.Coop.SuccessfullyStarted && ctx.StatusResponse.Status.Success) {
                ctx.Coop.SuccessfullyStarted = true;
            }

            await CheckForCoopCreatorStillIn(ctx.Coop, ctx.Status);
            return true;
        }

        private async Task<bool> SyncLeagueAndAttemptRestart(CoopProcessingContext ctx) {
            if(ctx.Coop.League != (uint)ctx.Status.Grade && ctx.Status.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset) {
                _logger.LogInformation("Updating co-op league: {coopName} from {oldLeague} to {newLeague}", ctx.Coop.Name, (Contract.Types.PlayerGrade)ctx.Coop.League, ctx.Status.Grade);
                ctx.Coop.League = (uint)ctx.Status.Grade;
            }
            if(ctx.Coop.League == 0) {
                _logger.LogWarning("{coopName} is returning Grade as 0", ctx.CoopName);
                return false;
            } else if(ctx.Status.SecondsRemaining == ctx.Coop.Contract.Details.GradeSpecs[(int)ctx.Coop.League - 1].LengthSeconds) {
                //Attempt to fix not started co-op
                _logger.LogInformation("Attempting to start co-op: {coopName}", ctx.Coop.Name);

                var joinResponse = await EggIncApi.Post<JoinCoopResponse, JoinCoopRequest>(new JoinCoopRequest {
                    ContractIdentifier = ctx.Coop.ContractID,
                    CoopIdentifier = ctx.Coop.Name.ToLower(),
                    UserId = ctx.Coop.CreatorID, ClientVersion = EggIncApi.ClientVersion, Eop = 1, SoulPower = 24, Grade = (Contract.Types.PlayerGrade)ctx.Coop.League, Platform = Platform.Droid, SecondsRemaining = ctx.Coop.Contract.Details.LengthSeconds, PointsReplay = false, UserName = "."
                }, ctx.Coop.CreatorID, false);

                var statusUpdate = new ContractCoopStatusUpdateRequest {
                    ContractIdentifier = ctx.Coop.ContractID,
                    CoopIdentifier = ctx.Coop.Name.ToLower(),
                    Eop = 1, SoulPower = 24, UserId = ctx.Coop.CreatorID, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = ctx.Coop.CreatorID, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                    ProductionParams = new FarmProductionParams {
                        FarmPopulation = 1, Delivered = 1, Elr = 1, FarmCapacity = 1, Ihr = 1, Sr = 1
                    }
                };

                var response = await EggIncApi.Post<ContractCoopStatusUpdateResponse, ContractCoopStatusUpdateRequest>(statusUpdate, statusUpdate.UserId, false);

                await Task.Delay(1000, ctx.Cancellation);
                var checkStatus = await EggIncApi.GetCoopStatus(ctx.Coop.ContractID, ctx.Coop.Name.ToLower(), ctx.Coop.CreatorID, cancellationToken: ctx.Cancellation);

                var kickPlayer = await EggIncApi.Send(new KickPlayerCoopRequest {
                    ClientVersion = EggIncApi.ClientVersion,
                    ContractIdentifier = ctx.Coop.ContractID,
                    CoopIdentifier = ctx.Coop.Name.ToLower(),
                    PlayerIdentifier = ctx.Coop.CreatorID,
                    Reason = KickPlayerCoopRequest.Types.Reason.Private,
                    RequestingUserId = ctx.Coop.CreatorID
                }, ctx.Coop.CreatorID);
            }
            return true;
        }

        private async Task BuildCoopDetails(CoopProcessingContext ctx) {
            ctx.CustomEggs = await ctx.Db.GetCustomEggsAsync();

            if(ctx.Coop.League == 0) {
                ctx.Coop.League = (uint)ctx.Status.Grade;
            }

            ctx.CoopDetails = new CoopDetails(ctx.Coop, ctx.Coop.Contract, ctx.Coop.League, ctx.Users, ctx.CustomEggs, _client.Gateway, ctx.Status);

            _ = CheckForCreator(ctx.Coop, ctx.CoopDetails);
        }

        private void FixChannelPermissions(CoopProcessingContext ctx) {
            var headChannel = ctx.CoopThread.CategoryId.HasValue ? ctx.Guild.GetTextChannel(ctx.CoopThread.CategoryId.Value) : null;
            if(headChannel is not null) {
                foreach(var participant in ctx.CoopDetails.CoopParticipants.Where(x => x.DBUser is not null)) {
                    if(participant.CoopStatus?.UserName == "[departed]") continue;
                    var overflowGuildUser = ctx.Guild.GetUser(participant.DBUser.DiscordId);
                    if(overflowGuildUser is null || overflowGuildUser.GetPermissions(headChannel).ViewChannel) continue;
                    var capturedUser = overflowGuildUser;
                    var capturedChannel = headChannel;
                    _queue.EnqueueLow(() => capturedChannel.AddPermissionOverwriteAsync(capturedUser, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Deny, sendMessagesInThreads: PermValue.Allow)));

                    if(!ctx.Coop.FinishedOrFailedOrExpired()) {
                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Fixing permission for {overflowGuildUser.Mention}"));
                    }
                }
            }
        }

        private async Task HandleCreatorNotKicked(CoopProcessingContext ctx) {
            if(ctx.CoopDetails.CoopParticipants.Any(x => x.Account?.Id == EggIncApi.UserId) && !ctx.Coop.FinishedOrFailedOrExpired()) {
                var success = await EggIncApi.Send(new KickPlayerCoopRequest { Reason = KickPlayerCoopRequest.Types.Reason.Private, ClientVersion = EggIncApi.ClientVersion, ContractIdentifier = ctx.Coop.ContractID, CoopIdentifier = ctx.Coop.Name, PlayerIdentifier = EggIncApi.UserId, RequestingUserId = EggIncApi.UserId, Rinfo = EggIncApi.GetInfo(EggIncApi.UserId) }, EggIncApi.UserId);
                _logger.LogInformation("Attempted to kick co-op creator to free up spot for {co-op}, it returned {status}", ctx.Coop.Name, success.ToString());
            }
        }

        private async Task ReconcileMissingXrefs(CoopProcessingContext ctx) {
            var participantsInCoopButWithoutXref = ctx.CoopDetails.CoopParticipants.Where(x =>
                x.DBUser is not null &&
                x.Xref is null &&
                x.CoopStatus is not null &&
                x.Backup.Farms.Any(f => f.CoopId is not null && f.CoopId.Equals(ctx.Coop.Name, StringComparison.CurrentCultureIgnoreCase))
            ).ToList();
            foreach(var participant in participantsInCoopButWithoutXref) {
                var xref = new UserCoopXref {
                    EggIncId = participant.Backup.EggIncId,
                    CreatedOn = DateTimeOffset.UtcNow,
                    JoinedCoop = true,
                    UserId = participant.DBUser.Id,
                    WasAssigned = false,
                    CoopId = ctx.Coop.Id
                };
                ctx.Db.Add(xref);
                participant.AddXref(xref);
            }
            var (xrefsSaved, _) = await ctx.Db.SaveChangesAsyncRetry(retryCount: 3, cancellationToken: CancellationToken.None, logger: _logger);
            // Guard before the loop: xrefsSaved is the same for all participants in this batch,
            // so log once here rather than once per participant inside the loop.
            // Skipping pings on failure prevents repeat pings if the xref never persisted.
            if(!xrefsSaved) {
                _logger.LogError("ProcessCoop {coop}: failed to save xrefs - skipping 'has joined' pings to prevent repeats", ctx.Coop.Name);
            } else {
                foreach(var participant in participantsInCoopButWithoutXref) {
                    if(ctx.Coop.UserCoopsXrefs.Any(x => x.UserId == participant.DBUser.Id && x.WasAssigned && !x.JoinedCoop)) {
                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"<@{participant.DBUser.DiscordId}>, it looks like you might have joined the coop with the wrong account."));
                        await BoolSendDm(participant.DiscordUser, $"It looks like you might have joined the coop with the wrong account in {ctx.CoopThread.Mention}.", ctx.Db);
                    } else {
                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"<@{participant.DBUser.DiscordId}> has joined the co-op"));
                    }
                }
            }
            ctx.Timings.Set(1);
        }
        private async Task PopulateUserStatuses(CoopProcessingContext ctx) {
            // Set time joined so we can later track and alert when a new BG might be worth it.
            ctx.CoopDetails.CoopParticipants.Where(x => x.Xref is not null && x.Xref?.Joined is null && x.CoopStatus is not null).ToList().ForEach(x => x.Xref.Joined = DateTimeOffset.UtcNow);

            ctx.UsersWithStatus = ctx.CoopDetails.CoopParticipants.Select(x => new UserWithStatus {
                Status = x.CoopStatus,
                Xref = x.Xref,
                User = x.DBUser,
                Backup = x.Backup,
                DiscordUser = x.DBUser is not null ? ctx.ParentGuild.GetUser(x.DBUser.DiscordId) : null
            }).ToList();

            await CheckDeflectorChange(ctx.Coop.LastStatusUpdate, ctx.Status, ctx.Coop, ctx.UsersWithStatus, ctx.CoopThread, ctx.Db);

            ctx.Timings.Set("1.1");
            ctx.UsersNotJoined = ctx.CoopDetails.CoopParticipants.Where(x => x.CoopStatus is null).ToList();

            foreach(var user in ctx.UsersWithStatus) {
                if(user.Backup != null) {
                    var awayTime = Research.GetTotalSiloCapacity(user.Backup);
                    var farm = user.Backup?.Farms?.FirstOrDefault(x => x.CoopId != null && x.CoopId.Equals(ctx.Coop.Name, StringComparison.CurrentCultureIgnoreCase));
                    if(farm != null) {
                        _bugSnag.Breadcrumbs.Leave($"User: {user.DiscordUser?.Id}, {user.Backup?.EggIncId}");
                        user.FarmStats = farm.WithStats(user.Backup, ctx.Coop, ctx.CustomEggs, contract: ctx.Coop.Contract);
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

            ctx.Timings.Set(2);
        }

        private async Task ReconcileUsersWithoutXref(CoopProcessingContext ctx) {
            var usersWithoutXref = ctx.CoopDetails.CoopParticipants.Where(x => x.DBUser is not null && x.Xref is null);
            foreach(var user in usersWithoutXref) {
                if(!ctx.CoopDiscordUsers.Any(x => x == user.DBUser.DiscordId)) {
                    ctx.UsersNeedingChannelPermissions.Add(user.DBUser.DiscordId);
                } else {
                    var xref = new UserCoopXref {
                        WaitingOnStarter = false,
                        UserId = user.DBUser.Id,
                        EggIncId = user.Backup.EggIncId,
                        AddedToChannel = false,
                        CoopId = ctx.Coop.Id,
                        CreatedOn = DateTimeOffset.UtcNow,
                        JoinedCoop = true,
                        Starter = false,
                        LastStatus = user.CoopStatus is not null ? new ContributionInfoCompact(user.CoopStatus) : null,
                        WasAssigned = false
                    };
                    ctx.Db.UserCoopXrefs.Add(xref);
                    if(ctx.Coop.UserCoopsXrefs.Any(x => x.UserId == user.DBUser.Id && x.WasAssigned && !x.JoinedCoop)) {
                        await WrongAccountWarning(user, ctx.CoopThread, ctx.Db, user.Backup.EggIncId);
                    } else {
                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"<@{user.DBUser.DiscordId}> has joined the co-op"));
                    }
                }
            }
        }

        private async Task ProcessSleepingParticipants(CoopProcessingContext ctx) {
            foreach(var participant in ctx.CoopDetails.CoopParticipants) {
                await HandleSleeping(participant, ctx.CoopThread, ctx.Coop, ctx.Db, ctx.DbGuild);
            }
            ctx.Timings.Set(3);
        }

        private async Task UpdateCoopLifecycleStatus(CoopProcessingContext ctx) {
            ctx.League = (int?)ctx.Coop.League ?? 0;
            ctx.TargetAmount = ctx.Coop.Contract.Details.GetGoals(ctx.League).Max(x => x.TargetAmount);
            ctx.AmountWithOffline = ctx.CoopDetails.CoopParticipants.Where(x => x.CoopStatus is not null).Sum(x => x.EggsShipped + x.OfflineEggs);
            ctx.RemainingAmount = ctx.TargetAmount - ctx.AmountWithOffline;
            ctx.TotalRate = ctx.Status.Participants.Sum(x => x.ContributionRate);

            ctx.TimeRemaining = GetTimeRemainingValue(ctx.TargetAmount, ctx.TotalRate, ctx.AmountWithOffline);

            ctx.WaitingOn = ctx.UsersWithStatus.Where(x => !x.Status?.Finalized ?? false);
            var isFinished = ctx.Coop.Finished || ctx.Status.Finished();
            if(!ctx.Coop.FinalizedFinishedOrFailed()) {
                await CheckHighestEBJoined(ctx.Coop, ctx.UsersWithStatus, ctx.CoopDetails, ctx.CoopThread, ctx.Db, ctx.UsersNotJoined);

                if(!isFinished && !ctx.Coop.ProjectedToFinish && ctx.CoopDetails.PercentProjectedForJoined >= 100 && ctx.Coop.CoopEnds > DateTimeOffset.UtcNow) {
                    ctx.Coop.ProjectedToFinish = true;
                    _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Coop {ctx.Coop.Name} is now projected to finish!"));
                }

                if(!isFinished && ctx.Status.SecondsRemaining > 1 && ctx.Coop.ProjectedToFinish && ctx.CoopDetails.PercentProjectedForJoined < 100 && ctx.Coop.CoopEnds > DateTimeOffset.UtcNow) {
                    ctx.Coop.ProjectedToFinish = false;
                    _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Coop {ctx.Coop.Name} is **no longer** projected to finish."));
                }

                if(!ctx.Coop.Finished && ctx.Status.Finished()) {
                    if(ctx.WaitingOn.Any()) {
                        ctx.Coop.Status = CoopStatus.Completed;
                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Coop {ctx.Coop.Name} is finished, and is waiting for users to check-in!"));
                    } else {
                        ctx.FinalChannelUpdate = true;
                        ctx.Coop.Status = CoopStatus.CompletedAllCheckIn;
                        ctx.Coop.ThreadArchived = true;
                        _queue.EnqueueLow(() => ctx.CoopThread.ModifyAsync(t => t.AutoArchiveDuration = ThreadArchiveDuration.OneDay));
                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Coop {ctx.Coop.Name} is finished!"));
                    }
                    ctx.Coop.CoopCompleted = DateTimeOffset.UtcNow;
                    ctx.Coop.Finished = true;

                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                    await HandleUnjoins(ctx.UsersNotJoined, ctx.Users, ctx.DbGuild, ctx.Coop, ctx.Db, ctx.CoopThread);
                }

                if(ctx.Coop.Finished && ctx.Coop.Status != CoopStatus.CompletedAllCheckIn && !ctx.WaitingOn.Any()) {
                    ctx.FinalChannelUpdate = true;
                    ctx.Coop.Status = CoopStatus.CompletedAllCheckIn;
                    ctx.Coop.ThreadArchived = true;
                    _queue.EnqueueLow(() => ctx.CoopThread.ModifyAsync(t => t.AutoArchiveDuration = ThreadArchiveDuration.OneDay));
                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                }
            }

            ctx.Timings.Set(4);
        }

        private void UpdateCurrentUserCount(CoopProcessingContext ctx) {
            if(ctx.Coop.CurrentUsers != ctx.Status.Contributors.Count) {
                ctx.Coop.CurrentUsers = ctx.Status.Contributors.Count;
                ctx.Coop.MaxUsers = ctx.Coop.Contract.MaxUsers;
            }
        }
        private async Task SyncChannelMembership(CoopProcessingContext ctx) {
            var threadObj = ctx.CoopThread as SocketThreadChannel;
            ctx.CurrentUserDiscordIds = ctx.Coop.UserCoopsXrefs.Where(x => x.JoinedCoop).Select(x => ctx.Users.FirstOrDefault(u => u.User.Id == x.UserId)).Where(x => x is not null).Select(x => x.User.DiscordId);
            foreach(var userStatus in ctx.CoopDetails.CoopParticipants.Where(x => x.Xref != null && x.DiscordUser is not null)) {
                if(!userStatus.Xref.AddedToChannel) {
                    ctx.UsersNeedingChannelPermissions.Add(userStatus.DiscordUser.Id);
                } else if(userStatus.DiscordUser is not null && !threadObj.Users.Any(x => x.Id == userStatus.DiscordUser.Id) && !ctx.CurrentUserDiscordIds.Any(u => u == userStatus.DiscordUser.Id)) {
                    ctx.UsersNeedingChannelPermissions.Add(userStatus.DiscordUser.Id);
                }

                if(!userStatus.Xref.JoinedCoop && userStatus.CoopStatus is not null) {
                    userStatus.Xref.JoinedCoop = true;
                    // Joined - drop from the "find my coop" lookup (they're in the thread now).
                    _provider.GetService<CoopAssignmentLookup>()?.Remove(userStatus.Xref.UserId, ctx.Coop.ContractID);
                    var unjoinedRole = ctx.Guild.Roles.FirstOrDefault(x => x.Id == KnownRoles.Unjoined);
                    if(unjoinedRole != null) {
                        await userStatus.DiscordUser.RemoveRoleAsync(unjoinedRole);
                    }
                }
            }
        }

        private async Task SendCoopCreatedDms(CoopProcessingContext ctx) {
            foreach(var xref in ctx.Coop.UserCoopsXrefs) {
                var user = ctx.Users.FirstOrDefault(x => x.User.Id == xref.UserId);
                if(xref.CoopSetting is null && user is not null) {
                    xref.CoopSetting = new CoopSetting(xref, user.User, ctx.DbGuild);
                    // PingOnCoopCreated: only when not already joined. PingOnCoopCreatedEvenIfJoined:
                    // regardless of join state (restores the pre-fix behavior for users who opt in).
                    var pingEvenIfJoined = xref.CoopSetting.PingOnCoopCreatedEvenIfJoined;
                    var pingIfNotJoined = xref.CoopSetting.PingOnCoopCreated && !xref.JoinedCoop;
                    if(pingEvenIfJoined || pingIfNotJoined) {
                        xref.CoopSetting.PingOnCoopCreated = false;
                        xref.CoopSetting.PingOnCoopCreatedEvenIfJoined = false;
                        xref.UpdateCoopSetting();
                        await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                        await SendDMWarning(ctx.Db, ctx.ParentGuild.GetUser(user.User.DiscordId), ctx.CoopThread, "Co-op has been created", ctx.Coop);
                    } else {
                        xref.UpdateCoopSetting();
                        await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                    }
                }
            }
            ctx.Timings.Set("5.1");
        }

        private async Task CompileAndSendChannelPings(CoopProcessingContext ctx) {
            var pingsLeft = ctx.UsersNeedingChannelPermissions.Distinct().Select(id => $"<@{id}>").ToList() ?? [];

            if(!ctx.Coop.RolesAddedToThread) {
                List<ulong> roleMembersCaught = [];
                try {
                    (await ctx.CoopThread.GetParentChannelAsync())?.Category?.PermissionOverwrites?
                        .Where(p => p.Permissions.ViewChannel == PermValue.Allow && p.TargetType == PermissionTarget.Role).ToList()
                        .Select(ow => ctx.Guild.GetRole(ow.TargetId)).Where(r => r != null).ToList()
                        .ForEach(role => {
                            if(role.Members.Any(m => !ctx.CurrentUserDiscordIds.Any(u => u == m.Id) && !roleMembersCaught.Contains(m.Id))) {
                                pingsLeft.Add(role.Mention);
                                roleMembersCaught.AddRange(role.Members.Select(m => m.Id).ToList());
                            }
                        });
                    ctx.Coop.RolesAddedToThread = true;
                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                } catch(Exception) {
                    _logger.LogInformation("Failed to compile role pings for {coop}", ctx.CoopName);
                }
            }

            ctx.Timings.Set("5.2");
            if(pingsLeft.Any()) {
                var currentContent = "";
                var pingsPerCycle = 1500 / 22;
                IUserMessage editPingsInto = null;
                var deleteAfter = false;

                try {
                    var pins = await ctx.CoopThread.GetPinnedMessagesAsync();
                    IUserMessage existingBotMessage = pins.Where(m => m.Author.IsBot && m.Content != "\u17B5").LastOrDefault() as IUserMessage;

                    if(existingBotMessage != null) {
                        editPingsInto = existingBotMessage;
                        currentContent = existingBotMessage.Content;
                        pingsPerCycle = (1500 - currentContent.Length) / 22;
                    } else {
                        editPingsInto = await _queue.EnqueueLowAsync(() => ctx.CoopThread.SendMessageAsync("[Ping into]"));
                        deleteAfter = true;
                    }
                    while(pingsLeft.Count > 0) {
                        var pingsBatch = pingsLeft.Take(pingsPerCycle).ToList();
                        var pingsMessage = editPingsInto;
                        var capturedContent = currentContent;
                        _queue.EnqueueLow(() => pingsMessage.ModifyAsync(m => m.Content = capturedContent + " " + string.Join(" ", pingsBatch)));
                        // Remove pingsPerCycle entries from pingsLeft
                        pingsLeft.RemoveRange(0, Math.Min(pingsPerCycle, pingsLeft.Count));
                    }
                    if(deleteAfter) _queue.EnqueueLow(() => editPingsInto.DeleteAsync());
                } catch {
                    _logger.LogWarning("Failed to send/coalesce pings for {coop}", ctx.CoopName);
                }
            }
            ctx.Timings.Set("5.3");
        }

        private void MarkChannelPermissionsAdded(CoopProcessingContext ctx) {
            var usersAdded = ctx.UsersNeedingChannelPermissions.Distinct().ToList();
            foreach(var userAdded in usersAdded) {
                var xref = ctx.CoopDetails.CoopParticipants.FirstOrDefault(x => x.DiscordUser?.Id == userAdded);
                if(xref?.Xref != null) {
                    xref.Xref.AddedToChannel = true;
                }
            }
        }
        private async Task ProcessUnjoinedParticipants(CoopProcessingContext ctx) {
            ctx.MissingFromServer = false;
            ctx.Timings.Set("5.4");
            if(ctx.UsersNotJoined.Count == 0 && !ctx.Coop.FinishedOrFailed()) {
                ctx.Coop.Status = CoopStatus.AllAssignedJoined;
            } else {
                var userList = new List<string>();
                foreach(var userFarmDetails in ctx.UsersNotJoined) {
                    try {
                        var user = ctx.Users.FirstOrDefault(x => x.User.Id == userFarmDetails.Xref.GetID())?.User;
                        user ??= await ctx.Db.DBUsers.FirstOrDefaultAsync(x => x.Id == userFarmDetails.Xref.UserId, ctx.Cancellation);

                        var discordUser = user == null ? null : ctx.ParentGuild.GetUser(user.DiscordId);

                        var mention = "";

                        if(discordUser == null) {
                            mention = $"{user.DiscordUsername} (Missing from server)";
                            ctx.MissingFromServer = true;
                        } else if(user.EggIncAccounts.Count > 1) {
                            var eggaccount = user.EggIncAccounts.FirstOrDefault(x => x.Id == userFarmDetails.Xref.EggIncId);
                            if(eggaccount != null)
                                mention = $"{discordUser.Mention} ({eggaccount.Backup?.UserName ?? "No Name"})";
                        } else {
                            mention = discordUser?.Mention;
                        }

                        if(userFarmDetails.Account is not null || userFarmDetails.Backup is not null) {
                            var grade = userFarmDetails.Account?.LastGrade ?? Ei.Contract.Types.PlayerGrade.GradeUnset;
                            if((uint)grade != ctx.Coop.League && !(ctx.Coop.Contract.cc_only || ctx.Coop.AnyLeague)) {
                                mention += $" (Wrong {grade})";
                            }
                        }

                        userList.Add(mention);

                        if(!ctx.Coop.Finished && ctx.Coop.Status != CoopStatus.Failed && ctx.Coop.CoopEnds > DateTimeOffset.UtcNow) {
                            if(discordUser != null) {
                                if(!userFarmDetails.Xref.JoinWarning24TillFinish && ctx.TimeRemaining.TotalHours < 24 && userFarmDetails.Xref.CreatedOn < DateTimeOffset.UtcNow.AddHours(-1)) {
                                    userFarmDetails.Xref.JoinWarning24TillFinish = true;
                                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                                    await SendDMWarning(ctx.Db, discordUser, ctx.CoopThread, $"reminder to join - co-op will be finished in under {Math.Ceiling(ctx.TimeRemaining.TotalHours)} hours", ctx.Coop);
                                } else if(!userFarmDetails.Xref.JoinWarning24h && userFarmDetails.Xref.CreatedOn < DateTimeOffset.UtcNow.AddHours(-24)) {
                                    userFarmDetails.Xref.JoinWarning24h = true;
                                    userFarmDetails.Xref.JoinWarning12h = true;
                                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                                    await SendDMWarning(ctx.Db, discordUser, ctx.CoopThread, $"reminder to join - 24h since added to co-op", ctx.Coop);
                                } else if(!userFarmDetails.Xref.JoinWarning12h && userFarmDetails.Xref.CreatedOn < DateTimeOffset.UtcNow.AddHours(-12)) {
                                    userFarmDetails.Xref.JoinWarning12h = true;
                                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                                    await SendDMWarning(ctx.Db, discordUser, ctx.CoopThread, $"reminder to join - 12h since added to co-op", ctx.Coop);
                                }
                            }

                            // Removal runs even when discordUser is null. A user who left the server while
                            // assigned but not joined can never join, so once past the kick window we still
                            // need to drop their xref to free the spot for /findcoopforuser.
                            var hoursToKick = ctx.Coop.Contract.cc_only ? 24 : 18;
                            if(user is not null && userFarmDetails.Xref.CreatedOn < DateTimeOffset.UtcNow.AddHours(-hoursToKick) && !userFarmDetails.Xref.NoDemerit) {
                                var accountName = user.EggIncAccounts.Count > 1 ? $" ({user.EggIncAccounts.Where(a => a.Id == userFarmDetails.Xref.EggIncId).FirstOrDefault()?.Backup?.UserName})" : "";
                                var kickReason = discordUser != null
                                    ? $"Failed to join {ctx.Coop.Contract.Name} within {hoursToKick} hours{accountName}, you have been removed from the co-op and your space might be filled."
                                    : $"Left the server without joining {ctx.Coop.Contract.Name} within {hoursToKick} hours{accountName}, removed from the co-op to free the space.";
                                await AddDemeritAndRemoveFromCoop(kickReason, user, ctx.Db, userFarmDetails.Xref, discordUser, ctx.CoopThread, ctx.DbGuild, ctx.Coop, false);
                            }
                        }

                        if(!userFarmDetails.Xref.OutsideCoop && ctx.Coop.GuildId == _CPGuildId && !ctx.Coop.FinishedOrFailedOrExpired() && userFarmDetails.Farm is not null) {
                            var farm = userFarmDetails.Farm;
                            if(farm.CoopId.Equals(ctx.Coop.Name, StringComparison.OrdinalIgnoreCase)) {
                                _logger.LogInformation("Coop matches but the game doesn't see {user} in the coop for {coop}. Marked as outside coop.", user.DiscordUsername, ctx.Coop.Name);
                                _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"{discordUser?.Mention ?? user.DiscordUsername}, it looks like your game thinks you have joined the co-op but the game's servers don't see you in the co-op. Please check with the other members of the co-op to verify they don't see you, if they don't then you will need to restart the contract and join again. After you do make sure the bot can see you in the co-op."));
                                userFarmDetails.Xref.OutsideCoop = true;
                                await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                            } else if(farm.CoopId.Length > 0 && farm.FarmType == FarmType.Contract) {
                                _logger.LogInformation("Outside coop detected for {user}. Assigned coop is {coop}, but the backup shows an entered code of {enteredCoop}", user.DiscordUsername, ctx.Coop.Name, farm.CoopId);
                                // This should always happen so that no matter what, we're only sending one message
                                userFarmDetails.Xref.OutsideCoop = true;

                                // Calculate a similarity scoreing to weed out typos
                                var similarityScoring = LevenshteinRatio(farm.CoopId.ToLower(), ctx.Coop.Name.ToLower());
                                if(similarityScoring >= 80) { // Almost certainly a typo
                                    var typoMessage = $"It looks like you may have typo-ed when joining your co-op <#{ctx.Coop.ThreadID}>.\n\n" +
                                        $"The co-op code is `{ctx.Coop.Name}`, but your backup shows an entered code of `{farm.CoopId}`.";
                                    await SendDMWarning(ctx.Db, discordUser, ctx.CoopThread, typoMessage, ctx.Coop);
                                } else {
                                    // Check if they used 'another' co-op code (from a different contract, etc.)
                                    var otherContractXref = await ctx.Db.UserCoopXrefs
                                        .Include(c => c.Coop)
                                            .ThenInclude(c => c.Contract)
                                        .FirstOrDefaultAsync(
                                            x => x.User.DiscordId == discordUser.Id &&
                                            EF.Functions.ILike(farm.CoopId, x.Coop.Name),
                                            cancellationToken: CancellationToken.None
                                        );
                                    if(otherContractXref != null) {
                                        var otherCoopMessage = $"It looks like you may have used the wrong co-op code for {ctx.Coop.Contract.Name}.\n\n" +
                                            $"Your co-op code is `{ctx.Coop.Name}, but your backup shows an entered code of `{farm.CoopId}`, which is the code for {otherContractXref.Coop.Contract.Name}";
                                        await SendDMWarning(ctx.Db, discordUser, ctx.CoopThread, otherCoopMessage, ctx.Coop);
                                    } else {
                                        var findGuild = ctx.DbGuild;

                                        // In the case this is 'coming from' an overflow server, and the user is not in the server, we want the mention to stick regardless
                                        discordUser ??= _client.Guilds.First(g => g.Id == findGuild.Id).GetUser(userFarmDetails.Xref.User.DiscordId);

                                        var message = $"It looks like {discordUser?.Mention ?? user.DiscordUsername} has joined another co-op named {farm.CoopId}.";
                                        _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync(message));
                                        var logMessage = $"Outside co-op detected for {discordUser?.Mention ?? user.DiscordUsername} they joined *{farm.CoopId}*, but were assigned to <#{ctx.CoopThread.Id}>";
                                        _queue.EnqueueLow(() => ChannelHelper.DetermineAndSend(_client.Gateway, findGuild, GuildChannelType.OutsideCoopLog, new() { Text = logMessage }, _logger, db: _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>()));
                                    }
                                }

                                // And we always want to save the DB
                                await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                            }
                        }
                    } catch(Exception e) {
                        _bugSnag.Notify(e);
                    }
                }
                ctx.LastMessage += $"Coop **{ctx.Coop.Name}** is ready for the following to join: {string.Join(", ", userList)}\n";
            }
            ctx.Timings.Set("5.5");
        }
        private void AppendGiftingSuggestions(CoopProcessingContext ctx) {
            var giftInfos = ctx.UsersWithStatus.Where(x => x.Status is not null && x.Status.FarmInfo is not null && x.FarmStats is not null).Select(x => new {
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
                ctx.LastMessage += $"\nFarms that would benefit from gifting chickens: \n```{string.Join("\n", GetTable(table))}```\n\n";
            } else if(ctx.CoopDetails.CoopParticipants.Any(y => y.CoopStatus is not null && y.FarmStats is not null)) {
                ctx.LastMessage += "\nLooks like everyone's shipping and/or habs are full or they haven't joined yet, so gifting chickens isn't useful.\n\n";
            }
            ctx.Timings.Set("5.53");
        }

        private void AppendCommandLinks(CoopProcessingContext ctx) {
            //New commands list, each is a quick-link to start using the command
            ctx.LastMessage += "__Co-op Commands (click to use):__\n";

            ctx.Timings.Set("5.54");
            if(_client.GetChannelAsync(GuildChannelType.CallStaffChannel, ctx.Guild) != null) {
                ctx.LastMessage += $"\n</callstaff:{ctx.SlashCommands.FirstOrDefault(c => c.Name.Equals("callstaff", StringComparison.CurrentCultureIgnoreCase))?.Id ?? 0}> Use this command if you joined a co-op for the wrong contract, or have other questions or concerns";
            }
            ctx.LastMessage += $"\n</coopsettings:{ctx.SlashCommands.FirstOrDefault(c => c.Name.Equals("coopsettings", StringComparison.CurrentCultureIgnoreCase))?.Id ?? 0}> Receive DM pings for various events in the co-op";
            ctx.LastMessage += $"\n</fixfullcooperror:{ctx.SlashCommands.FirstOrDefault(c => c.Name.Equals("fixfullcooperror", StringComparison.CurrentCultureIgnoreCase))?.Id ?? 0}> If you get the error co-op is full, try running this command to free up the space.";

            ctx.Timings.Set("5.6");
        }

        private void AppendGradeWarningAndCheckIn(CoopProcessingContext ctx) {
            var userWithDifferentGrade = ctx.UsersWithStatus.FirstOrDefault(x => x.Backup is not null && x.Backup.Farms.Any(y => y.CoopId is not null && y.CoopId.Equals(ctx.Coop.Name, StringComparison.CurrentCultureIgnoreCase) && (uint)y.Grade != ctx.Coop.League));
            if(!ctx.Coop.FinishedOrFailed() && userWithDifferentGrade is not null) {
                var farm = userWithDifferentGrade.Backup.Farms.FirstOrDefault(x => x.CoopId is not null && x.CoopId.Equals(ctx.Coop.Name, StringComparison.CurrentCultureIgnoreCase));
                ctx.LastMessage += $" Warning! Looks like this co-op is the wrong grade and is actually {farm.Grade}";
            }

            if(ctx.Status.AllGoalsAchieved && ctx.Status.Participants.Any(y => !y.Finalized)) {
                ctx.LastMessage += $"\n\nWaiting on the following users to check-in: {string.Join(", ", ctx.WaitingOn.Select(x => x.DiscordUser?.Mention ?? x.Status.UserName))}";
            }
        }

        private async Task ReportTimeCheaters(CoopProcessingContext ctx) {
            var afCheaterChannel = ChannelHelper.DetermineChannelType(ctx.DbGuild, ctx.Guild, GuildChannelType.CheaterThread);
            if(afCheaterChannel != null && !ctx.Status.AllGoalsAchieved) {
                foreach(var u in ctx.UsersWithStatus.Where(u => u.Status is not null && u.Status.TimeCheatDetected && u.Xref is not null && !u.Xref.TimeCheatReported).ToList()) {
                    var account = u.User?.EggIncAccounts?.FirstOrDefault(a => a.Id.Equals(u.Backup?.EggIncId.ToLower(), StringComparison.CurrentCultureIgnoreCase));
                    if(account is null || account.TimeCheatsMarkedClean) continue;
                    await ChannelHelper.DetermineAndSend(_client.Gateway, ctx.DbGuild, GuildChannelType.CheaterThread,
                        new() { Text = $"Time cheat detected for <@{u.User.DiscordId}> ({u.Backup?.UserName ?? "_No Username_"}) in the coop <#{ctx.Coop.ThreadID}> (`{ctx.Coop.Name}`)" }
                    );
                    u.Xref.TimeCheatReported = true;
                    await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                }
            }
        }

        private void ApplyDeflectorFlags(CoopProcessingContext ctx) {
            foreach(var u in ctx.UsersWithStatus.Where(x => x.Xref is not null)) {
                u.Xref.HasTachyonDeflector = u.Xref.HasTachyonDeflector || (u.Backup?.GetAvailableArtifacts().Any(a => a.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) ?? false);
                var farm = u.Backup?.Farms.FirstOrDefault(x => x.ContractId == ctx.Coop.ContractID);
                if(farm == null)
                    continue;
                u.Xref.EquipedTachyonDeflector = u.Xref.EquipedTachyonDeflector || farm.Artifacts.Any(a => a.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates);
                if(farm.Artifacts.Any(a => a.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates))
                    u.Xref.TachyonDeflectorNotified = false;
            }
        }

        private async Task ProcessTachyonSuggestions(CoopProcessingContext ctx) {
            var usersToCheckDeflector = ctx.UsersWithStatus.Where(x => x.Status is not null && !x.Status.BuffHistory.Any(y => y.EggLayingRate > 0) && x.Backup is not null && x.Backup.ArtifactHall is not null && x.Status.Projected < ctx.UsersWithStatus.Where(y => y.Status is not null).Max(y => y.Status.Projected) / 2);
            var usersNeedToAddDeflector = new List<(UserWithStatus User, List<EggIncArtifactInstance> RecommendedSet)>();
            // Opt-in per guild, and capped at 10-person coops: the best-set search runs per user
            // and the renders fan out per suggestion, so larger coops would blow up the cycle.
            if(ctx.DbGuild.TachyonSuggestionsEnabled && ctx.Coop.MaxUsers <= 10 && !ctx.Coop.FinishedOrFailed() && ctx.Coop.CoopEnds > DateTimeOffset.UtcNow) {
                foreach(var user in usersToCheckDeflector) {
                    if(user.Xref?.TachyonDeflectorNotified == true) continue;
                    var farm = user.Backup.Farms.FirstOrDefault(x => x.ContractId == ctx.Coop.ContractID);
                    if(farm is null) continue;
                    if(farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)) continue;
                    if(!user.Backup.GetAvailableArtifacts().Any(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)) continue;

                    var withTachyonResult = ArtifactCombos.FindBestComboSet(user.Backup, farm, ctx.Coop, withTachyon: true, allowChangingStones: false, ctx.CustomEggs, _logger);
                    var withoutTachyonResult = ArtifactCombos.FindBestComboSet(user.Backup, farm, ctx.Coop, withTachyon: false, allowChangingStones: false, ctx.CustomEggs, _logger);

                    if(withTachyonResult is not null && withTachyonResult.Value.Rate > (withoutTachyonResult?.Rate ?? 0)) {
                        usersNeedToAddDeflector.Add((user, withTachyonResult.Value.Artifacts));
                    }
                }
            }

            foreach(var (deflectorUser, recommendedSet) in usersNeedToAddDeflector) {
                var mention = deflectorUser.DiscordUser?.Mention ?? $"<@{deflectorUser.User?.DiscordId}>";
                var (b64, renderError) = await AfxSetsRender.RenderSingleSetB64(recommendedSet);
                if(b64 is not null) {
                    if(deflectorUser.Xref is not null) {
                        deflectorUser.Xref.TachyonDeflectorNotified = true;
                        await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                    }
                    var capturedMention = mention;
                    var capturedB64 = b64;
                    _queue.EnqueueLow(async () => {
                        using var file = new FileAttachment(new MemoryStream(Convert.FromBase64String(capturedB64)), "TachyonSuggestion.jpeg", "Recommended Artifact Set");
                        await ctx.CoopThread.SendFilesAsync([file], text: $"{capturedMention} should equip their **Tachyon Deflector**. Recommended set:");
                    });
                } else {
                    _logger.LogWarning("Tachyon image render failed for {user}: {error}", deflectorUser.User?.DiscordId, renderError);
                    ctx.LastMessage += $"\n\n{mention} should equip their **Tachyon Deflector**.";
                    if(deflectorUser.Xref is not null) {
                        deflectorUser.Xref.TachyonDeflectorNotified = true;
                        await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                    }
                }
            }
        }

        private void ApplyFullStatus(CoopProcessingContext ctx) {
            if(ctx.Status.Contributors.Count == ctx.Coop.MaxUsers && ctx.Coop.Status != CoopStatus.Completed && ctx.Coop.Status != CoopStatus.Failed) {
                ctx.Coop.Status = CoopStatus.Full;
            }
        }

        private async Task ApplyFailedStatus(CoopProcessingContext ctx) {
            if(ctx.Coop.Status != CoopStatus.Failed && ctx.Status.Failed()) {
                if(ctx.Coop.Contract.GoodUntil > DateTimeOffset.UtcNow) {
                    _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Co-op {ctx.Coop.Name} failed to reach all the goals and the contract is still available for {(ctx.Coop.Contract.GoodUntil - DateTimeOffset.UtcNow).Humanize()} if you want to restart and try again."));
                } else {
                    _queue.EnqueueLow(() => ctx.CoopThread.SendMessageAsync($"Co-op {ctx.Coop.Name} failed to reach all the goals and the contract is no longer available."));
                }
                ctx.Coop.Status = CoopStatus.Failed;
                ctx.FinalChannelUpdate = true;
                ctx.Coop.ThreadArchived = true;
                await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);

                await HandleUnjoins(ctx.UsersNotJoined, ctx.Users, ctx.DbGuild, ctx.Coop, ctx.Db, ctx.CoopThread);
            }
            ctx.Timings.Set(6);
        }
        private async Task SendLifecycleNotifications(CoopProcessingContext ctx) {
            ctx.MissingCount = ctx.CoopDetails.CoopParticipants.Count(x => x.Xref is not null && x.CoopStatus is null);

            if(ctx.MissingCount == 0) {
                await HandlePingOnFull(ctx.Db, ctx.CoopDetails.CoopParticipants, ctx.CoopThread, _queue);
            }

            if(ctx.Status.ClearedForExit) {
                await HandlePingOnCheckedIn(ctx.Db, ctx.CoopDetails.CoopParticipants, ctx.CoopThread, _queue);
            }

            if(ctx.Coop.FinishedOrFailed()) {
                await HandleFinished(ctx.Db, ctx.CoopDetails.CoopParticipants, ctx.CoopThread, _queue);
            }

            ctx.Timings.Set(7);
        }

        private void BuildEmojisAndColor(CoopProcessingContext ctx) {
            ctx.Emojis = "";
            ctx.EmbedColor = Color.DarkGrey;
            if(ctx.Coop.Status == CoopStatus.Failed) {
                ctx.Emojis += "🚩";
            } else if(ctx.Coop.Finished) {
                ctx.Emojis += "🏁";
            } else {

                if(ctx.MissingCount > 0) {
                    if(DateTimeOffset.UtcNow > (ctx.Coop.Created + TimeSpan.FromHours(12))) {
                        if(ctx.MissingCount <= 20) {
                            ctx.Emojis += Convert.ToChar(9311 + ctx.MissingCount);
                        } else if(ctx.MissingCount <= 35) {
                            ctx.Emojis += Convert.ToChar(12881 + (ctx.MissingCount - 21));
                        } else if(ctx.MissingCount <= 50) {
                            ctx.Emojis += Convert.ToChar(12977 + (ctx.MissingCount - 36));
                        } else {
                            ctx.Emojis += "❌";
                        }
                    } else {
                        ctx.Emojis += "📶";
                    }

                    if(
                        !ctx.Coop.Finished && (
                            ctx.TimeRemaining.TotalHours < 24
                            || ctx.Status.SecondsRemaining > 0 && ctx.Status.SecondsRemaining < TimeSpan.FromHours(24).TotalSeconds
                        )
                    ) {
                        ctx.Emojis += "🔺";
                    }
                }

                var percent = ctx.CoopDetails.PercentProjectedForJoined;

                if(percent < 60) {
                    ctx.EmbedColor = Color.Red;
                    ctx.Emojis += "🔴";
                } else if(percent < 90) {
                    ctx.EmbedColor = new Color(139, 69, 19);
                    ctx.Emojis += "🤎";
                } else if(percent < 100) {
                    ctx.EmbedColor = Color.Orange;
                    ctx.Emojis += "🧡";
                } else if(percent < 105) {
                    ctx.EmbedColor = new Color(255, 255, 0);
                    ctx.Emojis += "💛";
                } else {
                    ctx.EmbedColor = Color.Green;
                    ctx.Emojis += "💚";
                }

                if(percent < 100 && ctx.CoopDetails.PercentProjected >= 100) {
                    ctx.Emojis += "💹";
                }

                if(ctx.MissingFromServer) {
                    ctx.Emojis += "👻";
                }

                if(ctx.CoopDetails.CoopParticipants.Any(x => x.Xref is null) && !ctx.Status.Public && !ctx.Coop.Finished) {
                    ctx.Emojis += "👽";
                }

                if(ctx.CoopDetails.CoopParticipants.Count > ctx.Coop.Contract.MaxUsers) {
                    ctx.Emojis += "🤢";
                }
            }
        }

        private async Task UpdateThreadName(CoopProcessingContext ctx) {
            var coopname = ctx.Emojis + ctx.Coop.Name;
            if(ctx.CoopThread.Name != coopname) {
                for(var i = 0; i < 5; i++) {
                    try {
                        await _queue.EnqueueLowAsync(async () => {
                            await ctx.CoopThread.ModifyAsync(x => x.Name = coopname);
                            return true;
                        });
                        break;
                    } catch(Exception) {
                        _logger.LogInformation("Error updating thread name for {coopName}, delaying...", ctx.Coop.Name);
                        await Task.Delay(new Random().Next(500), ctx.Cancellation);
                    }
                }
            }

            ctx.Timings.Set(8);
        }

        private async Task BuildStatusEmbed(CoopProcessingContext ctx) {
            if(ctx.LastMessage != "")
                ctx.Msgs.AddRange(DiscordMessageSplitter.SplitMessage(ctx.LastMessage, "\n"));

            var gradeMessage = $"**Co-op Grade**: {PlayerGradeDetails.GetEmoji((Contract.Types.PlayerGrade)(int)ctx.Coop.League)}{(ctx.Coop.AnyLeague ? " (<:ultra:1131045418319495369> **Any-Grade**)" : "")}";

            var highestEB = ctx.CoopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
            var highestEBMessage = "";
            if(highestEB != null)
                highestEBMessage = $"**\nHighest EB**: {highestEB.DBUser.DiscordUsername} at {highestEB.Backup.EarningsBonus.ToEggString()} {(ctx.UsersNotJoined.Any(x => x?.EggIncId == highestEB.Backup.EggIncId) ? "has not joined yet." : "**has joined!**")}";

            var createdByMessage = "";
            if(!string.IsNullOrEmpty(ctx.Coop.CreatorID) && !EggIncApi.CoopCreatorIds.Any(x => x.EggIncId == ctx.Coop.CreatorID)) {
                var creator = ctx.Users.FirstOrDefault(x => x.Backup?.EggIncId == ctx.Coop.CreatorID);
                if(creator != null) {
                    var account = creator.User.EggIncAccounts.First(x => x.Id == ctx.Coop.CreatorID);
                    createdByMessage += $"\n**Created By**: {creator.User.DiscordUsername} {PlayerGradeDetails.GetEmoji((Contract.Types.PlayerGrade)(int)account.LastGrade)}";
                }
            }

            var publicMessage = ctx.Status.Public ? $"\n**This co-op is public**." : "";

            ctx.EmbedBuilder = new EmbedBuilder()
            .WithDescription($"{gradeMessage}{highestEBMessage}{createdByMessage}{publicMessage}\n" +
            (
                (ctx.Status.Finished()
                ? "\nThis co-op is finished!"
                : ctx.CoopDetails.PercentProjectedForJoined >= 100 && !ctx.Coop.FinishedOrFailed()
                ? "\nThis co-op is projected to succeed without growth as long as there are no sleepers!"
                : "") + $"\n[View on egg9000.com](https://egg9000.com/coop/{ctx.Coop.ContractID}/{ctx.Coop.Name})"
            ))
            .WithColor(ctx.EmbedColor)
            .WithTimestamp(DateTimeOffset.UtcNow)
            ;

            ctx.EmbedBuilder.WithAuthor(new EmbedAuthorBuilder().WithName($"{ctx.Coop.Contract.Name} - Coop Code: {ctx.Coop.Name}").WithIconUrl(EggIncStatics.GetEggByContract(ctx.Coop.Contract, ctx.CustomEggs).image));

            var updates = UpdateInterval.TotalMinutes;
            if(ctx.FinalChannelUpdate) {
                ctx.EmbedBuilder.WithFooter($"Final Update");
            } else {
                ctx.EmbedBuilder.WithFooter($"Updates Every {updates} Minute{(updates > 1 ? "s" : "")} - Last Updated");
            }

            var ends = DiscordHelpers.TimeStamper(TimeSpan.FromSeconds(ctx.Status.SecondsRemaining));
            if(ctx.Status.SecondsRemaining <= 0) {
                ends = $"Expired {ends}";
                if(!ctx.Coop.PseudoExpired) ctx.Coop.PseudoExpired = true;
            }

            for(var i = 0; i < 3; i++) {
                if(ctx.Coop.Contract.Details.GetGoals(ctx.League).Count > i) {
                    var goal = ctx.Coop.Contract.Details.GetGoals(ctx.League)[i];
                    var title = $"Goal {i + 1} ";
                    var time = "";
                    var goalRemaingAmount = goal.TargetAmount - ctx.AmountWithOffline;
                    var goalRemaingTime = goalRemaingAmount / ctx.TotalRate;
                    time = $"\nTime: {GetTimeRemaining(goal.TargetAmount, ctx.TotalRate, ctx.AmountWithOffline)}";
                    if(ctx.Status.TotalAmount > goal.TargetAmount) {
                        title += "✅";
                        time = "";
                    } else if(ctx.Coop.Status == CoopStatus.Failed) {
                        title += "❌";
                        time = "";
                    } else if(ctx.CoopDetails.PercentProjectedForJoined > goal.TargetAmount) {
                        title += "☑";
                    }
                    ctx.EmbedBuilder.AddField(title, $"Target: {goal.TargetAmount.ToEggString()}\nReward: {EggIncStatics.GetReward(goal)}{time}", true);
                } else {
                    ctx.EmbedBuilder.AddField("\u17B5", "\u17B5", true);
                }
            }

            //Estimate the time the coop is projected to finish
            try {
                ctx.Coop.ProjectedFinish = DateTimeOffset.UtcNow.AddSeconds(Math.Min(TimeSpan.FromDays(365).TotalSeconds, GetTimeRemainingValue(ctx.TargetAmount, ctx.TotalRate, ctx.AmountWithOffline).TotalSeconds));
            } catch(ArgumentOutOfRangeException) {
                ctx.Coop.ProjectedFinish = DateTimeOffset.UtcNow.AddYears(1);
            }

            var totalRatePerHour = ctx.TotalRate * 60 * 60;
            if(ctx.Coop.Status != CoopStatus.Completed && ctx.Coop.Status != CoopStatus.Failed) {
                ctx.EmbedBuilder.AddField("Co-op Expires", ends, inline: true);

                if(ctx.RemainingAmount > 0) {
                    var remainingTime = ctx.RemainingAmount / ctx.TotalRate;
                    if(remainingTime < TimeSpan.MaxValue.TotalSeconds) {
                        try {
                            ctx.EmbedBuilder.AddField("Time To Complete", GetTimeRemaining(ctx.TargetAmount, ctx.TotalRate, ctx.AmountWithOffline), inline: true);
                            if(ctx.Status.SecondsRemaining > remainingTime) {
                                ctx.EmbedBuilder.AddField("Ahead By", TimeSpan.FromSeconds(ctx.Status.SecondsRemaining - remainingTime).Humanize(2).ShortenTime(), inline: true);
                            } else {
                                ctx.EmbedBuilder.AddField("Behind By", TimeSpan.FromSeconds(ctx.Status.SecondsRemaining - remainingTime).Humanize(2).ShortenTime(), inline: true);
                            }
                        } catch(OverflowException) {

                        }
                    } else {
                        ctx.EmbedBuilder.AddField("Time To Complete", "**\u221E**", inline: true);
                        ctx.EmbedBuilder.AddField("\u17B5", "\u17B5");
                    }
                } else if(!ctx.Status.Finished()) {
                    await CheckCompleteOnCheckIn(ctx.Coop, ctx.UsersWithStatus, ctx.CoopThread, ctx.Db);
                    ctx.EmbedBuilder.AddField("Time To Complete", "Once everyone checks in", inline: true);
                }

                ctx.EmbedBuilder.AddField("Projected Amount", $"{ctx.CoopDetails.Projected.ToEggString()} of {ctx.TargetAmount.ToEggString()} {Math.Round(ctx.CoopDetails.PercentProjectedForJoined)}%", inline: true);
                ctx.EmbedBuilder.AddField("Current Amount", ctx.Status.TotalAmount.ToEggString(), inline: true);
                ctx.EmbedBuilder.AddField("Current With Offline", ctx.AmountWithOffline.ToEggString(), inline: true);
            } else if(ctx.Coop.Status == CoopStatus.Completed) {
                ctx.EmbedBuilder.AddField("Final Amount", ctx.Status.TotalAmount.ToEggString(), inline: true);
                ctx.EmbedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
            } else if(ctx.Coop.Status == CoopStatus.Failed) {
                ctx.EmbedBuilder.AddField("Final Amount", ctx.Status.TotalAmount.ToEggString(), inline: true);
                ctx.EmbedBuilder.AddField("Final Rate", totalRatePerHour.ToEggString() + "/h", inline: true);
            }
        }
    }
}