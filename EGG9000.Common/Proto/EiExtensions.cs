using EGG9000.Bot;
using System;
using System.Collections.Generic;
using System.Linq;
using static Ei.Contract.Types;
using static Ei.MissionInfo.Types;

namespace Ei {
    public partial class EggIncFirstContactResponse {
        public bool Success { get; set; }
        public string Error { get; set; }
        public bool Unchanged { get; set; }
        public DateTime CacheAdded { get; set; }
    }

    public partial class ContractCoopStatusResponse {
        public bool Success { get; set; }
        public string Error { get; set; }


        private List<Types.ContributionInfo> _participants { get; set; }
        public List<Types.ContributionInfo> Participants {
            get {
                if(_participants != null)
                    return _participants;
                _participants = new List<Types.ContributionInfo>();
                foreach(var p in Contributors) {
                    p.TimeLeftSeconds = SecondsRemaining;
                    _participants.Add(p);
                }
                return _participants;
            }
        }

        public bool Finished() {
            return AllGoalsAchieved;
        }

        public bool Failed() {
            return !AllGoalsAchieved && ClearedForExit;
        }

        public partial class Types {
            public partial class ContributionInfo {
                public string GetID() { return UserId; }
                public string TotalString { get { return ArgumentsHelper.NumberToString(ContributionAmount, false, -1); } }
                public double TimeLeftSeconds { get; set; }

                public TimeSpan LastActive { get; set; }
                public string RateString { get { return ArgumentsHelper.NumberToString(ContributionRate * 60 * 60, false, -1) + "/h"; } }

                public double AmountWithOffline(double siloTimeMinutes, TimeSpan lastActive) {
                    if(TimeLeftSeconds < 0) {
                        var awayTime = Math.Min(lastActive.TotalSeconds, siloTimeMinutes * 60);
                        return ContributionAmount + ContributionRate * (awayTime + TimeLeftSeconds);
                    }
                    return ContributionAmount + ContributionRate * Math.Min(lastActive.TotalSeconds, siloTimeMinutes * 60);
                }

                public double AmountWithOfflineIgnoreSilo() {
                    if(TimeLeftSeconds < 0) {
                        return ContributionAmount;
                    }
                    return ContributionAmount + ContributionRate * LastActive.TotalSeconds;
                }

                public double Projected {
                    get {
                        if(TimeLeftSeconds < 0) {
                            return ContributionAmount + ContributionRate * Math.Min(TimeLeftSeconds, LastActive.TotalSeconds);
                        }
                        return ContributionAmount + ContributionRate * TimeLeftSeconds + ContributionRate * LastActive.TotalSeconds;
                    }
                }
            }
        }
    }

    public partial class Contract {

        public List<Goal> GetGoals(int league) {
            if(GradeSpecs.Count > 0)
                return [.. GradeSpecs[league - 1].Goals];
            else return [.. GoalSets[league].Goals];
        }
        public List<Goal> GetGoals(LocalContract contract) {
            if(contract.Grade != PlayerGrade.GradeUnset)
                return [.. GradeSpecs[(int)contract.Grade - 1].Goals];
            else if(GoalSets.Count > 0)
                return [.. GoalSets[(int)contract.League].Goals];
            else
                return [.. Goals];
        }

        public int GetPossiblePE() {
            return (int)((GradeSpecs?.FirstOrDefault()?.Goals ?? GoalSets?.FirstOrDefault()?.Goals ?? Goals).FirstOrDefault(x => x.RewardType == Ei.RewardType.EggsOfProphecy)?.RewardAmount ?? 0);
        }
    }

