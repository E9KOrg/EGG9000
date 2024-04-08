using EGG9000.Common.Database;
using EGG9000.Common.JsonData.EiAfxConfig;

using System;
using System.Collections.Generic;
using System.Linq;
using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Helpers {
    public static class MissionHelpers {

        public static readonly Dictionary<Spaceship, uint> MaxShipLevels 
            = Root.Get().missionParameters.ToDictionary(
                mp => mp.shipEnum, 
                mp => (uint)mp.levelMissionRequirements.Count
            );

        public static readonly Dictionary<Spaceship, Dictionary<DurationType, List<int>>> NominalShipCapacities
            = Root.Get().missionParameters.ToDictionary(
                mp => mp.shipEnum,
                mp => mp.durations.ToDictionary(
                    dur => dur.durationTypeEnum,
                    dur => Enumerable.Range(0, mp.levelMissionRequirements.Count + 1)
                                     .Select(i => dur.capacity + i * dur.levelCapacityBump)
                                     .ToList()
                )
            );

        private static readonly Dictionary<Spaceship, Dictionary<DurationType, int>> ShipBaseTimesMinutes
            = Root.Get().missionParameters.ToDictionary(
                mp => mp.shipEnum,
                mp => mp.durations.ToDictionary(
                    dur => dur.durationTypeEnum,
                    dur => dur.seconds / 60
                )
            );

        private static readonly Dictionary<Spaceship, List<int>> LevelRequirements
            = Root.Get().missionParameters.ToDictionary(
                mp => mp.shipEnum,
                mp => mp.levelMissionRequirements
            );

        public static int GetNominalCapacity(this CustomBackup backup, SpaceMission mission) {
            if(mission is null || !NominalShipCapacities.ContainsKey(mission.Ship)) return 0;
            var nominalCaps = NominalShipCapacities[mission.Ship][mission.Duration];
            var erLevel = backup.EpicResearch.FirstOrDefault(er => er.Id == "afx_mission_capacity")?.Level ?? 0;
            return (int)(nominalCaps[(int)mission.Stars] * (1 + (erLevel * 0.05)));
        }

        public static string GetShipTime(this CustomBackup backup, Spaceship ship, DurationType duration, int number) {
            var erScalar = backup.EpicResearch.FirstOrDefault(er => er.Id == "afx_mission_time").Level;
            //ER only applies to Quintillion and above
            if(erScalar < 0 || ship < Spaceship.MilleniumChicken) erScalar = 0;
            var shipTimes = ShipBaseTimesMinutes[ship];
            return shipTimes.ContainsKey(duration) ?
                MinutesToString((int)(ShipBaseTimesMinutes[ship][duration] * ((double)number / 3) * (double)(1 - (.01 * erScalar)))) //Div by 3 for 3 ship slots
                : "";
        }

        public static int GetShipTimeRaw(this CustomBackup backup, Spaceship ship, DurationType duration, int number) {
            var erScalar = backup.EpicResearch.FirstOrDefault(er => er.Id == "afx_mission_time").Level;
            //ER only applies to Quintillion and above
            if(erScalar < 0 || ship < Spaceship.MilleniumChicken) erScalar = 0;
            var shipTimes = ShipBaseTimesMinutes[ship];
            return shipTimes.ContainsKey(duration) ? 
                (int)(ShipBaseTimesMinutes[ship][duration] * ((double)number / 3) * (double)(1 - (.01 * erScalar))) //Div by 3 for 3 ship slots
                : 0;
        }

        public static string MinutesToString(int minutes) {
            var timeSpan = TimeSpan.FromMinutes(minutes);

            if(timeSpan.TotalMinutes < 60) return $"{timeSpan.Minutes}m";

            if(timeSpan.TotalMinutes < 1440)
                return $"{timeSpan.Hours}h{(timeSpan.Minutes > 0 ? $"{timeSpan.Minutes}m" : "")}";

            if(timeSpan.TotalMinutes < 525600)
                return $"{timeSpan.Days}d{timeSpan.Hours}h{(timeSpan.Minutes > 0 ? $"{timeSpan.Minutes}m" : "")}";

            return $"{(int)(timeSpan.TotalDays / 365)}y{timeSpan.Days % 365}d{timeSpan.Hours}h{(timeSpan.Minutes > 0 ? $"{timeSpan.Minutes}m" : "")}";
        }

        public static int GetShipLevel(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent == null) return 0;

            //If they don't have the ship unlocked
            if(!backup.ShipsSent.Any(x => x.ship == ship)) return 0;

            for(var i = LevelRequirements[ship].Count; i > 0; i--) {
                if(backup.ShipsSent.Where(x => x.ship == ship).ToList().Sum(s => s.count * ((int)s.type < 3 ? (1 + ((int)s.type * .4)) : 0)) >= LevelRequirements[ship].Take(i).Sum()) {
                    return i;
                }
            }

            return 0;
        }

        public static string GetProperShipName(Spaceship ship) {
            return ship switch {
                Spaceship.ChickenOne => "Chicken One",
                Spaceship.ChickenNine => "Chicken Nine",
                Spaceship.ChickenHeavy => "Chicken Heavy",
                Spaceship.Bcr => "BCR",
                Spaceship.MilleniumChicken => "Quintillion Chicken",
                Spaceship.CorellihenCorvette => "Cornish-Hen Corvette",
                Spaceship.Galeggtica => "Galleggtica",
                Spaceship.Chickfiant => "Defihent",
                Spaceship.Voyegger => "Voyegger",
                Spaceship.Henerprise => "Henerprise",
                Spaceship.Atreggies => "Atreggies Henliner",
                _ => "How did you get here?"
            };
        }

        public static Dictionary<DurationType, int> LaunchShipsToNext(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent is null) return [];

            var points = backup.ShipsSent.Where(x => x.ship == ship).ToList().Sum(s => s.count * ((int)s.type < 3 ? (1 + ((int)s.type * .4)) : 0));
            var levelMissionRequirements = LevelRequirements[ship];

            for(var i = levelMissionRequirements.Count; i > 0; i--) {
                if(points >= levelMissionRequirements.Take(i).Sum()) {
                    return ShipsFromPoints(levelMissionRequirements.Take(i + 1).Sum() - points);
                }
            }
            if(levelMissionRequirements.Count == 0) return [];
            if(points < levelMissionRequirements[0]) return ShipsFromPoints(levelMissionRequirements[0] - points);
            else return ShipsFromPoints(0);
        }

        public static Dictionary<DurationType, int> LaunchShipsToMax(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent is null) return [];
            var points = backup.ShipsSent.Where(x => x.ship == ship).ToList().Sum(s => s.count * ((int)s.type < 3 ? (1 + ((int)s.type * .4)) : 0));
            return ShipsFromPoints(LevelRequirements[ship].Sum() - points);
        }

        public static Dictionary<DurationType, int> ShipsFromPoints(double neededPoints) {
            return Enum.GetValues<DurationType>().ToDictionary(
               d => d,
               d => (int)Math.Ceiling(neededPoints / (1 + ((int)d * 0.4)))
            );
        }

        public static double GetShipProgressNextLevel(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent == null) return 0;
            if(ship == Spaceship.ChickenOne) return 1;

            var points = backup.ShipsSent.Where(x => x.ship == ship).ToList().Sum(s => s.count * ((int)s.type < 3 ? (1 + ((int)s.type * .4)) : 0));

            var levelMissionRequirements = Root.Get().missionParameters.First(x => x.shipEnum == ship).levelMissionRequirements;

            for(var i = levelMissionRequirements.Count; i > 0; i--) {
                var sum = levelMissionRequirements.Take(i).Sum();
                if(points >= sum) {
                    if(levelMissionRequirements.Count > i) {
                        var numLaunchedCurrentLevel = points - sum;
                        double numForNextLevel = levelMissionRequirements[i];
                        return numLaunchedCurrentLevel / numForNextLevel;
                    } else {
                        return 1;
                    }
                }
            }
            if(points > 0 && points < levelMissionRequirements[0]) return points / levelMissionRequirements[0];
            return 0;
        }

        public static double GetShipProgressTotal(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent == null) return 0;
            var points = backup.ShipsSent.Where(x => x.ship == ship).ToList().Sum(s => s.count * ((int)s.type < 3 ? (1 + ((int)s.type * .4)) : 0));
            double sum = LevelRequirements[ship].Sum();

            return (points == 0 ? 0 : (points / sum > 1 ? 1 : points / sum));
        }

        public static bool HasMaxedShips(this CustomBackup backup) {
            return MaxShipLevels.All(x => backup.GetShipLevel(x.Key) == x.Value);
        }
    }
}
