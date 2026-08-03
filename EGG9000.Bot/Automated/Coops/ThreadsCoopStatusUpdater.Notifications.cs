using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Ei;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        public static async Task HandlePingOnFull(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, IThreadChannel coopChannel, IDiscordQueue queue) {
            var notifiedDiscordIds = new HashSet<ulong>();
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnFull ?? false)) {
                userStatus.Xref.CoopSetting.PingOnFull = false;
                userStatus.Xref.UpdateCoopSetting();
                await db.SaveChangesAsyncRetry(cancellationToken: System.Threading.CancellationToken.None);

                if(userStatus.DiscordUser is null || !notifiedDiscordIds.Add(userStatus.DiscordUser.Id)) continue;

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"All users have joined the co-op {coopChannel.Mention}", db);
                if(dmResult != DMResult.Success) {
                    var capturedUser = userStatus.DiscordUser;
                    queue.EnqueueLow(() => coopChannel.SendMessageAsync($"{capturedUser.Mention} All users have joined the co-op {coopChannel.Mention} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}"));
                }
            }
        }
        public static async Task HandlePingOnCheckedIn(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, IThreadChannel coopChannel, IDiscordQueue queue) {
            var notifiedDiscordIds = new HashSet<ulong>();
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnEveryoneCheckedIn ?? false)) {
                userStatus.Xref.CoopSetting.PingOnEveryoneCheckedIn = false;
                userStatus.Xref.UpdateCoopSetting();
                await db.SaveChangesAsyncRetry(cancellationToken: System.Threading.CancellationToken.None);

                if(userStatus.DiscordUser is null || !notifiedDiscordIds.Add(userStatus.DiscordUser.Id)) continue;

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"The co-op {coopChannel.Mention} has finished and you are able to exit the co-op.", db);
                if(dmResult != DMResult.Success) {
                    var capturedUser = userStatus.DiscordUser;
                    queue.EnqueueLow(() => coopChannel.SendMessageAsync($"{capturedUser.Mention} The co-op {coopChannel.Mention} has finished and everyone is checked in. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}"));
                }
            }
        }

        public static async Task HandleFinished(ApplicationDbContext db, List<UserFarmDetails> userFarmDetails, IThreadChannel coopChannel, IDiscordQueue queue) {
            var notifiedDiscordIds = new HashSet<ulong>();
            foreach(var userStatus in userFarmDetails.Where(x => x.Xref?.CoopSetting?.PingOnFinished ?? false)) {
                userStatus.Xref.CoopSetting.PingOnFinished = false;
                userStatus.Xref.UpdateCoopSetting();
                await db.SaveChangesAsyncRetry(cancellationToken: System.Threading.CancellationToken.None);
                if(userStatus.DiscordUser is null || !notifiedDiscordIds.Add(userStatus.DiscordUser.Id)) continue;

                var dmResult = await BoolSendDm(userStatus.DiscordUser, $"The co-op {coopChannel.Mention} has finished.", db);
                if(dmResult != DMResult.Success) {
                    var capturedUser = userStatus.DiscordUser;
                    queue.EnqueueLow(() => coopChannel.SendMessageAsync($"{capturedUser.Mention} The co-op {coopChannel.Mention} has finished. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}"));
                }
            }
        }

        public async Task CheckHighestEBJoined(Coop coop, List<UserWithStatus> usersWithStatus, CoopDetails coopDetails, IThreadChannel coopChannel, ApplicationDbContext _db, List<UserFarmDetails> usersNotJoined) {
            if(usersWithStatus.Any(x => x.Xref?.CoopSetting?.PingOnHighestEB ?? false)) {
                var highestEB2 = coopDetails.CoopParticipants.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).FirstOrDefault();
                if(highestEB2 != null && !usersNotJoined.Any(x => x?.EggIncId == highestEB2.Backup?.EggIncId)) {
                    var notifiedDiscordIds = new HashSet<ulong>();
                    foreach(var user in usersWithStatus.Where(x => x.Xref?.CoopSetting?.PingOnHighestEB ?? false)) {
                        if(highestEB2.DBUser != null && user.User?.DiscordId == highestEB2.DBUser.DiscordId) continue;
                        user.Xref.CoopSetting.PingOnHighestEB = false;
                        user.Xref.UpdateCoopSetting();
                        await _db.SaveChangesAsyncRetry(cancellationToken: System.Threading.CancellationToken.None, logger: _logger);
                        if(user.DiscordUser is null || !notifiedDiscordIds.Add(user.DiscordUser.Id)) continue;
                        await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Highest EB ({highestEB2.DiscordUser?.GetCleanName()} at {highestEB2.Backup.EarningsBonus.ToEggString()}) has joined", coop);
                    }
                }
            }
        }

        public async Task CheckCompleteOnCheckIn(Coop coop, List<UserWithStatus> usersWithStatus, IThreadChannel coopChannel, ApplicationDbContext _db) {
            var anybodyWithPingSetting = usersWithStatus.Where(x => x.Xref?.CoopSetting?.PingOnCompleteOnCheckIn ?? false);

            if(anybodyWithPingSetting.Any()) {
                var notifiedDiscordIds = new HashSet<ulong>();
                foreach(var user in anybodyWithPingSetting) {
                    user.Xref.CoopSetting.PingOnCompleteOnCheckIn = false;
                    user.Xref.UpdateCoopSetting();
                    await _db.SaveChangesAsyncRetry(cancellationToken: System.Threading.CancellationToken.None, logger: _logger);
                    if(user.DiscordUser is null || !notifiedDiscordIds.Add(user.DiscordUser.Id)) continue;
                    await SendDMWarning(_db, user.DiscordUser, coopChannel, $"Your co-op will complete once everyone checks in.", coop);
                }
            }
        }

        public async Task CheckDeflectorChange(ContractCoopStatusResponse prevStatus, ContractCoopStatusResponse newStatus, Coop coop, List<UserWithStatus> usersWithStatus, IThreadChannel coopChannel, ApplicationDbContext _db) {
            if(prevStatus == null || coop.FinishedOrFailed() || coop.CoopEnds < DateTimeOffset.UtcNow) {
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

        private static decimal GetTachyonAmount(IEnumerable<ContractCoopStatusResponse.Types.ContributionInfo> contributions, string currentUserUuid) {
            var matches = contributions.Where(x => x.Uuid != currentUserUuid && x.BuffHistory.Count > 0);
            var histories = matches.Select(x => x.BuffHistory.Last());
            return histories.Sum(x => (decimal)x.EggLayingRate - 1);
        }
    }
}