    public partial class LocalContract {
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }
        public bool Completed {
            get {
                var targetGoals = Contract.GradeSpecs.Count > 0 && Grade != PlayerGrade.GradeUnset ?
                    Contract.GradeSpecs[(int)(Grade - 1)].Goals.Count :
                    (Contract.GoalSets.Any() ?
                    Contract.GoalSets[0].Goals.Count : Contract.Goals.Count);
                return NumGoalsAchieved == targetGoals;
            }
        }
    }

    public partial class Backup {
        public DateTime CacheAdded { get; set; }
        public string GetID() {
            if(!string.IsNullOrEmpty(EiUserId)) {
                return EiUserId;
            }
            return UserId;
        }
        //public FarmDetails GetFarmDetails(Simulation farm) {
        //    var farmIndex = Farms.IndexOf(farm);

        //    var artifacts = new List<EggIncArtifactInstance>();

        //    if(ArtifactsDb != null) {
        //        var activeArtifactSlots = ArtifactsDb.ActiveArtifactSets[farmIndex].Slots.Where(x => x.Occupied);
        //        var activeArtifacts = activeArtifactSlots.Select(x => ArtifactsDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId));

        //        artifacts.AddRange(activeArtifacts.Where(x => x != null).Select(x => EggIncArtifacts.GetArtifact(x.Artifact.Spec)));

        //        artifacts.AddRange(activeArtifacts.Where(x => x != null)
        //            .SelectMany(x => ArtifactsDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId)?.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y))));
        //        artifacts = artifacts.Where(x => x != null).ToList();
        //    }

        //    return new FarmDetails {
        //        EpicResearch = Game.EpicResearch,
        //        Farm = farm,
        //        Contract = Contracts.Contracts.FirstOrDefault(x => x.Contract.Identifier == farm.ContractId) ?? Contracts.Archive.FirstOrDefault(x => x.Contract.Identifier == farm.ContractId),
        //        EarningsBonus = Game.EarningsBonus,
        //        CurrentMultiplier = Game.CurrentMultiplier,
        //        Artifacts = artifacts
        //    };
        //}

        //public class FarmDetails {
        //    public List<EggIncArtifactInstance> Artifacts { get; set; }
        //    public double CurrentMultiplier { get; set; }
        //    public double EarningsBonus { get; set; }
        //    public Simulation Farm { get; set; }
        //    public Ei.LocalContract Contract { get; set; }
        //    public Google.Protobuf.Collections.RepeatedField<Ei.Backup.ResearchItem> EpicResearch { get; set; }
        //    public Double EggLayingRate {
        //        get {
        //            return Research.GetEggLayingRatePerSec(Farm, EpicResearch.ToList()) * EggIncArtifacts.GetEggLayingRateMultiple(Artifacts);
        //        }
        //    }
        //    public Double MaxShippingRate {
        //        get {
        //            return Research.GetShippingCapacityPerSec(Farm, EpicResearch.ToList()) * EggIncArtifacts.GetShippingMultiple(Artifacts);
        //        }
        //    }
        //    public Double EggValue {
        //        get {
        //            return Research.GetEggValue(Farm, EpicResearch.ToList()) *EggIncArtifacts.GetEggValueMutiple(Artifacts);
        //        }
        //    }
        //    public Double CurrentShippingRate {
        //        get {
        //            return Math.Min(MaxShippingRate, EggLayingRate);
        //        }
        //    }
        //    public Double Income {
        //        get {
        //            return CurrentShippingRate * EggValue * (EarningsBonus/100) * CurrentMultiplier;
        //        }
        //    }

        //    public Double MaxRunningBonus {
        //        get {
        //            return Research.MaxRunningBonus(Farm, EpicResearch.ToList()) + EggIncArtifacts.GetMaxRunningBonusAdditive(Artifacts);
        //        }
        //    }
        //}

        public partial class Types {
            public partial class Game {
                public double SoulEggsTotal { get { return SoulEggsD == 0 ? SoulEggs : SoulEggsD; } }

                public double EarningsBonus {
                    get {
                        var soul_egg_bonus = (double)(EpicResearch.FirstOrDefault(x => x.Id == "soul_eggs")?.Level ?? 0d) + 10;
                        var prophecy_bonus = ((double)(EpicResearch.FirstOrDefault(x => x.Id == "prophecy_bonus")?.Level ?? 0d) + 5) / 100 + 1;
                        var earnings_bonus = SoulEggsTotal * soul_egg_bonus * Math.Pow(prophecy_bonus, EggsOfProphecy);
                        return earnings_bonus;
                    }
                }

            }
        }
    }

    public partial class MissionInfo {
        public static Dictionary<Egg, double> GetFuelTargets(Spaceship Ship, DurationType DurationType) {
            return new Dictionary<Spaceship, Dictionary<DurationType, Dictionary<Egg, double>>> {
                {Spaceship.ChickenOne, new() {
                    { DurationType.Tutorial, new(){ { Egg.RocketFuel, 1e5 } }},
                    { DurationType.Short, new(){ { Egg.RocketFuel, 2e6 } }},
                    { DurationType.Long, new() { { Egg.RocketFuel, 3e6 } }},
                    { DurationType.Epic, new() { { Egg.RocketFuel, 10e6 } }},
                }},
                {Spaceship.ChickenNine, new() {
                    { DurationType.Short, new() { { Egg.RocketFuel, 10e6 } }},
                    { DurationType.Long, new() { { Egg.RocketFuel, 15e6 } }},
                    { DurationType.Epic, new() { { Egg.RocketFuel, 25e6 } }},
                }},
                {Spaceship.ChickenHeavy, new() {
                    { DurationType.Short, new() { { Egg.RocketFuel, 100e6 } }},
                    { DurationType.Long, new() { { Egg.RocketFuel, 50e6 }, { Egg.Fusion, 5e6 } }},
                    { DurationType.Epic, new() { { Egg.RocketFuel, 75e6 }, { Egg.Fusion, 25e6 } }},
                }},
                {Spaceship.Bcr, new() {
                    { DurationType.Short, new() { { Egg.RocketFuel, 250e6 }, { Egg.Fusion, 50e6 } }},
                    { DurationType.Long, new() { { Egg.RocketFuel, 400e6 }, { Egg.Fusion, 75e6 } }},
                    { DurationType.Epic, new() { { Egg.Superfood, 5e6 }, { Egg.RocketFuel, 300e6 }, { Egg.Fusion, 100e6 } }},
                }},
                {Spaceship.MilleniumChicken, new() {
                    { DurationType.Short, new() { { Egg.Fusion, 5e9 }, { Egg.Graviton, 1e9 } }},
                    { DurationType.Long, new() { { Egg.Fusion, 7e9 }, { Egg.Graviton, 5e9 } }},
                    { DurationType.Epic, new() { { Egg.Superfood, 10e6 }, { Egg.Fusion, 10e9 }, { Egg.Graviton, 15e9 } }},
                }},
                {Spaceship.CorellihenCorvette, new() {
                    { DurationType.Short, new() { { Egg.Fusion, 15e9 }, { Egg.Graviton, 2e9 } }},
                    { DurationType.Long, new() { { Egg.Fusion, 20e9 }, { Egg.Graviton, 3e9 } }},
                    { DurationType.Epic, new() { { Egg.Superfood, 500e6 }, { Egg.Fusion, 25e9 }, { Egg.Graviton, 5e9 } }},
                }},
                {Spaceship.Galeggtica, new() {
                    { DurationType.Short, new() { { Egg.Fusion, 50e9 }, { Egg.Graviton, 10e9 } }},
                    { DurationType.Long, new() { { Egg.Fusion, 75e9 }, { Egg.Graviton, 25e9 } }},
                    { DurationType.Epic, new() { { Egg.Fusion, 100e9 }, { Egg.Graviton, 50e9 }, { Egg.Antimatter, 1e9 } }},
                }},
                {Spaceship.Chickfiant, new() {
                    { DurationType.Short, new() { { Egg.Dilithium, 200e9 }, { Egg.Antimatter, 50e9 } }},
                    { DurationType.Long, new() { { Egg.Dilithium, 250e9 }, { Egg.Antimatter, 150e9 } }},
                    { DurationType.Epic, new() { { Egg.Tachyon, 25e9 }, { Egg.Dilithium, 250e9 }, { Egg.Antimatter, 250e9 } }},
                }},
                {Spaceship.Voyegger, new() {
                    { DurationType.Short, new() { { Egg.Dilithium, 1e12 }, { Egg.Antimatter, 1e12 } }},
                    { DurationType.Long, new() { { Egg.Dilithium, 1.5e12 }, { Egg.Antimatter, 1.5e12 } }},
                    { DurationType.Epic, new() { { Egg.Tachyon, 100e9 }, { Egg.Dilithium, 2e12}, { Egg.Antimatter, 2e12 } }},
                }},
                {Spaceship.Henerprise, new() {
                    { DurationType.Short, new() { { Egg.Dilithium, 2e12 }, { Egg.Antimatter, 2e12 } }},
                    { DurationType.Long, new() { { Egg.Dilithium, 3e12 }, { Egg.Antimatter, 3e12 }, { Egg.DarkMatter, 3e12 } }},
                    { DurationType.Epic, new() { { Egg.Tachyon, 1e12 }, { Egg.Dilithium, 3e12}, { Egg.Antimatter, 3e12 }, { Egg.DarkMatter, 3e12 } }},
                }},
                {Spaceship.Atreggies, new() {
                    { DurationType.Short, new() { { Egg.Dilithium, 4e12 }, { Egg.Antimatter, 4e12 }, { Egg.DarkMatter, 3e12 } }},
                    { DurationType.Long, new() { { Egg.Dilithium, 6e12 }, { Egg.Antimatter, 6e12 }, { Egg.DarkMatter, 4e12 } }},
                    { DurationType.Epic, new() { { Egg.Tachyon, 2e12 }, { Egg.Dilithium, 6e12}, { Egg.Antimatter, 6e12 }, { Egg.DarkMatter, 6e12 } }},
                }}
            }[Ship][DurationType];
        }
    }
}