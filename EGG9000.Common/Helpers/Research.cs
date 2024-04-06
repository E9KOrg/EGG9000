using EGG9000.Common.Database;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Bot.EggIncAPI {
    public class Research {

        public class ResearchItem {
            public string id { get; set; }
            public string Name { get; set; }
            public int Levels { get; set; }
            public decimal Increase { get; set; }
            public IT Type { get; set; }
            public bool Epic { get; set; }
        }

        public static double GetEggLayingRatePerSec(CustomFarm farm, List<CustomResearch> epic) {
            double ratePerMin = farm.NumChickens * 2;

            foreach(var item in Research.EpicResearchList) {
                if(item.Type == Research.IT.EggLayingRate || item.Type == Research.IT.EggLayingAndValue) {
                    var current = epic.First(x => x.Id == item.id);
                    var r = current.Level * (double)item.Increase;
                    if(r > 0) {
                        ratePerMin *= 1 + r;
                    }
                }
            }

            foreach(var item in Research.CommonResearchList) {
                if(item.Type == Research.IT.EggLayingRate || item.Type == Research.IT.EggLayingAndValue) {
                    var current = farm.CommonResearch.First(x => x.Id == item.id);
                    var r = current.Level * (double)item.Increase;
                    if(r > 0) {
                        ratePerMin *= 1 + r;
                    }
                }
            }
            return ratePerMin / 60;
        }

        public static double GetEggLayingRatePerSec(CustomFarm farm, List<CustomResearch> epic, TimingsFactory timings) {
            double ratePerMin = farm.NumChickens * 2;
            timings.Set("ratePerMin");
            foreach(var item in Research.CommonResearchList) {
                if(item.Type == Research.IT.EggLayingRate || item.Type == Research.IT.EggLayingAndValue) {
                    var current = farm.CommonResearch.First(x => x.Id == item.id);
                    var r = current.Level * (double)item.Increase;
                    if(r > 0) {
                        ratePerMin *= 1 + r;
                    }
                }
            }
            timings.Set("Common");
            var epicItems = Research.EpicResearchList.Where(x => x.Type == Research.IT.EggLayingRate || x.Type == Research.IT.EggLayingAndValue).ToArray();
            timings.Set("epicItems");
            foreach(var item in epicItems) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    ratePerMin *= 1 + r;
                }
            }
            timings.Set("Epic");

            return ratePerMin / 60;
        }

        public static double MaxRunningBonus(CustomFarm farm, List<CustomResearch> epic) {
            double baseValue = 5;

            foreach(var item in Research.EpicResearchList.Where(x => x.Type == Research.IT.MaxRunningBonus)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue += r;
                    //Console.Write($"Increase: {r}")
                }
            }

            foreach(var item in Research.CommonResearchList.Where(x => x.Type == Research.IT.MaxRunningBonus)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue += r;
                }
            }

            return baseValue;
        }

        public static double InternalHatchery(CustomFarm farm, List<CustomResearch> epic) {
            double baseValue = 0;

            foreach(var item in Research.CommonResearchList.Where(x => x.Type == Research.IT.InternalHatchery)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue += r;
                }
            }


            foreach(var item in Research.EpicResearchList.Where(x => x.Type == Research.IT.InternalHatchery)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue *= r + 1;
                }
            }

            return baseValue;
        }

        public static double GetEggValue(CustomFarm farm, List<CustomResearch> epic) {
            double baseValue = EggIncStatics.GetEggById(farm.EggType).Value;

            foreach(var item in Research.EpicResearchList.Where(x => x.Type == Research.IT.EggValue || x.Type == Research.IT.EggLayingAndValue)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue *= 1 + r;
                }
            }

            foreach(var item in Research.CommonResearchList.Where(x => x.Type == Research.IT.EggValue || x.Type == Research.IT.EggLayingAndValue || x.Type == IT.EggValueMultiply)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                if(current.Level == 0)
                    continue;
                if(item.Type == IT.EggValueMultiply) {
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
            double baseValue = EggIncHabSpace.GetBaseHabSpace(farm);
            //return baseValue;
            foreach(var item in Research.EpicResearchList.Where(x => x.Type == Research.IT.HabCapacity)) {
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseValue *= 1 + r;
                }
            }

            foreach(var item in Research.CommonResearchList.Where(x => x.Type == Research.IT.HabCapacity)) {
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                if(current.Level == 0)
                    continue;
                var r = current.Level * (double)item.Increase;
                baseValue *= 1 + r;
            }

            return baseValue;
        }

        public static double GetTotalSiloCapacity(CustomBackup backup) {
            var baseAwayTime = 60;
            var epicSilo = backup.EpicResearch.FirstOrDefault(x => x.Id == "silo_capacity");
            if(epicSilo != null) {
                baseAwayTime += (int)epicSilo.Level * 6;
            }
            return baseAwayTime;
        }

        public static Double GetFarmSiloTime(Ei.PlayerFarmInfo farmInfo) {
            var baseAwayTime = 60;
            var epicSilo = farmInfo.EpicResearch.FirstOrDefault(x => x.Id == "silo_capacity");
            if(epicSilo != null) {
                baseAwayTime += (int)epicSilo.Level * 6;
            }
            return baseAwayTime * farmInfo.SilosOwned;
        }

        //public static double GetEggShippedRatePerSec(CustomFarm farm, List<CustomResearch> epic) {
        //    var laying = GetEggLayingRatePerSec(farm, epic);
        //    var shipping = GetShippingCapacityPerSec(farm, epic);

        //    if(shipping < laying) {
        //        //Console.WriteLine($"Shipping maxed");
        //    }
        //    return Math.Min(laying, shipping);
        //}

        public static double GetShippingCapacityPerSec(CustomFarm farm, List<CustomResearch> epic) {
            var vehicles = Vehicles;

            double baseMultiplier = 1;
            double hoverMultiplier = 1;
            double hyperLoopMultipler = 1;

            foreach(var item in Research.EpicResearchList) {
                if(item.Type != IT.VehicleCapacity)
                    continue;
                var current = epic.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseMultiplier *= 1 + r;
                    hoverMultiplier *= 1 + r;
                    hyperLoopMultipler *= 1 + r;
                }
            }

            foreach(var item in Research.CommonResearchList) {
                if(item.Type != IT.VehicleCapacity)
                    continue;
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    baseMultiplier *= 1 + r;
                    hoverMultiplier *= 1 + r;
                    hyperLoopMultipler *= 1 + r;
                }
            }

            foreach(var item in Research.CommonResearchList) {
                if(item.Type != IT.HoverVehicleCapacity)
                    continue;
                var current = farm.CommonResearch.First(x => x.Id == item.id);
                var r = current.Level * (double)item.Increase;
                if(r > 0) {
                    hoverMultiplier *= 1 + r;
                    hyperLoopMultipler *= 1 + r;
                }
            }

            foreach(var item in Research.CommonResearchList) {
                if(item.Type != IT.HyperloopCapacity)
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

        public static Dictionary<uint, uint> Vehicles = new Dictionary<uint, uint> {
            {0, 5000 },
                {1, 15000},
                {2, 50000},
                {3, 100000},
                {4, 250000},
                {5, 500000},
                {6, 1000000},
                {7, 5000000},
                {8, 15000000},
                {9, 30000000}, //Hover
                {10, 50000000},
                {11, 50000000},
        };

        public static List<ResearchItem> EpicResearchList = new List<ResearchItem>{
            new ResearchItem { id = "epic_internal_incubators", Name = "Epic Int. Hatcheries", Levels = 20, Increase = 0.05M, Type = IT.InternalHatchery, Epic = true },
            new ResearchItem { id = "epic_clucking", Name = "Epic Clucking", Levels = 20, Increase = 0.001M, Type = IT.RunningBonus, Epic = true },
            new ResearchItem { id = "epic_multiplier", Name = "Epic Multiplier", Levels = 100, Increase = 2, Type = IT.MaxRunningBonus, Epic = true },
            new ResearchItem { id = "cheaper_research", Name = "Lab Upgrade", Levels = 10, Increase = 0.05M, Type = IT.ResearchCost, Epic = true },
            new ResearchItem { id = "silo_capacity", Name = "Silo Capacity", Levels = 20, Increase = 6M, Type = IT.Other, Epic = true },
            new ResearchItem { id = "int_hatch_sharing", Name = "Internal Hatchery Sharing", Levels = 10, Increase = 0.1M, Type = IT.Other, Epic = true },
            new ResearchItem { id = "int_hatch_calm", Name = "Internal Hatchery Calm", Levels = 20, Increase = 0.1M, Type = IT.Other, Epic = true },
            new ResearchItem { id = "soul_eggs", Name = "Soul Food", Levels = 140, Increase = 0.01M, Type = IT.Other, Epic = true },
            new ResearchItem { id = "epic_egg_laying", Name = "Epic Comfy Nests", Levels = 20, Increase = 0.05M, Type = IT.EggLayingRate, Epic = true },
            new ResearchItem { id = "transportation_lobbyist", Name = "Transportation Lobbyists", Levels = 30, Increase = 0.05M, Type = IT.VehicleCapacity , Epic = true},
            new ResearchItem { id = "prophecy_bonus", Name = "Prophecy Bonus", Levels = 5, Increase = .01M, Type = IT.Other, Epic = true },
        };

        public static List<ResearchItem> CommonResearchList = new List<ResearchItem> {
            new ResearchItem { id = "comfy_nests", Name = "Comfortable Nests", Levels = 50, Increase = 0.1M, Type = IT.EggLayingRate },
            new ResearchItem { id = "nutritional_sup", Name = "Nutritional Supplements", Levels = 40, Increase = 0.25M, Type = IT.EggValue },
            //new ResearchItem { id = "better_incubators", Name = "", Levels = 50, Increase = 0.1M, Type = IT.EggLayingRate },
            new ResearchItem { id = "excitable_chickens", Name = "Excitable Chickens", Levels = 25, Increase = 0.001M, Type = IT.RunningBonus },

            new ResearchItem { id = "hab_capacity1", Name = "Hen House Remodel", Levels = 8, Increase = 0.05M, Type = IT.HabCapacity },
            new ResearchItem { id = "internal_hatchery1", Name = "Internal Hatcheries", Levels = 10, Increase = 2M, Type = IT.InternalHatchery },
            new ResearchItem { id = "padded_packaging", Name = "Padded Packaging	", Levels = 30, Increase = 0.25M, Type = IT.EggValue },
            //new ResearchItem { id = "hatchery_expansion", Name = "", Levels = 50, Increase = 0.1M, Type = IT.EggLayingRate },
            new ResearchItem { id = "bigger_eggs", Name = "Bigger Eggs	", Levels = 1, Increase = 1M, Type = IT.EggValue },

            new ResearchItem { id = "internal_hatchery2", Name = "Internal Hatchery Upgrades", Levels = 10, Increase = 5M, Type = IT.InternalHatchery },
            new ResearchItem { id = "leafsprings", Name = "Improved Leafsprings", Levels = 30, Increase = 0.05M, Type = IT.VehicleCapacity },
            new ResearchItem { id = "vehicle_reliablity", Name = "Vehicle Reliability", Levels = 2, Increase = 1M, Type = IT.FleetSize },
            //new ResearchItem { id = "rooster_booster", Name = "", Levels = 50, Increase = 0.1M, Type = IT.EggLayingRate },
            new ResearchItem { id = "coordinated_clucking", Name = "Coordinated Clucking", Levels = 50, Increase = 0.2M, Type = IT.MaxRunningBonus },

            //new ResearchItem { id = "hatchery_rebuild1", Name = "", Levels = 50, Increase = 0.1M, Type = IT.EggLayingRate },
            new ResearchItem { id = "usde_prime", Name = "USDE Prime Certification", Levels = 1, Increase = 2M, Type = IT.EggValue },
            new ResearchItem { id = "hen_house_ac", Name = "Hen House A/C", Levels = 50, Increase = 0.05M, Type = IT.EggLayingRate },
            new ResearchItem { id = "superfeed", Name = "Super-Feed™ Diet", Levels = 35, Increase = 0.25M, Type = IT.EggValue },
            new ResearchItem { id = "microlux", Name = "Microlux™ Chicken Suites", Levels = 10, Increase = 0.05M, Type = IT.HabCapacity },

            //new ResearchItem { id = "compact_incubators", Name = "", Levels = 50, Increase = 0.1M, Type = IT.EggLayingRate },
            new ResearchItem { id = "lightweight_boxes", Name = "Lightweight Boxes", Levels = 40, Increase = 0.1M, Type = IT.VehicleCapacity },
            new ResearchItem { id = "excoskeletons", Name = "Depot Worker Exoskeletons", Levels = 2, Increase = 1M, Type = IT.FleetSize },
            new ResearchItem { id = "internal_hatchery3", Name = "Internal Hatchery Expansion", Levels = 15, Increase = 10M, Type = IT.InternalHatchery },
            new ResearchItem { id = "improved_genetics", Name = "Improved Genetics", Levels = 30, Increase = 0.15M, Type = IT.EggLayingAndValue },

            new ResearchItem { id = "traffic_management", Name = "Traffic Management", Levels = 2, Increase = 1M, Type = IT.FleetSize },
            new ResearchItem { id = "motivational_clucking", Name = "Motivational Clucking", Levels = 80, Increase = 0.5M, Type = IT.MaxRunningBonus },
            new ResearchItem { id = "driver_training", Name = "Driver Training", Levels = 30, Increase = 0.05M, Type = IT.VehicleCapacity },
            new ResearchItem { id = "shell_fortification", Name = "Shell Fortification", Levels = 60, Increase = 0.15M, Type = IT.EggValue },

            new ResearchItem { id = "egg_loading_bots", Name = "Egg Loading Bots", Levels = 2, Increase = 1M, Type = IT.FleetSize },
            new ResearchItem { id = "super_alloy", Name = "Super Alloy Frames", Levels = 50, Increase = 0.05M, Type = IT.VehicleCapacity },
            new ResearchItem { id = "even_bigger_eggs", Name = "Even Bigger Eggs", Levels = 5, Increase = 2M, Type = IT.EggValueMultiply },
            new ResearchItem { id = "internal_hatchery4", Name = "Internal Hatchery Expansion", Levels = 30, Increase = 25M, Type = IT.InternalHatchery },

            new ResearchItem { id = "quantum_storage", Name = "Quantum Egg Storage", Levels = 20, Increase = 0.05M, Type = IT.VehicleCapacity },
            new ResearchItem { id = "genetic_purification", Name = "Genetic Purification", Levels = 100, Increase = 0.1M, Type = IT.EggValue },
            new ResearchItem { id = "internal_hatchery5", Name = "Machine Learning Incubators", Levels = 250, Increase = 5M, Type = IT.InternalHatchery },
            new ResearchItem { id = "time_compress", Name = "Time Compression", Levels = 20, Increase = 0.1M, Type = IT.EggLayingRate },

            new ResearchItem { id = "hover_upgrades", Name = "Hover Upgrades", Levels = 25, Increase = 0.05M, Type = IT.HoverVehicleCapacity },
            new ResearchItem { id = "graviton_coating", Name = "Graviton Coating", Levels = 7, Increase = 2M, Type = IT.EggValueMultiply },
            new ResearchItem { id = "grav_plating", Name = "Grav Plating", Levels = 25, Increase = 0.02M, Type = IT.HabCapacity },
            new ResearchItem { id = "chrystal_shells", Name = "Crystalline Shelling", Levels = 100, Increase = 0.25M, Type = IT.EggValue },

            new ResearchItem { id = "autonomous_vehicles", Name = "Autonomous Vehicles", Levels = 5, Increase = 1M, Type = IT.FleetSize },
            new ResearchItem { id = "neural_linking", Name = "Neural Linking", Levels = 30, Increase = 50M, Type = IT.InternalHatchery },
            new ResearchItem { id = "telepathic_will", Name = "Telepathic Will", Levels = 50, Increase = 0.25M, Type = IT.EggValue },
            new ResearchItem { id = "enlightened_chickens", Name = "Enlightened Chickens", Levels = 150, Increase = 2M, Type = IT.MaxRunningBonus },

            new ResearchItem { id = "dark_containment", Name = "Dark Containment	", Levels = 25, Increase = 0.05M, Type = IT.VehicleCapacity },
            new ResearchItem { id = "atomic_purification", Name = "Atomic Purification", Levels = 50, Increase = 0.1M, Type = IT.EggValue },
            new ResearchItem { id = "multi_layering", Name = "Multiversal Layering", Levels = 3, Increase = 10M, Type = IT.EggValueMultiply },
            new ResearchItem { id = "timeline_diversion", Name = "Timeline Diversion", Levels = 50, Increase = 0.02M, Type = IT.EggLayingRate },

            new ResearchItem { id = "wormhole_dampening", Name = "Wormhole Dampening", Levels = 25, Increase = 0.02M, Type = IT.HabCapacity },
            new ResearchItem { id = "eggsistor", Name = "Eggsistor Miniturization", Levels = 100, Increase = 0.05M, Type = IT.EggValue },
            new ResearchItem { id = "micro_coupling", Name = "Gravitron Coupling", Levels = 5, Increase = 1M, Type = IT.HyperloopLength },
            new ResearchItem { id = "neural_net_refine", Name = "Neural Net Refinement", Levels = 25, Increase = 0.05M, Type = IT.VehicleCapacity },

            new ResearchItem { id = "matter_reconfig", Name = "Matter Reconfiguration", Levels = 500, Increase = 0.01M, Type = IT.EggValue },
            new ResearchItem { id = "timeline_splicing", Name = "Timeline Splicing", Levels = 1, Increase = 10M, Type = IT.EggValueMultiply },
            new ResearchItem { id = "hyper_portalling", Name = "Hyper Portalling", Levels = 25, Increase = 0.05M, Type = IT.HyperloopCapacity },
            new ResearchItem { id = "relativity_optimization", Name = "Relativity Optimization", Levels = 10, Increase = 0.1M, Type = IT.EggLayingRate },
        };

        public enum IT {
            EggValueMultiply,
            EggLayingRate,
            EggValue,
            EggLayingAndValue,
            HabCapacity,
            InternalHatchery,
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
