using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using static Ei.Contract.Types;
using static Ei.GameModifier.Types;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class CustomFarm : CustomFarmBase {
        [Key(0)]
        [DerivedSlot(nameof(SimulationBytes))]
        public Ei.FarmType FarmType {
            get => Simulation is { } s ? s.FarmType : field;
            set;
        }
        [Key(1)]
        [DerivedSlot(nameof(SimulationBytes))]
        public string ContractId {
            get => Simulation is { } s ? s.ContractId : field;
            set;
        }
        [Key(2)]
        [DerivedSlot(nameof(SimulationBytes))]
        public double EggsPaidFor {
            get => Simulation is { } s ? s.EggsPaidFor : field;
            set;
        }
        [Key(3)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public uint? League {
            get => LocalContract is { } l ? l.League : field;
            set;
        }
        [Key(4)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public string CoopId {
            get => LocalContract is { } l ? l.CoopIdentifier : field;
            set;
        }
        [Key(5)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public bool Cancelled {
            get => LocalContract is { } l ? l.Cancelled : field;
            set;
        }
        [Key(6)]
        public bool Completed { get; set; }
        [Key(7)]
        [DerivedSlot(nameof(SimulationBytes))]
        public List<CustomResearch> CommonResearch {
            get => Simulation is { } s ? _commonResearch ??= [.. s.CommonResearch.Select(x => new CustomResearch(x))] : field;
            set {
                field = value;
                _commonResearch = null;
            }
        }
        private List<CustomResearch> _commonResearch;
        [Key(8)]
        [DerivedSlot(nameof(SimulationBytes))]
        public ulong NumChickens {
            get => Simulation is { } s ? s.NumChickens : field;
            set;
        }
        [Key(10)]
        [DerivedSlot(nameof(SimulationBytes))]
        public Ei.Egg EggType {
            get => Simulation is { } s ? s.EggType : field;
            set;
        }
        [Key(11)]
        [DerivedSlot(nameof(SimulationBytes))]
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
        [DerivedSlot(nameof(SimulationBytes))]
        public uint SilosOwned {
            get => Simulation is { } s ? s.SilosOwned : field;
            set;
        }
        [Key(15)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public long TimeAccepted {
            get => LocalContract is { } l ? (long)l.TimeAccepted : field;
            set;
        }
        [Key(16)]
        public bool CoopAllowed { get; set; }
        [Key(17)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public long CoopSharedEndTime {
            get => LocalContract is { } l ? (long)l.CoopSharedEndTime : field;
            set;
        }
        [Key(18)]
        [DerivedSlot(nameof(SimulationBytes))]
        public ushort BoostTokensReceived {
            get => Simulation is { } s ? (ushort)s.BoostTokensReceived : field;
            set;
        }
        [Key(19)]
        [DerivedSlot(nameof(SimulationBytes))]
        public ushort BoostTokensGiven {
            get => Simulation is { } s ? (ushort)s.BoostTokensGiven : field;
            set;
        }
        [Key(20)]
        [DerivedSlot(nameof(SimulationBytes))]
        public ushort BoostTokensSpent {
            get => Simulation is { } s ? (ushort)s.BoostTokensSpent : field;
            set;
        }
        [Key(21)]
        [DerivedSlot(nameof(SimulationBytes))]
        public double CashEarned {
            get => Simulation is { } s ? s.CashEarned : field;
            set;
        }
        [Key(22)]
        [DerivedSlot(nameof(SimulationBytes))]
        public double CashSpent {
            get => Simulation is { } s ? s.CashSpent : field;
            set;
        }
        [Key(23)]
        [DerivedSlot(nameof(SimulationBytes))]
        public long TimeCheatDebt {
            get => Simulation is { } s ? (long)s.TimeCheatDebtDEP : field;
            set;
        }
        [Key(24)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public ushort BoostsUsed {
            get => LocalContract is { } l ? (ushort)l.BoostsUsed : field;
            set;
        }
        [Key(25)]
        [DerivedSlot(nameof(SimulationBytes))]
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
        [DerivedSlot(nameof(SimulationBytes))]
        public List<ushort> Habs {
            get => Simulation is { } s ? _habs ??= [.. s.Habs.Select(x => (ushort)x)] : field;
            set {
                field = value;
                _habs = null;
            }
        }
        private List<ushort> _habs;
        [Key(33)]
        [DerivedSlot(nameof(SimulationBytes))]
        public float LastStepTime {
            get => Simulation is { } s ? (float)s.LastStepTime : field;
            set;
        }
        [Key(34)]
        public List<string> ReportedUUIDs { get; set; }
        [Key(35)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public PlayerGrade Grade {
            get => LocalContract is { } l ? l.Grade : field;
            set;
        }
        [Key(36)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public double EvaluationCxp {
            get {
                if(LocalContract is not { } l)
                    return field;
                return l.Evaluation is { } e ? (float)e.Cxp : 0.0;
            }
            set;
        }
        [Key(37)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public bool ContributionFinalized {
            get => LocalContract is { } l ? l.CoopContributionFinalized : field;
            set;
        }
        [Key(38)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public double CoopSimulationEndTime {
            get => LocalContract is { } l ? l.CoopSimulationEndTime : field;
            set;
        }
        [Key(39)]
        [DerivedSlot(nameof(LocalContractBytes))]
        public byte NumGoalsAchieved {
            get => LocalContract is { } l ? (byte)l.NumGoalsAchieved : field;
            set;
        }
        [Key(40)]
        public bool Creator { get; set; }
        [Key(41)]
        [JsonIgnore]
        [XmlIgnore]
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
        [JsonIgnore]
        [XmlIgnore]
        [IgnoreDataMember]
        public Ei.Backup.Types.Simulation Simulation {
            get {
                if(_simulation is null && SimulationBytes is { Length: > 0 })
                    _simulation = Ei.Backup.Types.Simulation.Parser.ParseFrom(SimulationBytes);
                return _simulation;
            }
        }
        private Ei.Backup.Types.Simulation _simulation;

        [Key(42)]
        [JsonIgnore]
        [XmlIgnore]
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
}
