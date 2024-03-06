using EGG9000.Bot;
using EGG9000.Bot.EggIncAPI;

using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;

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
                foreach(var p in this.Contributors) {
                    p.TimeLeftSeconds = this.SecondsRemaining;
                    _participants.Add(p);
                }

                return _participants;
            }
        }

        public bool Finished() {
            return this.AllGoalsAchieved;
        }

        public bool Failed() {
            return !this.AllGoalsAchieved && this.ClearedForExit;
        }

        public partial class Types {
            public partial class ContributionInfo {
                public string GetID() { return UserId; }
                public string TotalString { get { return ArgumentsHelper.NumberToString(this.ContributionAmount, false, -1); } }
                public double TimeLeftSeconds { get; set; }

                public TimeSpan LastActive { get; set; }
                public string RateString { get { return ArgumentsHelper.NumberToString(this.ContributionRate * 60 * 60, false, -1) + "/h"; } }

                public double AmountWithOffline(double siloTimeMinutes, TimeSpan lastActive) {
                    if(TimeLeftSeconds < 0) {
                        var awayTime = Math.Min(lastActive.TotalSeconds, siloTimeMinutes * 60);
                        return this.ContributionAmount + this.ContributionRate * (awayTime + TimeLeftSeconds);
                    }
                    return this.ContributionAmount + this.ContributionRate * Math.Min(lastActive.TotalSeconds, siloTimeMinutes * 60);
                }

                public double AmountWithOfflineIgnoreSilo() {
                    if(TimeLeftSeconds < 0) {
                        return this.ContributionAmount;
                    }
                    return this.ContributionAmount + this.ContributionRate * LastActive.TotalSeconds;
                }

                public double Projected {
                    get {
                        if(TimeLeftSeconds < 0) {
                            return this.ContributionAmount + this.ContributionRate * Math.Min(TimeLeftSeconds, LastActive.TotalSeconds);
                        }
                        return this.ContributionAmount + this.ContributionRate * TimeLeftSeconds + this.ContributionRate * LastActive.TotalSeconds;
                    }
                }
            }
        }


    }

    public partial class Contract {

        public List<Ei.Contract.Types.Goal> GetGoals(int league) {
            if(this.GradeSpecs.Count > 0)
                return this.GradeSpecs[league - 1].Goals.ToList();
            else return this.GoalSets[league].Goals.ToList();
        }
        public List<Ei.Contract.Types.Goal> GetGoals(Ei.LocalContract contract) {
            if(contract.Grade != Types.PlayerGrade.GradeUnset)
                return this.GradeSpecs[(int)contract.Grade - 1].Goals.ToList();
            else if(this.GoalSets.Count > 0)
                return this.GoalSets[(int)contract.League].Goals.ToList();
            else
                return this.Goals.ToList();
        }

        public int GetPossiblePE() {
            return (int)((this.GradeSpecs?.FirstOrDefault()?.Goals ?? this.GoalSets?.FirstOrDefault()?.Goals ?? this.Goals).FirstOrDefault(x => x.RewardType == Ei.RewardType.EggsOfProphecy)?.RewardAmount ?? 0);
        }
    }

    public partial class LocalContract {
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }
        public bool Completed {
            get {
                var targetGoals = Contract.GradeSpecs.Count > 0 && Grade != Contract.Types.PlayerGrade.GradeUnset ?
                    Contract.GradeSpecs[(int)(this.Grade - 1)].Goals.Count :
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
        //public FarmDetails GetFarmDetails(Types.Simulation farm) {
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
        //    public Types.Simulation Farm { get; set; }
        //    public Ei.LocalContract Contract { get; set; }
        //    public Google.Protobuf.Collections.RepeatedField<Ei.Backup.Types.ResearchItem> EpicResearch { get; set; }
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
        public static Dictionary<Egg, double> GetFuelTargets(Types.Spaceship Ship, Types.DurationType DurationType) {
            Dictionary<Types.Spaceship, Dictionary<Types.DurationType, Dictionary<Egg, double>>> ShipFuelTargets = new Dictionary<Types.Spaceship, Dictionary<Types.DurationType, Dictionary<Egg, double>>>();
            ShipFuelTargets.Add(Types.Spaceship.ChickenOne, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Tutorial, new Dictionary<Egg, double> { { Egg.RocketFuel, 1e5 } } },
                    { Types.DurationType.Short, new Dictionary<Egg, double> { { Egg.RocketFuel, 2e6 } } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> { { Egg.RocketFuel, 3e6 } } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> { { Egg.RocketFuel, 10e6 } } }
                });
            ShipFuelTargets.Add(Types.Spaceship.ChickenNine, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> { { Egg.RocketFuel, 10e6 } } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> { { Egg.RocketFuel, 15e6 } } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> { { Egg.RocketFuel, 25e6 } } }
                });
            ShipFuelTargets.Add(Types.Spaceship.ChickenHeavy, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> { { Egg.RocketFuel, 100e6 } } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.RocketFuel, 50e6 },
                        { Egg.Fusion, 5e6 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.RocketFuel, 75e6 },
                        { Egg.Fusion, 25e6 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.Bcr, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.RocketFuel, 250e6 },
                        { Egg.Fusion, 50e6 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.RocketFuel, 400e6 },
                        { Egg.Fusion, 75e6 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Superfood, 5e6 },
                        { Egg.RocketFuel, 300e6 },
                        { Egg.Fusion, 100e6 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.MilleniumChicken, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Fusion, 5e9 },
                        { Egg.Graviton, 1e9 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Fusion, 7e9 },
                        { Egg.Graviton, 5e9 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Superfood, 10e6 },
                        { Egg.Fusion, 10e9 },
                        { Egg.Graviton, 15e9 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.CorellihenCorvette, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Fusion, 15e9 },
                        { Egg.Graviton, 2e9 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Fusion, 20e9 },
                        { Egg.Graviton, 3e9 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Superfood, 500e6 },
                        { Egg.Fusion, 25e9 },
                        { Egg.Graviton, 5e9 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.Galeggtica, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Fusion, 50e9 },
                        { Egg.Graviton, 10e9 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Fusion, 75e9 },
                        { Egg.Graviton, 25e9 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Fusion, 100e9 },
                        { Egg.Graviton, 50e9 },
                        { Egg.Antimatter, 1e9 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.Chickfiant, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 200e9 },
                        { Egg.Antimatter, 50e9 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 250e9 },
                        { Egg.Antimatter, 150e9 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Tachyon, 25e9 },
                        { Egg.Dilithium, 250e9 },
                        { Egg.Antimatter, 250e9 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.Voyegger, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 1e12 },
                        { Egg.Antimatter, 1e12 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 1.5e12 },
                        { Egg.Antimatter, 1.5e12 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Tachyon, 100e9 },
                        { Egg.Dilithium, 2e12},
                        { Egg.Antimatter, 2e12 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.Henerprise, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 2e12 },
                        { Egg.Antimatter, 2e12 }
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 3e12 },
                        { Egg.Antimatter, 3e12 },
                        { Egg.DarkMatter, 3e12 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Tachyon, 1e12 },
                        { Egg.Dilithium, 3e12},
                        { Egg.Antimatter, 3e12 },
                        { Egg.DarkMatter, 3e12 },
                    } }
                });
            ShipFuelTargets.Add(Types.Spaceship.Atreggies, new Dictionary<Types.DurationType, Dictionary<Egg, double>> {
                    { Types.DurationType.Short, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 4e12 },
                        { Egg.Antimatter, 4e12 },
                        { Egg.DarkMatter, 3e12 },
                    } },
                    { Types.DurationType.Long, new Dictionary<Egg, double> {
                        { Egg.Dilithium, 6e12 },
                        { Egg.Antimatter, 6e12 },
                        { Egg.DarkMatter, 4e12 },
                    } },
                    { Types.DurationType.Epic, new Dictionary<Egg, double> {
                        { Egg.Tachyon, 2e12 },
                        { Egg.Dilithium, 6e12},
                        { Egg.Antimatter, 6e12 },
                        { Egg.DarkMatter, 6e12 },
                    } }
                });

            return ShipFuelTargets[Ship][DurationType];
        }
    }
}