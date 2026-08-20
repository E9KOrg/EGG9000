using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData;
using EGG9000.Common.Proto;
using Google.Protobuf.Collections;
using MessagePack;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using static Ei.ArtifactSpec.Types;
using static Ei.Contract.Types;
using static Ei.GameModifier.Types;
using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class CustomBackup {
        //public bool Unchanged { get; set; }
        [Key(0)]
        public List<CustomFarm> Farms { get; set; }
        [Key(1)]
        public string EggIncId {
            get => EiBackup is { } p ? p.GetID() : field;
            set;
        }
        [Key(2)]
        public string UserName {
            get => EiBackup is { } p && !string.IsNullOrEmpty(p.UserName) ? p.UserName : field;
            set;
        }
        //[Key(3)]
        //public double EarningsBonus { get; set; }
        [Key(4)]
        public long LastBackupTime {
            get => EiBackup?.Settings is { } s ? (long)s.LastBackupTime : field;
            set;
        }
        public DateTimeOffset GetLastBackupDateTime() {
            return DateTimeOffset.FromUnixTimeSeconds(LastBackupTime);
        }

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
        public List<CustomResearch> EpicResearch {
            get => EiBackup?.Game is { } g ? _epicResearch ??= [.. g.EpicResearch.Select(x => new CustomResearch(x))] : field;
            set {
                field = value;
                _epicResearch = null;
            }
        }
        private List<CustomResearch> _epicResearch;
        [Key(6)]
        public ushort PermitLevel {
            get => EiBackup?.Game is { } g ? (ushort)g.PermitLevel : field;
            set;
        }
        [Key(8)]
        public ushort EggsOfProphecy {
            get => EiBackup?.Game is { } g ? (ushort)g.EggsOfProphecy : field;
            set;
        }
        [Key(9)]
        public double SoulEggs {
            get => EiBackup?.Game is { } g ? g.SoulEggsTotal : field;
            set;
        }
        [Key(10)]
        public double CurrentMultiplier {
            get => EiBackup?.Game is { } g ? g.CurrentMultiplier : field;
            set;
        }
        //[Key(11)]
        //public List<string> CompleteContracts { get; set; }
        [Key(12)]
        public bool EmptyBackup { get; set; }
        [Key(13)]
        public List<CustomArchivedFarms> ArchivedFarms { get; set; }
        [Key(14)]
        public ulong NumPrestiges {
            get => EiBackup?.Stats is { } s ? s.NumPrestiges : field;
            set;
        }
        [Key(15)]
        public List<SpaceMission> SpaceMissions { get; set; }

        [Key(16)]
        public uint NumDailyGiftsCollected {
            get => EiBackup?.Game is { } g ? g.NumDailyGiftsCollected : field;
            set;
        }

        [IgnoreMember]
        public uint PEFromDailyGifts => Math.Min(24, NumDailyGiftsCollected / 28);

        [Key(17)]
        public List<uint> EggMedalLevel {
            get => EiBackup?.Game is { } g ? _eggMedalLevel ??= [.. g.EggMedalLevel] : field;
            set {
                field = value;
                _eggMedalLevel = null;
            }
        }
        private List<uint> _eggMedalLevel;

        [Key(18)]
        public ulong GoldenEggsEarned {
            get => EiBackup?.Game is { } g ? g.GoldenEggsEarned : field;
            set;
        }
        [Key(19)]
        public ulong GoldenEggsSpent {
            get => EiBackup?.Game is { } g ? g.GoldenEggsSpent : field;
            set;
        }
        [Key(20)]
        public ulong PiggyBank {
            get => EiBackup?.Game is { } g ? g.PiggyBank : field;
            set;
        }
        [Key(21)]
        public ulong DroneTakedowns {
            get => EiBackup?.Stats is { } s ? s.DroneTakedowns : field;
            set;
        }
        [Key(22)]
        public ulong DroneTakedownsElite {
            get => EiBackup?.Stats is { } s ? s.DroneTakedownsElite : field;
            set;
        }
        [Key(23)]
        public ulong NumPiggyBreaks {
            get => EiBackup?.Stats is { } s ? s.NumPiggyBreaks : field;
            set;
        }
        [Key(24)]
        public List<ArtifactCount> ArtifactHall { get; set; }
        [Key(25)]
        public bool HyperloopPurchased {
            get => EiBackup?.Game is { } g ? g.HyperloopStation : field;
            set;
        }
        [Key(26)]
        public uint TankLevel { get; set; }
        // Retired - see LastContractPlayerInfoBytes
        //[Key(27)]
        //public PlayerGrade Grade { get; set; }
        [Key(28)]
        public byte ClientVersion {
            get => EiBackup is { } p ? (byte)p.Version : field;
            set;
        }
        [Key(29)]
        public Dictionary<Ei.Egg, double> FuelAmounts { get; set; }
        // Retired - see LastContractPlayerInfoBytes 
        //[Key(30)]
        //public double GradeProgress { get; set; }
        [Key(31)]
        public Ei.Egg MaxEggReached {
            get => EiBackup?.Game is { } g ? g.MaxEggReached : field;
            set;
        }
        [Key(32)]
        public Dictionary<Ei.Egg, ulong> MaxFarmSizeReached {
            get => EiBackup?.Game is { } g ? _maxFarmSizeReached ??= BackupProjections.BuildMaxFarmSizeReached(g) : field;
            set {
                field = value;
                _maxFarmSizeReached = null;
            }
        }
        private Dictionary<Ei.Egg, ulong> _maxFarmSizeReached;

        [Key(33)]
        public bool HasDeviceId {
            get => EiBackup is { } p ? p.HasDeviceId : field;
            set;
        } = false;
        [Key(34)]
        public string DeviceId {
            get => EiBackup is { HasDeviceId: true } p ? p.DeviceId : field;
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
        public double CraftingXP {
            get => EiBackup?.Artifacts is { } a ? a.CraftingXp : field;
            set;
        } = 0;
        [Key(41)]
        public SpaceMission FuelingMission { get; set; }
        [Key(42)]
        public Dictionary<string, ulong> CustomEggMaxFarmSizeReached { get; set; } = [];


        //[Key(43)]
        //public uint EoV { get; set; } = 0;

        [Key(44)]
        public double[] VirtueEggsDelivered {
            get => EiBackup?.Virtue is { } v ? _virtueEggsDelivered ??= [.. v.EggsDelivered] : field;
            set {
                field = value;
                _virtueEggsDelivered = null;
            }
        } = [];
        private double[] _virtueEggsDelivered;
        [Key(45)]
        public uint Resets {
            get => EiBackup?.Virtue is { } v ? v.Resets : field;
            set;
        }
        [Key(46)]
        public uint ShiftCount {
            get => EiBackup?.Virtue is { } v ? v.ShiftCount : field;
            set;
        }
        [Key(47)]
        public uint[] EovEarned {
            get => EiBackup?.Virtue is { } v ? _eovEarned ??= [.. v.EovEarned] : field;
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
        public bool NoAliasInLatestBackup {
            get => EiBackup is { } p ? string.IsNullOrEmpty(p.UserName) : field;
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
        public Ei.ContractPlayerInfo LastContractPlayerInfo {
            get {
                if(_lastContractPlayerInfo is null && LastContractPlayerInfoBytes is { Length: > 0 })
                    _lastContractPlayerInfo = Ei.ContractPlayerInfo.Parser.ParseFrom(LastContractPlayerInfoBytes);
                return _lastContractPlayerInfo;
            }
        }
        private Ei.ContractPlayerInfo _lastContractPlayerInfo;

        [Key(52)]
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
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        [System.Runtime.Serialization.IgnoreDataMember]
        public Ei.Backup EiBackup {
            get {
                if(_eiBackup is null && EiBackupBytes is { Length: > 0 })
                    _eiBackup = Ei.Backup.Parser.ParseFrom(EiBackupBytes);
                return _eiBackup;
            }
        }
        private Ei.Backup _eiBackup;

        [IgnoreMember]
        public double GradeProgress => LastContractPlayerInfo?.GradeProgress ?? 0;
        [IgnoreMember]
        public double GradeScore => LastContractPlayerInfo?.GradeScore ?? 0;
        [IgnoreMember]
        public double TargetGradeScore => LastContractPlayerInfo?.TargetGradeScore ?? 0;
        [IgnoreMember]
        public double SoulPower => LastContractPlayerInfo?.SoulPower ?? 0;
        [IgnoreMember]
        public double TargetSoulPower => LastContractPlayerInfo?.TargetSoulPower ?? 0;
        [IgnoreMember]
        public double IssueScore => LastContractPlayerInfo?.IssueScore ?? 0;
        [IgnoreMember]
        public IReadOnlyList<Ei.ContractEvaluation.Types.PoorBehavior> Issues => LastContractPlayerInfo?.Issues ?? [];
        [IgnoreMember]
        public double LastEvaluationTime => LastContractPlayerInfo?.LastEvaluationTime ?? 0;
        [IgnoreMember]
        public string LastEvaluationVersion => LastContractPlayerInfo?.LastEvaluationVersion ?? "";
        [IgnoreMember]
        public string AggregationNotes => LastContractPlayerInfo?.AggregationNotes ?? "";


        [IgnoreMember]
        public uint EggsOfTruth { get { return (uint?)EovEarned?.Sum(x => x) ?? (uint)0; } }

        [IgnoreMember]
        public int EggsOfTruthTotal { get { return VirtueEggsDelivered?.Select(x => VirtueHelper.CurrentLevel(x)).Sum() ?? 0; } }

        [IgnoreMember]
        public ulong TotalGEInPiggyBank => AccountFormulas.TotalGeInPiggyBank(PiggyBank, NumPiggyBreaks);

        [IgnoreMember]
        public int PEFromTrophies => AccountFormulas.PeFromTrophies(EggMedalLevel);

        public List<ArtifactCount> GetAvailableArtifacts() {
            if(ArtifactHall is null || ArtifactHall.Count == 0) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();
            Farms?.Where(x => !x.isVirtueEgg).ToList().ForEach(f => f.Artifacts?.ForEach(a => {
                var artifact = artifacts.FirstOrDefault(x => x.Artifact.Equals(a));
                if(artifact is not null) artifact.Count--;
            }));
            return artifacts?.Where(x => x.Count > 0).ToList() ?? [];
        }

        public List<ArtifactCount> GetAvailableArtifacts(CustomFarm farm) {
            if(ArtifactHall is null || ArtifactHall.Count == 0 || farm.isVirtueEgg) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();
            Farms.Where(x => x != farm && x.FarmType != Ei.FarmType.Empty && x.CoopSimulationEndTime == 0).ToList()?.ForEach(f => f.Artifacts?.ForEach(a => { var artifact = artifacts.FirstOrDefault(x => x.Artifact.Equals(a)); if(artifact is not null) artifact.Count--; }));
            return artifacts?.Where(x => x.Count > 0).ToList() ?? [];
        }

        // CS is sourced out-of-band (get_contract_player_info), so the protobuf rebuild has no fresh
        // value. Keep the prior value unless a positive fresh one is supplied. -1 is the legacy
        // "unknown" sentinel and counts as no value.
        public static double CarryForwardCs(double fresh, double last) => fresh > 0 ? fresh : last;

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

            var fm = backup.ArtifactsDb?.FuelingMission ?? null;
            if(fm != null) {
                FuelingMission = BackupProjections.ToSpaceMission(fm);
            }

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
                ?? backup.Contracts.Archive.Where(x => x != null).FirstOrDefault(x => x.ContractIdentifier == farm.ContractId);

            var customFarm = new CustomFarm {
                SimulationBytes = StorageTrimmer.TrimmedBytes(farm),
                LocalContractBytes = contract is null ? null : StorageTrimmer.TrimmedBytes(contract),
                Completed = contract?.Contract != null && contract.NumGoalsAchieved == contract.Contract.GetGoals(contract).Count,
                Vehicles = [.. farm.Vehicles],
                CoopAllowed = contract?.Contract?.CoopAllowed ?? false,
            };


            var currentCoopStatus = backup.Contracts.CurrentCoopStatuses.FirstOrDefault(x => x.ContractIdentifier == farm.ContractId);
            if(currentCoopStatus != null)
                customFarm.Creator = currentCoopStatus.CreatorId == backup.GetID();

            var uuids = backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier).SelectMany(x => x.Contributors.Where(y => y.UserId == backup.EiUserId).Select(y => y.Uuid)).ToList();

            customFarm.ReportedUUIDs = uuids;

            customFarm.Artifacts = [];
            var farmIndex = backup.Farms.IndexOf(farm);
            if(backup.ArtifactsDb != null) {
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

        private static List<EggIncArtifactInstance> ResolveActiveArtifacts(IEnumerable<Ei.ArtifactInventoryItem> activeArtifacts) {
            return [.. activeArtifacts.Where(x => x != null).Select(x => {
                var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                if(artifact == null)
                    return null;
                artifact.Stones = [.. x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null)];
                return artifact;
            }).Where(x => x != null)];
        }

        private static SpaceMission ToSpaceMission(Ei.MissionInfo m) {
            return new SpaceMission {
                Ship = m.Ship,
                Duration = m.DurationType,
                Status = m.Status,
                DurationSeconds = (long)m.DurationSeconds,
                StartTime = (long)m.StartTimeDerived,
                Fuels = [.. m.Fuel.Select(f => new SpaceMissionFuel {
                    Amount = f.Amount,
                    Egg = f.Egg
                })],
                Targeting = (int)m.Ship >= 4 ? m?.TargetArtifact ?? Name.Unknown : Name.Unknown,
                Capacity = m.Capacity,
                Stars = m.Level
            };
        }

        private static Dictionary<Ei.Egg, double> BuildFuelAmounts(Ei.Backup.Types.Artifacts activeTankArtifacts) {
            var fuelAmounts = new Dictionary<Ei.Egg, double>();
            for(var i = 0; i < activeTankArtifacts.TankFuels.Count; i++) {
                if(activeTankArtifacts.TankFuels[i] > 0)
                    fuelAmounts.Add((Ei.Egg)(i + 1), activeTankArtifacts.TankFuels[i]);
            }
            return fuelAmounts;
        }

        private static List<(Spaceship ship, DurationType type, int count)> BuildShipsSent(Ei.ArtifactsDB artifactsDb) {
            List<(Spaceship ship, DurationType type, int count)> shipsSent = [.. artifactsDb.MissionArchive.Where(x => x.DurationSeconds > 0).GroupBy(x => new { x.Ship, x.DurationType }).Select(x => (x.Key.Ship, x.Key.DurationType, x.Count()))];
            foreach(var ship in artifactsDb.MissionInfos.Where(x => (int)x.Status > 5)) {
                var shipInfo = shipsSent.FirstOrDefault(x => x.ship == ship.Ship && x.type == ship.DurationType);
                if(shipInfo != default) {
                    shipInfo.count++;
                    shipsSent.RemoveAll(x => x.ship == ship.Ship && x.type == ship.DurationType);
                    shipsSent.Add(shipInfo);
                } else {
                    shipsSent.Add((ship.Ship, ship.DurationType, 1));
                }
            }
            return shipsSent;
        }

        private static List<ArtifactCount> BuildArtifactHall(Ei.ArtifactsDB artifactsDb) {
            List<ArtifactCount> artifactHall = [.. artifactsDb.InventoryItems.Select(x => {
                var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                if(artifact is not null) {
                    artifact.Stones = [.. x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null)];
                }
                var artifactStatus = artifactsDb.ArtifactStatus.FirstOrDefault(a =>
                    a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                );
                return new ArtifactCount { Count = (int)x.Quantity, Artifact = artifact, NumberCrafted = artifactStatus?.Count ?? 0 };
            })];

            artifactHall.AddRange(artifactsDb.ArtifactStatus.Where(a =>
                !artifactsDb.InventoryItems.Any(x => a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                )
            ).Select(a => new ArtifactCount { Count = 0, Artifact = EggIncArtifacts.GetArtifact(a.Spec), NumberCrafted = a.Count }));

            return artifactHall;
        }

        private static List<List<EggIncArtifactInstance>> BuildArtifactSets(Ei.ArtifactsDB artifactsDb) {
            var afxSetsProjected = artifactsDb.SavedArtifactSets.Select(s =>
                s.Slots.Select(sl => {
                    var x = artifactsDb.InventoryItems.FirstOrDefault(item => item.ItemId == sl.ItemId);
                    if(x is null) return null;
                    var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                    if(artifact is null) return null;
                    artifact.Stones = [.. x.Artifact.Stones.Select(EggIncArtifacts.GetArtifact).Where(y => y != null)];
                    return artifact;
                })
            );
            return Helpers.AfxSets.AfxSetsBuilder.BuildSetsPreservingEmpty(afxSetsProjected);
        }

        private static Ei.Backup.Types.Artifacts ResolveActiveTankArtifacts(Ei.Backup backup) {
            var currentFarm = backup.Farms.ElementAtOrDefault((int)backup.Game.CurrentFarm);
            var inVirtueDimension = currentFarm is not null && (int)currentFarm.EggType >= 50 && (int)currentFarm.EggType <= 54;
            return inVirtueDimension && backup.Virtue?.Afx is not null ? backup.Virtue.Afx : backup.Artifacts;
        }

        private static void MergeMaxFarmSizes(Dictionary<string, ulong> target, Dictionary<string, ulong> source) {
            foreach(var kvp in source)
                if(!target.TryGetValue(kvp.Key, out var existing) || kvp.Value > existing)
                    target[kvp.Key] = kvp.Value;
        }

        private static void MergeMaxFarmSizes(Dictionary<string, ulong> target, IEnumerable<Ei.LocalContract> farms, FrozenSet<Ei.Contract> contracts) {
            var eggIdByContractId = contracts
                .Where(c => c != null && c.Egg == Ei.Egg.CustomEgg && !string.IsNullOrEmpty(c.CustomEggId))
                .GroupBy(c => c.Identifier)
                .ToDictionary(g => g.Key, g => g.First().CustomEggId.ToLower());
            MergeMaxFarmSizes(target,
                farms.Where(f => f.MaxFarmSizeReached > 0 && eggIdByContractId.ContainsKey(f.ContractIdentifier))
                     .GroupBy(f => eggIdByContractId[f.ContractIdentifier])
                     .ToDictionary(g => g.Key, g => (ulong)g.Max(f => f.MaxFarmSizeReached)));
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

        private static uint LevelForFarmSize(ulong farmSize) => farmSize switch {
            > 10000000000UL => 4,
            > 1000000000UL => 3,
            > 100000000UL => 2,
            > 10000000UL => 1,
            _ => 0
        };

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
        public double SoulEggBonus { get { return EpicResearch is null ? 0 : (double)(EpicResearch.FirstOrDefault(x => x.Id == "soul_eggs")?.Level ?? 0d) + 10; } }
        [IgnoreMember]
        public double ProphecyEggBonus { get { return EpicResearch is null ? 0 : ((double)(EpicResearch.FirstOrDefault(x => x.Id == "prophecy_bonus")?.Level ?? 0d) + 5) / 100 + 1; } }
        [IgnoreMember]
        public double EarningsBonus { get { return SoulEggs * SoulEggBonus * Math.Pow(ProphecyEggBonus, EggsOfProphecy) * (Math.Pow(1.01, EggsOfTruth)); } }

        [IgnoreMember]
        public double MER => Math.Round(AccountFormulas.MerValue(SoulEggs, EggsOfProphecy), 2);
    }

    [MessagePackObject]
    public class CustomResearch {
        [Key(0)]
        public string Id { get; set; }
        [Key(1)]
        public uint Level { get; set; }

        public CustomResearch() { }
        public CustomResearch(Ei.Backup.Types.ResearchItem item) {
            Id = item.Id;
            Level = item.Level;
        }
    }

    public abstract class CustomFarmBase {
        [IgnoreMember]
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        [System.Runtime.Serialization.IgnoreDataMember]
        public Ei.LocalContract LocalContract {
            get {
                if(_localContract is null && LocalContractBytesStorage is { Length: > 0 })
                    _localContract = Ei.LocalContract.Parser.ParseFrom(LocalContractBytesStorage);
                return _localContract;
            }
        }
        private Ei.LocalContract _localContract;

        protected void InvalidateLocalContract() {
            _localContract = null;
        }

        protected abstract byte[] LocalContractBytesStorage { get; }

        protected abstract long TimeAcceptedUnix { get; }

        [IgnoreMember]
        public DateTimeOffset Started => DateTimeOffset.FromUnixTimeSeconds(TimeAcceptedUnix);
    }

    [MessagePackObject]
    public class CustomFarm : CustomFarmBase {
        [Key(0)]
        public Ei.FarmType FarmType {
            get => Simulation is { } s ? s.FarmType : field;
            set;
        }
        [Key(1)]
        public string ContractId {
            get => Simulation is { } s ? s.ContractId : field;
            set;
        }
        [Key(2)]
        public double EggsPaidFor {
            get => Simulation is { } s ? s.EggsPaidFor : field;
            set;
        }
        [Key(3)]
        public uint? League {
            get => LocalContract is { } l ? l.League : field;
            set;
        }
        [Key(4)]
        public string CoopId {
            get => LocalContract is { } l ? l.CoopIdentifier : field;
            set;
        }
        [Key(5)]
        public bool Cancelled {
            get => LocalContract is { } l ? l.Cancelled : field;
            set;
        }
        [Key(6)]
        public bool Completed { get; set; }
        [Key(7)]
        public List<CustomResearch> CommonResearch {
            get => Simulation is { } s ? _commonResearch ??= [.. s.CommonResearch.Select(x => new CustomResearch(x))] : field;
            set {
                field = value;
                _commonResearch = null;
            }
        }
        private List<CustomResearch> _commonResearch;
        [Key(8)]
        public ulong NumChickens {
            get => Simulation is { } s ? s.NumChickens : field;
            set;
        }
        [Key(10)]
        public Ei.Egg EggType {
            get => Simulation is { } s ? s.EggType : field;
            set;
        }
        [Key(11)]
        public List<uint> TrainLength {
            get => Simulation is { } s ? _trainLength ??= [.. s.TrainLength] : field;
            set {
                field = value;
                _trainLength = null;
            }
        }
        private List<uint> _trainLength;
        [Key(12)]
        public List<uint> Vehicles;
        [Key(13)]
        public List<EggIncArtifactInstance> Artifacts { get; set; }
        [Key(14)]
        public uint SilosOwned {
            get => Simulation is { } s ? s.SilosOwned : field;
            set;
        }
        [Key(15)]
        public long TimeAccepted {
            get => LocalContract is { } l ? (long)l.TimeAccepted : field;
            set;
        }
        [Key(16)]
        public bool CoopAllowed { get; set; }
        [Key(17)]
        public long CoopSharedEndTime {
            get => LocalContract is { } l ? (long)l.CoopSharedEndTime : field;
            set;
        }
        [Key(18)]
        public ushort BoostTokensReceived {
            get => Simulation is { } s ? (ushort)s.BoostTokensReceived : field;
            set;
        }
        [Key(19)]
        public ushort BoostTokensGiven {
            get => Simulation is { } s ? (ushort)s.BoostTokensGiven : field;
            set;
        }
        [Key(20)]
        public ushort BoostTokensSpent {
            get => Simulation is { } s ? (ushort)s.BoostTokensSpent : field;
            set;
        }
        [Key(21)]
        public double CashEarned {
            get => Simulation is { } s ? s.CashEarned : field;
            set;
        }
        [Key(22)]
        public double CashSpent {
            get => Simulation is { } s ? s.CashSpent : field;
            set;
        }
        [Key(23)]
        public long TimeCheatDebt {
            get => Simulation is { } s ? (long)s.TimeCheatDebtDEP : field;
            set;
        }
        [Key(24)]
        public ushort BoostsUsed {
            get => LocalContract is { } l ? (ushort)l.BoostsUsed : field;
            set;
        }
        [Key(25)]
        public ushort TimeCheatsDetected {
            get => Simulation is { } s ? (ushort)s.TimeCheatsDetected : field;
            set;
        }
        //[Key(26)]
        //public Double CurrentShippingRate { get; set; }
        //[Key(27)]
        //public Double EggLayingRate { get; set; }
        //[Key(28)]
        //public Double MaxShippingRate { get; set; }
        //[Key(29)]
        //public Double EggValue { get; set; }
        //[Key(30)]
        //public Double Income { get; set; }
        //[Key(31)]
        //public Double MaxRunningBonus { get; set; }
        [Key(32)]
        public List<ushort> Habs {
            get => Simulation is { } s ? _habs ??= [.. s.Habs.Select(x => (ushort)x)] : field;
            set {
                field = value;
                _habs = null;
            }
        }
        private List<ushort> _habs;
        [Key(33)]
        public float LastStepTime {
            get => Simulation is { } s ? (float)s.LastStepTime : field;
            set;
        }
        [Key(34)]
        public List<string> ReportedUUIDs { get; set; }
        [Key(35)]
        public PlayerGrade Grade {
            get => LocalContract is { } l ? l.Grade : field;
            set;
        }
        [Key(36)]
        public double EvaluationCxp {
            get => LocalContract is { } l ? (l.Evaluation is { } e ? (float)e.Cxp : 0.0) : field;
            set;
        }
        [Key(37)]
        public bool ContributionFinalized {
            get => LocalContract is { } l ? l.CoopContributionFinalized : field;
            set;
        }
        [Key(38)]
        public double CoopSimulationEndTime {
            get => LocalContract is { } l ? l.CoopSimulationEndTime : field;
            set;
        }
        [Key(39)]
        public byte NumGoalsAchieved {
            get => LocalContract is { } l ? (byte)l.NumGoalsAchieved : field;
            set;
        }
        [Key(40)]
        public bool Creator { get; set; }
        [Key(41)]
        public byte[] SimulationBytes {
            get;
            set {
                field = value;
                _simulation = null;
                _commonResearch = null;
                _trainLength = null;
                _habs = null;
            }
        }

        [IgnoreMember]
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        [System.Runtime.Serialization.IgnoreDataMember]
        public Ei.Backup.Types.Simulation Simulation {
            get {
                if(_simulation is null && SimulationBytes is { Length: > 0 })
                    _simulation = Ei.Backup.Types.Simulation.Parser.ParseFrom(SimulationBytes);
                return _simulation;
            }
        }
        private Ei.Backup.Types.Simulation _simulation;

        [Key(42)]
        public byte[] LocalContractBytes {
            get;
            set {
                field = value;
                InvalidateLocalContract();
            }
        }

        protected override byte[] LocalContractBytesStorage => LocalContractBytes;

        protected override long TimeAcceptedUnix => TimeAccepted;

        public class Colleggtible {
            public GameDimension Dimension { get; set; }
            public double Value { get; set; }
        }

        [IgnoreMember]
        public bool isVirtueEgg { get { return (int)EggType >= 50 && (int)EggType <= 54; } }

        private double GetEggLayingBuff(Coop coop, double? ignoreBuff) {
            if(coop?.LastStatusUpdate is null)
                return 1.0;
            var eggLayingBuff = coop.LastStatusUpdate.Participants.Where(x => x.BuffHistory.Any())
                .Sum(x => x.BuffHistory.Last().EggLayingRate - 1);
            ignoreBuff ??= (Artifacts.FirstOrDefault(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)?.Value ?? 1) - 1;
            if(ignoreBuff.HasValue)
                eggLayingBuff -= ignoreBuff.Value;
            return eggLayingBuff + 1;
        }

        private static (double eggLayRatePerc, double shipCapPerc) GetLeagueModifierPercentages(Coop coop, DBContract contract) {
            if(coop is null || (coop.Contract is null && contract is null) || coop.League <= 1)
                return (1.0, 1.0);
            var modifiers = (coop.Contract ?? contract).Details.GradeSpecs[(int)coop.League - 1].Modifiers;
            var eggLayRateMod = modifiers.FirstOrDefault(x => x.Dimension == GameDimension.EggLayingRate);
            var shipCapMod = modifiers.FirstOrDefault(x => x.Dimension == GameDimension.ShippingCapacity);
            return (
                eggLayRateMod is not null ? (double)eggLayRateMod.Value : 1.0,
                shipCapMod is not null ? (double)shipCapMod.Value : 1.0
            );
        }

        private CustomFarmStats _stats = null;
        public CustomFarmStats WithStats(CustomBackup backup, Coop coop, List<DBCustomEgg> customEggs, double? ignoreBuff = null, DBContract contract = null) {
            if(_stats == null) {
                var eggLayingBuff = GetEggLayingBuff(coop, ignoreBuff);
                var (eggLayRatePerc, shipCapPerc) = GetLeagueModifierPercentages(coop, contract);

                var eggLayingResearch = Research.GetEggLayingRatePerSec(this, backup.EpicResearch);
                var eggLayingArtifact = EggIncArtifacts.GetEggLayingRateMultiple(this);

                var dimensionColleggtibleEffect = Colleggtibles.GetCollectibleData(customEggs, backup);

                _stats = new CustomFarmStats {
                    MaxShippingRate = Research.GetShippingCapacityPerSec(this, backup.EpicResearch) * EggIncArtifacts.GetShippingMultiple(this) * shipCapPerc * dimensionColleggtibleEffect[GameDimension.ShippingCapacity],
                    EggLayingRate = eggLayingResearch * eggLayingArtifact * eggLayingBuff * eggLayRatePerc * dimensionColleggtibleEffect[GameDimension.EggLayingRate]
                };
                _stats.CurrentShippingRate = Math.Min(_stats.MaxShippingRate, _stats.EggLayingRate);
                _stats.EggValue = Research.GetEggValue(this, backup.EpicResearch, contract, customEggs) * EggIncArtifacts.GetEggValueMutiple(this);
                _stats.Income = _stats.CurrentShippingRate * _stats.EggValue * (backup.EarningsBonus / 100) * backup.CurrentMultiplier * dimensionColleggtibleEffect[GameDimension.Earnings];
                _stats.MaxRunningBonus = Research.MaxRunningBonus(this, backup.EpicResearch) + EggIncArtifacts.GetMaxRunningBonusAdditive(this);
                _stats.HabSpace = Research.GetHabSpace(this, backup.EpicResearch) * Math.Round(EggIncArtifacts.GetHabSpaceMultiple(this), 5) * dimensionColleggtibleEffect[GameDimension.HabCapacity];
                _stats.InternalHatchery = (int)(Research.InternalHatchery(this, backup.EpicResearch) * EggIncArtifacts.GetMultiple(EggIncBoostTypeEnum.InternalHatchery, this) * dimensionColleggtibleEffect[GameDimension.InternalHatcheryRate]);
                if(isVirtueEgg) {
                    _stats.InternalHatchery = (int)((double)_stats.InternalHatchery * Math.Pow(1.1, backup.EggsOfTruth));
                }
            }
            return _stats;
        }
    }

    public class CustomFarmStats {
        public double CurrentShippingRate { get; set; }
        public double EggLayingRate { get; set; }
        public double MaxShippingRate { get; set; }
        public double EggValue { get; set; }
        public double Income { get; set; }
        public double MaxRunningBonus { get; set; }
        public double HabSpace { get; set; }
        public int InternalHatchery { get; set; }
    }

    [MessagePackObject]
    public class CustomArchivedFarms : CustomFarmBase {
        [Key(0)]
        public string CoopId {
            get => LocalContract is { } l ? l.CoopIdentifier : field;
            set;
        }
        [Key(1)]
        public string ContractId {
            get => LocalContract is { } l && l.HasContractIdentifier ? l.ContractIdentifier : field;
            set;
        }
        [Key(2)]
        public float TimeAccepted {
            get => LocalContract is { } l ? (float)l.TimeAccepted : field;
            set;
        }
        [Key(3)]
        public bool Completed { get; set; }
        [Key(4)]
        public byte? League {
            get => LocalContract is { } l ? (byte?)l.League : field;
            set;
        }
        [Key(5)]
        public byte PEPossible { get; set; }
        [Key(6)]
        public byte PEGained { get; set; }
        [Key(7)]
        public float ContributionAmount {
            get => LocalContract is { } l ? (float)l.CoopLastUploadedContribution : field;
            set;
        }
        [Key(8)]
        public PlayerGrade Grade {
            get => LocalContract is { } l ? l.Grade : field;
            set;
        }
        [Key(9)]
        public float EvaluationCxp {
            get => LocalContract is { } l ? (l.Evaluation is { } e ? (float)e.Cxp : 0f) : field;
            set;
        }
        [Key(10)]
        public byte NumGoalsAchieved {
            get => LocalContract is { } l ? (byte)l.NumGoalsAchieved : field;
            set;
        }
        [Key(11)]
        public List<string> ReportedUUIDs {
            get => LocalContract is { } l ? _reportedUUIDs ??= [.. l.ReportedUuids] : field;
            set {
                field = value;
                _reportedUUIDs = null;
            }
        }
        private List<string> _reportedUUIDs;
        [Key(12)]
        public byte[] LocalContractBytes {
            get;
            set {
                field = value;
                InvalidateLocalContract();
                _reportedUUIDs = null;
            }
        }

        protected override byte[] LocalContractBytesStorage => LocalContractBytes;

        protected override long TimeAcceptedUnix => (long)TimeAccepted;

        public CustomArchivedFarms() { }
        public CustomArchivedFarms(Ei.LocalContract localContract) {
            ContractId = localContract.Contract?.Identifier;
            var goals = localContract.Contract is not null ? localContract.Contract.GetGoals(localContract) : null;
            Completed = localContract.Contract is not null && localContract.NumGoalsAchieved == goals.Count;
            if(goals is not null) {
                PEPossible = (byte)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy).Sum(x => x.RewardAmount);
                PEGained = (byte)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy && goals.IndexOf(x) < localContract.NumGoalsAchieved).Sum(x => x.RewardAmount);
            }
            LocalContractBytes = StorageTrimmer.TrimmedBytes(localContract);
        }
    }


    public class CustomUniversalFarm {
        public static implicit operator CustomUniversalFarm(CustomFarm farm) {
            return new CustomUniversalFarm {
                FarmType = farm.FarmType, Artifacts = farm.Artifacts, BoostsUsed = farm.BoostsUsed, BoostTokensGiven = farm.BoostTokensGiven, BoostTokensReceived = farm.BoostTokensReceived,
                BoostTokensSpent = farm.BoostTokensSpent, Cancelled = farm.Cancelled, CashEarned = farm.CashEarned, CashSpent = farm.CashSpent, CommonResearch = farm.CommonResearch, Completed = farm.Completed, ContractId = farm.ContractId, ContributionFinalized = farm.ContributionFinalized, CoopAllowed = farm.CoopAllowed,
                CoopId = farm.CoopId, CoopSharedEndTime = farm.CoopSharedEndTime, EggsPaidFor = farm.EggsPaidFor, EggType = farm.EggType, EvaluationCxp = farm.EvaluationCxp, Grade = farm.Grade, Habs = farm.Habs, LastStepTime = farm.LastStepTime, League = farm.League, NumChickens = farm.NumChickens,
                ReportedUUIDs = farm.ReportedUUIDs, SilosOwned = farm.SilosOwned, TimeAccepted = farm.TimeAccepted, TimeCheatDebt = farm.TimeCheatDebt, TimeCheatsDetected = farm.TimeCheatsDetected, TrainLength = farm.TrainLength
            };
        }
        public static implicit operator CustomUniversalFarm(CustomArchivedFarms farm) {
            return new CustomUniversalFarm {
                CoopId = farm.CoopId, ContractId = farm.ContractId, TimeAccepted = (long)farm.TimeAccepted, Completed = farm.Completed, League = farm.League, ContributionAmount = farm.ContributionAmount,
                Grade = farm.Grade, EvaluationCxp = farm.EvaluationCxp, PEGained = farm.PEGained, PEPossible = farm.PEPossible
            };
        }

        public Ei.FarmType FarmType { get; set; }
        public string ContractId { get; set; }
        public double EggsPaidFor { get; set; }
        public uint? League { get; set; }
        public string CoopId { get; set; }
        public bool Cancelled { get; set; }
        public bool Completed { get; set; }
        public List<CustomResearch> CommonResearch { get; set; }
        public ulong NumChickens { get; set; }
        public Ei.Egg EggType { get; set; }
        public List<uint> TrainLength { get; set; }
        public List<uint> Vehicles;
        public List<EggIncArtifactInstance> Artifacts { get; set; }
        public uint SilosOwned { get; set; }
        public long TimeAccepted { get; set; }
        public bool CoopAllowed { get; set; }
        public long CoopSharedEndTime { get; set; }
        public ushort BoostTokensReceived { get; set; }
        public ushort BoostTokensGiven { get; set; }
        public ushort BoostTokensSpent { get; set; }
        public double CashEarned { get; set; }
        public double CashSpent { get; set; }
        public long TimeCheatDebt { get; set; }
        public ushort BoostsUsed { get; set; }
        public ushort TimeCheatsDetected { get; set; }
        public List<ushort> Habs { get; set; }
        public float LastStepTime { get; set; }
        public List<string> ReportedUUIDs { get; set; }
        public PlayerGrade Grade { get; set; }
        public double EvaluationCxp { get; set; }
        public bool ContributionFinalized { get; set; }
        public uint PEPossible { get; set; }
        public uint PEGained { get; set; }
        public double ContributionAmount { get; set; }
    }

    [MessagePackObject]
    public class SpaceMission {
        [Key(0)]
        public Spaceship Ship { get; set; }
        [Key(1)]
        public DurationType Duration { get; set; }
        [Key(2)]
        public Status Status { get; set; }
        [Key(3)]
        public List<SpaceMissionFuel> Fuels { get; set; }
        [Key(4)]
        public long DurationSeconds { get; set; }
        [Key(5)]
        public long StartTime { get; set; }
        [Key(6)]
        public Name Targeting { get; set; } = Name.Unknown;
        [Key(7)]
        public uint Capacity { get; set; } = 0;
        [Key(8)]
        public uint Stars { get; set; } = 0;


        [IgnoreMember]
        public long ReturnTime {
            get {
                return StartTime + DurationSeconds;
            }
        }
    }

    [MessagePackObject]
    public class SpaceMissionFuel {
        [Key(0)]
        public Ei.Egg Egg { get; set; }
        [Key(1)]
        public double Amount { get; set; }
    }

    public enum TrophyLevel {
        Bronze = 1,
        Silver = 2,
        Gold = 3,
        Platinum = 4,
        Diamond = 5,
    }
}
