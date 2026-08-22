using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace EGG9000.Common.Database.Entities {
    [Index(nameof(Status))]
    [Index(nameof(ThreadArchived), nameof(CoopEnds), nameof(ThreadID))]
    [Index(nameof(ThreadID), nameof(Created))]
    [Index(nameof(ThreadID))]
    public class Coop {
        // CreatorID sentinel for DEV-seeded fake coops (TestSuiteCommands). A global query
        // filter (ApplicationDbContext.OnModelCreating) excludes coops with this CreatorID
        // from every Coop query by default, so synthetic data never triggers real Egg Inc
        // API calls, thread creation, or status polling.
        public const string TestSeedCreatorId = "TESTSEED";

        public Guid Id { get; set; }
        public string ContractID { get; set; }
        public string Name { get; set; }

        public int? CurrentUsers { get; set; }
        public int? MaxUsers { get; set; }
        public int JoinUsers { get; set; }

        public DateTimeOffset? CoopEnds { get; set; }
        public DateTimeOffset? CoopCompleted { get; set; }
        public DateTimeOffset? ProjectedFinish { get; set; } = DateTimeOffset.MaxValue;

        public DateTimeOffset Created { get; set; }

        public bool ProjectedToFinish { get; set; }
        public bool Finished { get; set; }
        public uint League { get; set; }
        public bool AnyLeague { get; set; }
        public bool SuccessfullyStarted { get; set; }

        public ulong GuildId { get; set; }
        public ulong OverflowGuildId { get; set; }
        public string UpdateMessagesId { get; set; }

        public string CreatorID { get; set; }
        public DateTimeOffset? LastUpdateToChannel { get; set; }
        public DateTimeOffset? WarningForDeleteChannel { get; set; }
        public ulong Group { get; set; }
        public bool AddedFromBackup { get; set; } = false;

        public ulong ThreadID { get; set; }
        public ulong ThreadParentChannel { get; set; }
        public bool ThreadArchived { get; set; } = false;
        public bool RolesAddedToThread { get; set; } = false;


        public CoopStatus Status { get; set; }
        public bool PseudoExpired { get; set; } = false;

        public DBContract Contract { get; set; }
        public List<UserCoopXref> UserCoopsXrefs { get; set; }

        public byte[] _StatusCompressed { get; set; }

        [NotMapped]
        private readonly CodecBlobAccessor<Ei.ContractCoopStatusResponse> _status = new(CoopStatusCodec.Decode, CoopStatusCodec.Encode);

        [NotMapped]
        public Ei.ContractCoopStatusResponse LastStatusUpdate {
            get => _status.Get(_StatusCompressed);
            set {
                // Only reassign the mapped LOB column when the payload actually changed, so EF Core
                // does not rewrite _StatusCompressed every status cycle. That blob write is the
                // heaviest and most lock-contended write on Coops during contract launches.
                _StatusCompressed = _status.Set(value, _StatusCompressed);
            }
        }

        public bool FinishedOrFailed() {
            return CoopStatusSets.FinishedOrFailed.Contains(Status);
        }

        public bool FinalizedFinishedOrFailed() {
            return CoopStatusSets.FinalizedFinishedOrFailed.Contains(Status);
        }

        public bool FinishedOrFailedOrExpired() {
            return FinishedOrFailed() || CoopEnds < DateTimeOffset.UtcNow;
        }

        public bool IsOpenForAssignment() {
            return CoopStatusSets.OpenForAssignment.Contains(Status);
        }
    }

    public enum CoopStatus {
        ManualWaitingOnCreation = 1,
        WaitingOnCreation = 2,
        WaitingOnThread = 3,
        WaitingOnStarter = 10,
        WaitingOnAssigned = 11,
        AllAssignedJoined = 12,
        Full = 13,
        Completed = 14,
        CompletedAllCheckIn = 15,
        Failed = -1
    }

    public static class CoopStatusSets {
        public static readonly CoopStatus[] FinishedOrFailed = [CoopStatus.Completed, CoopStatus.Failed, CoopStatus.CompletedAllCheckIn];
        public static readonly CoopStatus[] FinalizedFinishedOrFailed = [CoopStatus.CompletedAllCheckIn, CoopStatus.Failed];
        public static readonly CoopStatus[] OpenForAssignment = [CoopStatus.WaitingOnThread, CoopStatus.WaitingOnStarter, CoopStatus.WaitingOnAssigned, CoopStatus.AllAssignedJoined];
    }
}
