using EGG9000.Common.Helpers;
using EGG9000.Common.Proto;
using Google.Protobuf.Collections;
using MessagePack;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using static Ei.Contract.Types;
using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class CustomBackup {
        //public bool Unchanged { get; set; }
        [Key(0)]
        public List<CustomFarm> Farms { get; set; }
        [Key(1)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public string EggIncId {
            get { return EiBackup is { } p ? p.GetID() : field ?? string.Empty; }
            set;
        }
        [Key(2)]
        public string UserName {
            get { return EiBackup is { } p && !string.IsNullOrEmpty(p.UserName) ? p.UserName : field; }
            set;
        }
        //[Key(3)]
        //public double EarningsBonus { get; set; }
        [Key(4)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public long LastBackupTime {
            get { return EiBackup?.Settings is { } s ? (long)s.LastBackupTime : field; }
            set;
        }
        public DateTimeOffset GetLastBackupDateTime() { return DateTimeOffset.FromUnixTimeSeconds(LastBackupTime); }

        // Grade of the most recently accepted contract, with when it was accepted. last_cpi is no
        // longer in backups, so this is how we read a player's current grade. The accept time lets
        // callers ignore it when a known promotion is newer than any contract.
        public (PlayerGrade Grade, DateTimeOffset Accepted) GetMostRecentContractGrade() {
            var graded = new List<(double time, PlayerGrade grade)>();
            if(Farms is not null)
                graded.AddRange(Farms.Where(x => x.Grade != PlayerGrade.GradeUnset).Select(x => ((double)x.TimeAccepted, x.Grade)));
            if(ArchivedFarms is not null)
                graded.AddRange(ArchivedFarms.Where(x => x.Grade != PlayerGrade.GradeUnset).Select(x => ((double)x.TimeAccepted, x.Grade)));
            var (time, grade) = graded.OrderByDescending(x => x.time).FirstOrDefault();
            if(grade == PlayerGrade.GradeUnset)
                return (PlayerGrade.GradeUnset, DateTimeOffset.MinValue);
            return (grade, DateTimeOffset.FromUnixTimeSeconds((long)time));
        }
        [Key(5)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public List<CustomResearch> EpicResearch {
            get { return EiBackup?.Game is { } g ? _epicResearch ??= [.. g.EpicResearch.Select(x => new CustomResearch(x))] : field; }
            set {
                field = value;
                _epicResearch = null;
            }
        }
        private List<CustomResearch> _epicResearch;
        [Key(6)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ushort PermitLevel {
            get { return EiBackup?.Game is { } g ? (ushort)g.PermitLevel : field; }
            set;
        }
        [Key(7)]
        public DateTime CacheAdded { get; set; }
        [Key(8)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ushort EggsOfProphecy {
            get { return EiBackup?.Game is { } g ? (ushort)g.EggsOfProphecy : field; }
            set;
        }
        [Key(9)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public double SoulEggs {
            get { return EiBackup?.Game is { } g ? g.SoulEggsTotal : field; }
            set;
        }
        [Key(10)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public double CurrentMultiplier {
            get { return EiBackup?.Game is { } g ? g.CurrentMultiplier : field; }
            set;
        }
        //[Key(11)]
        //public List<string> CompleteContracts { get; set; }
        [Key(12)]
        public bool EmptyBackup { get; set; }
        [Key(13)]
        public List<CustomArchivedFarms> ArchivedFarms { get; set; }
        [Key(14)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong NumPrestiges {
            get { return EiBackup?.Stats is { } s ? s.NumPrestiges : field; }
            set;
        }
        [Key(15)]
        public List<SpaceMission> SpaceMissions { get; set; }

        [Key(16)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public uint NumDailyGiftsCollected {
            get { return EiBackup?.Game is { } g ? g.NumDailyGiftsCollected : field; }
            set;
        }

        [IgnoreMember]
        public uint PEFromDailyGifts { get { return Math.Min(24, NumDailyGiftsCollected / 28); } }

        [Key(17)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public List<uint> EggMedalLevel {
            get { return EiBackup?.Game is { } g ? _eggMedalLevel ??= [.. g.EggMedalLevel] : field; }
            set {
                field = value;
                _eggMedalLevel = null;
            }
        }
        private List<uint> _eggMedalLevel;

        [Key(18)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong GoldenEggsEarned {
            get { return EiBackup?.Game is { } g ? g.GoldenEggsEarned : field; }
            set;
        }
        [Key(19)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong GoldenEggsSpent {
            get { return EiBackup?.Game is { } g ? g.GoldenEggsSpent : field; }
            set;
        }
        [Key(20)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong PiggyBank {
            get { return EiBackup?.Game is { } g ? g.PiggyBank : field; }
            set;
        }
        [Key(21)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong DroneTakedowns {
            get { return EiBackup?.Stats is { } s ? s.DroneTakedowns : field; }
            set;
        }
        [Key(22)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong DroneTakedownsElite {
            get { return EiBackup?.Stats is { } s ? s.DroneTakedownsElite : field; }
            set;
        }
        [Key(23)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public ulong NumPiggyBreaks {
            get { return EiBackup?.Stats is { } s ? s.NumPiggyBreaks : field; }
            set;
        }
        [Key(24)]
        public List<ArtifactCount> ArtifactHall { get; set; }
        [Key(25)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public bool HyperloopPurchased {
            get { return EiBackup?.Game is { } g ? g.HyperloopStation : field; }
            set;
        }
        [Key(26)]
        public uint TankLevel { get; set; }
        // Retired - see LastContractPlayerInfoBytes
        //[Key(27)]
        //public PlayerGrade Grade { get; set; }
        [Key(28)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public byte ClientVersion {
            get { return EiBackup is { } p ? (byte)p.Version : field; }
            set;
        }
        [Key(29)]
        public Dictionary<Ei.Egg, double> FuelAmounts { get; set; }
        // Retired - see LastContractPlayerInfoBytes
        //[Key(30)]
        //public double GradeProgress { get; set; }
        [Key(31)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public Ei.Egg MaxEggReached {
            get { return EiBackup?.Game is { } g ? g.MaxEggReached : field; }
            set;
        }
        [Key(32)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public Dictionary<Ei.Egg, ulong> MaxFarmSizeReached {
            get { return EiBackup?.Game is { } g ? _maxFarmSizeReached ??= BackupProjections.BuildMaxFarmSizeReached(g) : field; }
            set {
                field = value;
                _maxFarmSizeReached = null;
            }
        }
        private Dictionary<Ei.Egg, ulong> _maxFarmSizeReached;

        [Key(33)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public bool HasDeviceId {
            get { return EiBackup is { } p ? p.HasDeviceId : field; }
            set;
        } = false;
        [Key(34)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public string DeviceId {
            get { return EiBackup is { HasDeviceId: true } p ? p.DeviceId : field ?? string.Empty; }
            set;
        } = string.Empty;
        [Key(36)]
        public List<(Spaceship ship, DurationType type, int count)> ShipsSent { get; set; }
        [Key(37)]
        public double SeasonCS { get; set; } = 0;
        [Key(38)]
        public double TotalCS { get; set; } = 0;
        [Key(39)]
        public List<List<EggIncArtifactInstance>> ArtifactSets { get; set; } = [];
        [Key(40)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public double CraftingXP {
            get { return EiBackup?.Artifacts is { } a ? a.CraftingXp : field; }
            set;
        } = 0;
        [Key(41)]
        public SpaceMission FuelingMission { get; set; }
        [Key(42)]
        public Dictionary<string, ulong> CustomEggMaxFarmSizeReached { get; set; } = [];

        //[Key(43)]
        //public uint EoV { get; set; } = 0;

        [Key(44)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public double[] VirtueEggsDelivered {
            get { return EiBackup?.Virtue is { } v ? _virtueEggsDelivered ??= [.. v.EggsDelivered] : field ?? []; }
            set {
                field = value;
                _virtueEggsDelivered = null;
            }
        } = [];
        private double[] _virtueEggsDelivered;
        [Key(45)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public uint Resets {
            get { return EiBackup?.Virtue is { } v ? v.Resets : field; }
            set;
        }
        [Key(46)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public uint ShiftCount {
            get { return EiBackup?.Virtue is { } v ? v.ShiftCount : field; }
            set;
        }
        [Key(47)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public uint[] EovEarned {
            get { return EiBackup?.Virtue is { } v ? _eovEarned ??= [.. v.EovEarned] : field ?? []; }
            set {
                field = value;
                _eovEarned = null;
            }
        } = [];
        private uint[] _eovEarned;
        [Key(48)]
        public double SubscriptionEnds { get; set; } = 0;
        [Key(49)]
        public Ei.UserSubscriptionInfo.Types.Level? SubscriptionLevel { get; set; } = null;
        [Key(50)]
        [DerivedSlot(nameof(EiBackupBytes))]
        public bool NoAliasInLatestBackup {
            get { return EiBackup is { } p ? string.IsNullOrEmpty(p.UserName) : field; }
            set;
        }
        [Key(51)]
        public byte[] LastContractPlayerInfoBytes {
            get;
            set {
                field = value;
                _lastContractPlayerInfo = null;
            }
        }

        [IgnoreMember]
        [JsonIgnore]
        [XmlIgnore]
        [IgnoreDataMember]
        public Ei.ContractPlayerInfo LastContractPlayerInfo {
            get {
                if(_lastContractPlayerInfo is null && LastContractPlayerInfoBytes is { Length: > 0 })
                    _lastContractPlayerInfo = Ei.ContractPlayerInfo.Parser.ParseFrom(LastContractPlayerInfoBytes);
                return _lastContractPlayerInfo;
            }
        }
        private Ei.ContractPlayerInfo _lastContractPlayerInfo;

        [Key(52)]
        [JsonIgnore]
        [XmlIgnore]
        public byte[] EiBackupBytes {
            get;
            set {
                field = value;
                _eiBackup = null;
                _epicResearch = null;
                _eggMedalLevel = null;
                _maxFarmSizeReached = null;
                _virtueEggsDelivered = null;
                _eovEarned = null;
            }
        }

        [IgnoreMember]
        [JsonIgnore]
        [XmlIgnore]
        [IgnoreDataMember]
        public Ei.Backup EiBackup {
            get {
                if(_eiBackup is null && EiBackupBytes is { Length: > 0 })
                    _eiBackup = Ei.Backup.Parser.ParseFrom(EiBackupBytes);
                return _eiBackup;
            }
        }
        private Ei.Backup _eiBackup;

        [IgnoreMember]
        public double GradeProgress { get { return LastContractPlayerInfo?.GradeProgress ?? 0; } }
        [IgnoreMember]
        public double GradeScore { get { return LastContractPlayerInfo?.GradeScore ?? 0; } }
        [IgnoreMember]
        public double TargetGradeScore { get { return LastContractPlayerInfo?.TargetGradeScore ?? 0; } }
        [IgnoreMember]
        public double SoulPower { get { return LastContractPlayerInfo?.SoulPower ?? 0; } }
        [IgnoreMember]
        public double TargetSoulPower { get { return LastContractPlayerInfo?.TargetSoulPower ?? 0; } }
        [IgnoreMember]
        public double IssueScore { get { return LastContractPlayerInfo?.IssueScore ?? 0; } }
        [IgnoreMember]
        public IReadOnlyList<Ei.ContractEvaluation.Types.PoorBehavior> Issues { get { return LastContractPlayerInfo?.Issues ?? []; } }
        [IgnoreMember]
        public double LastEvaluationTime { get { return LastContractPlayerInfo?.LastEvaluationTime ?? 0; } }
        [IgnoreMember]
        public string LastEvaluationVersion { get { return LastContractPlayerInfo?.LastEvaluationVersion ?? ""; } }
        [IgnoreMember]
        public string AggregationNotes { get { return LastContractPlayerInfo?.AggregationNotes ?? ""; } }

        [IgnoreMember]
        public uint EggsOfTruth { get { return (uint)EovEarned.Sum(x => x); } }

        [IgnoreMember]
        public int EggsOfTruthTotal { get { return VirtueEggsDelivered.Sum(VirtueHelper.CurrentLevel); } }

        [IgnoreMember]
        public ulong TotalGEInPiggyBank { get { return AccountFormulas.TotalGeInPiggyBank(PiggyBank, NumPiggyBreaks); } }

        [IgnoreMember]
        public int PEFromTrophies { get { return AccountFormulas.PeFromTrophies(EggMedalLevel); } }

        public List<ArtifactCount> GetAvailableArtifacts() {
            if(ArtifactHall is null || ArtifactHall.Count == 0) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();
            foreach(var farm in Farms?.Where(x => !x.isVirtueEgg) ?? [])
                DecrementEquipped(artifacts, farm);
            return [.. artifacts.Where(x => x.Count > 0)];
        }

        public List<ArtifactCount> GetAvailableArtifacts(CustomFarm farm) {
            if(ArtifactHall is null || ArtifactHall.Count == 0 || farm.isVirtueEgg) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();
            foreach(var other in Farms.Where(x => x != farm && x.FarmType != Ei.FarmType.Empty && x.CoopSimulationEndTime == 0))
                DecrementEquipped(artifacts, other);
            return [.. artifacts.Where(x => x.Count > 0)];
        }

        private static void DecrementEquipped(List<ArtifactCount> artifacts, CustomFarm farm) {
            foreach(var equipped in farm.Artifacts ?? []) {
                var artifact = artifacts.FirstOrDefault(x => x.Artifact.Equals(equipped));
                if(artifact is not null) artifact.Count--;
            }
        }

        // CS is sourced out-of-band (get_contract_player_info), so the protobuf rebuild has no fresh
        // value. Keep the prior value unless a positive fresh one is supplied. -1 is the legacy
        // "unknown" sentinel and counts as no value.
        public static double CarryForwardCs(double fresh, double last) { return fresh > 0 ? fresh : last; }

        public CustomBackup() { }

        public CustomBackup(Ei.Backup backup, FrozenSet<Ei.Contract> contracts, CustomBackup lastBackup = null) {
            if(backup?.Game == null) {
                EmptyBackup = true;
                return;
            }
            EiBackupBytes = StorageTrimmer.TrimmedBytes(backup);
            UserName = string.IsNullOrEmpty(backup.UserName) ? lastBackup?.UserName ?? "" : backup.UserName;
            var activeTankArtifacts = BackupProjections.ResolveActiveTankArtifacts(backup);
            TankLevel = activeTankArtifacts.TankLevel;

            // CS is written out-of-band by AccountRefresh.ApplyExtrasAsync (from get_contract_player_info),
            // not derived from this protobuf backup. Carry the last known value forward so a mass-backup
            // rebuild doesn't reset it to 0 and drop the user from CSLeaderboard's "TotalCS > 0" filter.
            TotalCS = CarryForwardCs(0, lastBackup?.TotalCS ?? 0);
            SeasonCS = CarryForwardCs(0, lastBackup?.SeasonCS ?? 0);
            LastContractPlayerInfoBytes = lastBackup?.LastContractPlayerInfoBytes;

            SetSubscriptionInfo(backup);

            ArchivedFarms = [];
            AddContracts(backup.Contracts.Contracts, contracts);
            AddContracts(backup.Contracts.Archive, contracts);

            Farms = [];
            foreach(var farm in backup.Farms.Where(x => x.FarmType != Ei.FarmType.Empty)) {
                AddFarm(farm, backup);
            }

            SpaceMissions = backup.ArtifactsDb?.MissionInfos?.Select(BackupProjections.ToSpaceMission).ToList();

            if(backup.ArtifactsDb?.FuelingMission is { } fuelingMission)
                FuelingMission = BackupProjections.ToSpaceMission(fuelingMission);

            FuelAmounts = BackupProjections.BuildFuelAmounts(activeTankArtifacts);

            CustomEggMaxFarmSizeReached = [];
            BackupProjections.MergeMaxFarmSizes(CustomEggMaxFarmSizeReached, backup.Contracts.Archive.Concat(backup.Contracts.Contracts), contracts);
            if(lastBackup?.CustomEggMaxFarmSizeReached is not null)
                BackupProjections.MergeMaxFarmSizes(CustomEggMaxFarmSizeReached, lastBackup.CustomEggMaxFarmSizeReached);

            if(backup.ArtifactsDb is not null) {
                ShipsSent = BackupProjections.BuildShipsSent(backup.ArtifactsDb);
            }

            ArtifactHall = BackupProjections.BuildArtifactHall(backup.ArtifactsDb);

            ArtifactSets = BackupProjections.BuildArtifactSets(backup.ArtifactsDb);
        }

        private void SetSubscriptionInfo(Ei.Backup backup) {
            var subInfo = backup.SubInfo;
            if(subInfo is null) return;

            var hasActiveStatus = subInfo.HasStatus && (subInfo.Status == Ei.UserSubscriptionInfo.Types.Status.Active || subInfo.Status == Ei.UserSubscriptionInfo.Types.Status.GracePeriod);
            var inSubPeriod = subInfo.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(!hasActiveStatus || !inSubPeriod) {
                SubscriptionEnds = subInfo.PeriodEnd;
                return;
            }

            SubscriptionLevel = subInfo.SubscriptionLevel;
            SubscriptionEnds = subInfo.PeriodEnd;
        }

        private void AddFarm(Ei.Backup.Types.Simulation farm, Ei.Backup backup) {
            var contract = backup.Contracts.Contracts.FirstOrDefault(x => x.ContractIdentifier == farm.ContractId)
                ?? backup.Contracts.Archive.FirstOrDefault(x => x is not null && x.ContractIdentifier == farm.ContractId);

            var customFarm = new CustomFarm {
                SimulationBytes = StorageTrimmer.TrimmedBytes(farm),
                LocalContractBytes = contract is null ? null : StorageTrimmer.TrimmedBytes(contract),
                Completed = contract?.Contract is not null && contract.NumGoalsAchieved == contract.Contract.GetGoals(contract).Count,
                Vehicles = [.. farm.Vehicles],
                CoopAllowed = contract?.Contract?.CoopAllowed ?? false,
            };

            var currentCoopStatus = backup.Contracts.CurrentCoopStatuses.FirstOrDefault(x => x.ContractIdentifier == farm.ContractId);
            if(currentCoopStatus is not null)
                customFarm.Creator = currentCoopStatus.CreatorId == backup.GetID();

            customFarm.ReportedUUIDs = [.. backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier).SelectMany(x => x.Contributors.Where(y => y.UserId == backup.EiUserId).Select(y => y.Uuid))];

            customFarm.Artifacts = [];
            var farmIndex = backup.Farms.IndexOf(farm);
            if(backup.ArtifactsDb is not null) {
                if(farmIndex == 0 && (int)farm.EggType >= 50 && (int)farm.EggType <= 54) {
                    var activeArtifactSlots = backup.ArtifactsDb.VirtueAfxDb.ActiveArtifacts.Slots;
                    var activeArtifacts = activeArtifactSlots.Select(x => backup.ArtifactsDb.VirtueAfxDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId));
                    customFarm.Artifacts = BackupProjections.ResolveActiveArtifacts(activeArtifacts);
                } else {
                    var activeArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets.Count - 1 < farmIndex ? [] : backup.ArtifactsDb.ActiveArtifactSets[farmIndex].Slots.Where(x => x.Occupied);
                    var activeArtifacts = activeArtifactSlots.Select(x => backup.ArtifactsDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId));
                    customFarm.Artifacts = BackupProjections.ResolveActiveArtifacts(activeArtifacts);
                }
            }

            Farms.Add(customFarm);
        }

        public uint GetColleggtibleLevel(string identifier) {
            CustomEggMaxFarmSizeReached.TryGetValue(identifier.ToLower(), out var farmSize);
            return LevelForFarmSize(farmSize);
        }

        // Level plus the raw max habitat population reached, in one lookup.
        public (uint Level, ulong FarmSize) GetColleggtibleProgress(string identifier) {
            CustomEggMaxFarmSizeReached.TryGetValue(identifier.ToLower(), out var farmSize);
            return (LevelForFarmSize(farmSize), farmSize);
        }

        private static uint LevelForFarmSize(ulong farmSize) {
            return farmSize switch {
                > 10000000000UL => 4,
                > 1000000000UL => 3,
                > 100000000UL => 2,
                > 10000000UL => 1,
                _ => 0
            };
        }

        private void AddContracts(RepeatedField<Ei.LocalContract> contracts, FrozenSet<Ei.Contract> allContracts) {
            foreach(var localContract in contracts) {
                if(localContract.Contract is null) {
                    var contract = allContracts.FirstOrDefault(x => x.Identifier == localContract.ContractIdentifier);
                    if(contract is null) {
                        // Definition is not in our cache/DB (e.g. a contract never offered to the
                        // reference account, absorbed lazily via get_contracts_info). Skip this entry
                        // instead of crashing the whole backup or attaching an unrelated contract.
                        Console.WriteLine($"Missing contract definition, skipping: {localContract.ContractIdentifier}");
                        continue;
                    }
                    localContract.Contract = contract;
                }
                ArchivedFarms.Add(new CustomArchivedFarms(localContract));
            }
        }

        [IgnoreMember]
        public double SoulEggBonus { get { return EpicResearch is null ? 0 : (EpicResearch.FirstOrDefault(x => x.Id == "soul_eggs")?.Level ?? 0d) + 10; } }
        [IgnoreMember]
        public double ProphecyEggBonus { get { return EpicResearch is null ? 0 : ((EpicResearch.FirstOrDefault(x => x.Id == "prophecy_bonus")?.Level ?? 0d) + 5) / 100 + 1; } }
        [IgnoreMember]
        public double EarningsBonus { get { return SoulEggs * SoulEggBonus * Math.Pow(ProphecyEggBonus, EggsOfProphecy) * Math.Pow(1.01, EggsOfTruth); } }

        [IgnoreMember]
        public double MER { get { return Math.Round(AccountFormulas.MerValue(SoulEggs, EggsOfProphecy), 2); } }
    }
}
