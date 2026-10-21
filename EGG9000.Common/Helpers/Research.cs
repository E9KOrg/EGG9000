using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using EggSharp.GameData;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public class Research {

        public class ResearchItem {
            public string id { get; set; }
            public string Name { get; set; }
            public int Levels { get; set; }
            public decimal Increase { get; set; }
            public ResearchEffect Type { get; set; }
            public bool Epic { get; set; }
        }

        public static double GetEggLayingRatePerSec(CustomFarm farm, List<CustomResearch> epic) {
            double ratePerMin = farm.NumChickens * 2;

            foreach(var item in EpicResearchList) {
                if(item.Type == ResearchEffect.EggLayingRate || item.Type == ResearchEffect.EggLayingAndValue) {
                    var current = epic.First(x => x.Id == item.id);
                    var r = current.Level * (double)item.Increase;
                    if(r > 0) ratePerMin *= 1 + r;
                }
            }

            foreach(var item in CommonResearchList) {
                if(item.Type == ResearchEffect.EggLayingRate || item.Type == ResearchEffect.EggLayingAndValue) {
                    var current = farm.CommonResearch.First(x => x.Id == item.id);
                    var r = current.Level * (double)item.Increase;
                    if(r > 0) ratePerMin *= 1 + r;
                }
            }
            return ratePerMin / 60;
        }

        public static double MaxRunningBonus(CustomFarm farm, List<CustomResearch> epic) {
            double baseValue = 5;

            foreach(var item in EpicResearchList.Where(x => x.Type == ResearchEffect.MaxRunningBonus)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) baseValue += r;
            }

            foreach(var item in CommonResearchList.Where(x => x.Type == ResearchEffect.MaxRunningBonus)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) baseValue += r;
            }

            return baseValue;
        }

        public static double InternalHatchery(CustomFarm farm, List<CustomResearch> epic) {
            double baseValue = 0;

            foreach(var item in CommonResearchList.Where(x => x.Type == ResearchEffect.InternalHatchery)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) baseValue += r;
            }


            foreach(var item in EpicResearchList.Where(x => x.Type == ResearchEffect.InternalHatchery)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) baseValue *= r + 1;
            }

            return baseValue;
        }

        public static double InternalHatcheryOfflineBonus(CustomFarm farm, List<CustomResearch> epic) {
            double baseValue = 0;
            foreach(var item in EpicResearchList.Where(x => x.Type == ResearchEffect.InternalHatcheryOffline)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue += r;
                }
            }
            return baseValue;
        }

        public static double GetEggValue(CustomFarm farm, List<CustomResearch> epic, DBContract contract, List<DBCustomEgg> customEggs) {
            var baseValue = 0.01;
            try {
                baseValue = EggIncStatics.GetEggById(farm.EggType, contract, customEggs).value;
            } catch(NullReferenceException) {
                throw new Exception($"Egg type {farm.EggType} ({(int)farm.EggType}) not found in database.");
            }
            foreach(var item in EpicResearchList.Where(x => x.Type == ResearchEffect.EggValue || x.Type == ResearchEffect.EggLayingAndValue)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) baseValue *= 1 + r;
            }

            foreach(var item in CommonResearchList.Where(x => x.Type == ResearchEffect.EggValue || x.Type == ResearchEffect.EggLayingAndValue || x.Type == ResearchEffect.EggValueMultiply)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                if(current.Level == 0)
                    continue;
                if(item.Type == ResearchEffect.EggValueMultiply) {
                    var r = Math.Pow((double)item.Increase, current.Level);
                    baseValue *= r;
                } else {
                    var r = current.Level * (double)item.Increase;
                    baseValue *= 1 + r;
                }
            }

            return baseValue;
        }

        public static double GetHabSpace(CustomFarm farm, List<CustomResearch> epic) {
            var baseValue = EggIncHabSpace.GetBaseHabSpace(farm);
            foreach(var item in EpicResearchList.Where(x => x.Type == ResearchEffect.HabCapacity)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) baseValue *= 1 + r;
            }

            foreach(var item in CommonResearchList.Where(x => x.Type == ResearchEffect.HabCapacity)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                if(current.Level == 0)
                    continue;
                var r = current.Level * (double)item.Increase;
                baseValue *= 1 + r;
            }

            return baseValue;
        }

        // A standard permit caps the player at 2 silos, a pro permit at 10.
        public const int MaxSilosStandardPermit = 2;
        public const int MaxSilosProPermit = 10;

        public static int GetMaxSilos(int permitLevel) =>
            permitLevel > 0 ? MaxSilosProPermit : MaxSilosStandardPermit;

        public static double GetTotalSiloCapacity(CustomBackup backup) {
            var baseAwayTime = 60;
            var epicSilo = backup.EpicResearch.FirstOrDefault(x => x.Id == "silo_capacity");
            if(epicSilo != null) {
                baseAwayTime += (int)epicSilo.Level * 6;
            }
            return baseAwayTime;
        }

        public static double GetFarmSiloTime(Ei.PlayerFarmInfo farmInfo) {
            var baseAwayTime = 60;
            var epicSilo = farmInfo.EpicResearch.FirstOrDefault(x => x.Id == "silo_capacity");
            if(epicSilo != null) {
                baseAwayTime += (int)epicSilo.Level * 6;
            }
            return baseAwayTime * farmInfo.SilosOwned;
        }

        public static double GetShippingCapacityPerSec(CustomFarm farm, List<CustomResearch> epic) {
            var vehicles = Vehicles;

            double baseMultiplier = 1;
            double hoverMultiplier = 1;
            double hyperLoopMultipler = 1;

            foreach(var item in EpicResearchList) {
                if(item.Type != ResearchEffect.VehicleCapacity)
                    continue;
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseMultiplier *= 1 + r;
                    hoverMultiplier *= 1 + r;
                    hyperLoopMultipler *= 1 + r;
                }
            }

            foreach(var item in CommonResearchList) {
                if(item.Type != ResearchEffect.VehicleCapacity)
                    continue;
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseMultiplier *= 1 + r;
                    hoverMultiplier *= 1 + r;
                    hyperLoopMultipler *= 1 + r;
                }
            }

            foreach(var item in CommonResearchList) {
                if(item.Type != ResearchEffect.HoverVehicleCapacity)
                    continue;
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    hoverMultiplier *= 1 + r;
                    hyperLoopMultipler *= 1 + r;
                }
            }

            foreach(var item in CommonResearchList) {
                if(item.Type != ResearchEffect.HyperloopCapacity)
                    continue;
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    hyperLoopMultipler *= 1 + r;
                }
            }


            double baseRate = 0;
            for(var i = 0; i < (farm.Vehicles?.Count ?? 0); i++) {
                var v = farm.Vehicles[i];
                double size;
                if(v == 11) {
                    size = vehicles[v] * farm.TrainLength[i] * hyperLoopMultipler;
                } else if(v >= 9) {
                    size = vehicles[v] * hoverMultiplier;
                } else {
                    size = vehicles[v] * baseMultiplier;
                }
                baseRate += size;
            }


            return baseRate / 60;
        }

        public static double GetCommonResearchCostMultiplier(CustomFarm farm, List<CustomResearch> epic) {
            double costMult = 1;
            var item = EpicResearchList.FirstOrDefault(x => x.Type == ResearchEffect.ResearchCost);
            if(item != null) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r < 0) costMult *= 1 + r;
            }
            return costMult;
        }

        public static readonly Dictionary<uint, uint> Vehicles = GameDataSource.Provider.Vehicles.Vehicles.ToDictionary(v => (uint)v.Index, v => (uint)v.Capacity);

        public static Effect FindGameData(string id, EffectTarget? preferred = null) {
            var rows = GameDataSource.Provider.All(Families.Research)
                .Where(e => e.Id == id || e.Id.StartsWith(id + ":", StringComparison.Ordinal))
                .ToList();
            return rows.FirstOrDefault(e => e.Id == id)
                ?? rows.FirstOrDefault(e => e.Target == preferred)
                ?? rows.FirstOrDefault();
        }

        public static EffectTarget? TargetOf(ResearchEffect type) => type switch {
            ResearchEffect.EggLayingRate => EffectTarget.EggLayingRate,
            ResearchEffect.EggLayingAndValue => EffectTarget.EggLayingRate,
            ResearchEffect.EggValue => EffectTarget.EggValue,
            ResearchEffect.EggValueMultiply => EffectTarget.EggValue,
            ResearchEffect.HabCapacity => EffectTarget.HabCapacity,
            ResearchEffect.InternalHatchery => EffectTarget.IHRBase,
            ResearchEffect.InternalHatcheryOffline => EffectTarget.IHROffline,
            ResearchEffect.VehicleCapacity => EffectTarget.VehicleCapacity,
            ResearchEffect.HoverVehicleCapacity => EffectTarget.HoverVehicleCapacity,
            ResearchEffect.FleetSize => EffectTarget.FleetSize,
            ResearchEffect.RunningBonus => EffectTarget.RunningChickenBonus,
            ResearchEffect.MaxRunningBonus => EffectTarget.RunningChickenBonusCap,
            ResearchEffect.HyperloopLength => EffectTarget.HyperloopTrainLength,
            ResearchEffect.HyperloopCapacity => EffectTarget.HyperloopCarCapacity,
            ResearchEffect.ResearchCost => EffectTarget.ResearchCost,
            _ => null
        };

        private static ResearchItem FromGameData(string id, ResearchEffect type, bool epic) {
            var effect = FindGameData(id, TargetOf(type))
                ?? throw new InvalidOperationException($"Research '{id}' missing from EggSharp game data.");
            var magnitude = effect.Target == EffectTarget.SiloTime ? effect.Magnitude / 60 : effect.Magnitude;
            return new ResearchItem {
                id = id,
                Name = effect.TryMeta("name", out var name) ? (string)name : id,
                Levels = effect.MaxLevel ?? 0,
                Increase = (decimal)magnitude,
                Type = type,
                Epic = epic
            };
        }

        public static readonly List<ResearchItem> EpicResearchList = [
            FromGameData("epic_internal_incubators", ResearchEffect.InternalHatchery, true),
            FromGameData("epic_clucking", ResearchEffect.RunningBonus, true),
            FromGameData("epic_multiplier", ResearchEffect.MaxRunningBonus, true),
            FromGameData("cheaper_research", ResearchEffect.ResearchCost, true),
            FromGameData("silo_capacity", ResearchEffect.Other, true),
            FromGameData("int_hatch_sharing", ResearchEffect.Other, true),
            FromGameData("int_hatch_calm", ResearchEffect.InternalHatcheryOffline, true),
            FromGameData("soul_eggs", ResearchEffect.Other, true),
            FromGameData("epic_egg_laying", ResearchEffect.EggLayingRate, true),
            FromGameData("transportation_lobbyist", ResearchEffect.VehicleCapacity, true),
            FromGameData("prophecy_bonus", ResearchEffect.Other, true),
        ];

        public static readonly List<ResearchItem> CommonResearchList = [
            FromGameData("comfy_nests", ResearchEffect.EggLayingRate, false),
            FromGameData("nutritional_sup", ResearchEffect.EggValue, false),
            FromGameData("excitable_chickens", ResearchEffect.RunningBonus, false),

            FromGameData("hab_capacity1", ResearchEffect.HabCapacity, false),
            FromGameData("internal_hatchery1", ResearchEffect.InternalHatchery, false),
            FromGameData("padded_packaging", ResearchEffect.EggValue, false),
            FromGameData("bigger_eggs", ResearchEffect.EggValue, false),

            FromGameData("internal_hatchery2", ResearchEffect.InternalHatchery, false),
            FromGameData("leafsprings", ResearchEffect.VehicleCapacity, false),
            FromGameData("vehicle_reliablity", ResearchEffect.FleetSize, false),
            FromGameData("coordinated_clucking", ResearchEffect.MaxRunningBonus, false),

            FromGameData("usde_prime", ResearchEffect.EggValue, false),
            FromGameData("hen_house_ac", ResearchEffect.EggLayingRate, false),
            FromGameData("superfeed", ResearchEffect.EggValue, false),
            FromGameData("microlux", ResearchEffect.HabCapacity, false),

            FromGameData("lightweight_boxes", ResearchEffect.VehicleCapacity, false),
            FromGameData("excoskeletons", ResearchEffect.FleetSize, false),
            FromGameData("internal_hatchery3", ResearchEffect.InternalHatchery, false),
            FromGameData("improved_genetics", ResearchEffect.EggLayingAndValue, false),

            FromGameData("traffic_management", ResearchEffect.FleetSize, false),
            FromGameData("motivational_clucking", ResearchEffect.MaxRunningBonus, false),
            FromGameData("driver_training", ResearchEffect.VehicleCapacity, false),
            FromGameData("shell_fortification", ResearchEffect.EggValue, false),

            FromGameData("egg_loading_bots", ResearchEffect.FleetSize, false),
            FromGameData("super_alloy", ResearchEffect.VehicleCapacity, false),
            FromGameData("even_bigger_eggs", ResearchEffect.EggValueMultiply, false),
            FromGameData("internal_hatchery4", ResearchEffect.InternalHatchery, false),

            FromGameData("quantum_storage", ResearchEffect.VehicleCapacity, false),
            FromGameData("genetic_purification", ResearchEffect.EggValue, false),
            FromGameData("internal_hatchery5", ResearchEffect.InternalHatchery, false),
            FromGameData("time_compress", ResearchEffect.EggLayingRate, false),

            FromGameData("hover_upgrades", ResearchEffect.HoverVehicleCapacity, false),
            FromGameData("graviton_coating", ResearchEffect.EggValueMultiply, false),
            FromGameData("grav_plating", ResearchEffect.HabCapacity, false),
            FromGameData("chrystal_shells", ResearchEffect.EggValue, false),

            FromGameData("autonomous_vehicles", ResearchEffect.FleetSize, false),
            FromGameData("neural_linking", ResearchEffect.InternalHatchery, false),
            FromGameData("telepathic_will", ResearchEffect.EggValue, false),
            FromGameData("enlightened_chickens", ResearchEffect.MaxRunningBonus, false),

            FromGameData("dark_containment", ResearchEffect.VehicleCapacity, false),
            FromGameData("atomic_purification", ResearchEffect.EggValue, false),
            FromGameData("multi_layering", ResearchEffect.EggValueMultiply, false),
            FromGameData("timeline_diversion", ResearchEffect.EggLayingRate, false),

            FromGameData("wormhole_dampening", ResearchEffect.HabCapacity, false),
            FromGameData("eggsistor", ResearchEffect.EggValue, false),
            FromGameData("micro_coupling", ResearchEffect.HyperloopLength, false),
            FromGameData("neural_net_refine", ResearchEffect.VehicleCapacity, false),

            FromGameData("matter_reconfig", ResearchEffect.EggValue, false),
            FromGameData("timeline_splicing", ResearchEffect.EggValueMultiply, false),
            FromGameData("hyper_portalling", ResearchEffect.HyperloopCapacity, false),
            FromGameData("relativity_optimization", ResearchEffect.EggLayingRate, false),
        ];

        public enum ResearchEffect {
            EggValueMultiply,
            EggLayingRate,
            EggValue,
            EggLayingAndValue,
            HabCapacity,
            InternalHatchery,
            InternalHatcheryOffline,
            VehicleCapacity,
            HoverVehicleCapacity,
            FleetSize,
            RunningBonus,
            MaxRunningBonus,
            HyperloopLength,
            HyperloopCapacity,
            ResearchCost,
            Other
        }
    }
}
