using EGG9000.Common.Database;
using EGG9000.Common.JsonData.EiAfxConfig;

using Ei;

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
                MinutesToString(backup, (int)(ShipBaseTimesMinutes[ship][duration] * ((double)number / 3) * (double)(1 - (.01 * erScalar)))) //Div by 3 for 3 ship slots
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

        public static string MinutesToString(this CustomBackup backup, int minutes) {
            if(minutes < 60) return minutes + "m";
            if(minutes < 1440) { // Less than a day
                var hours = minutes / 60;
                var remainingMinutes = minutes % 60;
                return remainingMinutes == 0 ? hours + "h" : hours + "h" + remainingMinutes + "m";
            }
            if(minutes < 525600) { // Less than a year (assuming 365 days in a year)
                var days = minutes / 1440;
                var remainingMinutes = minutes % 1440;
                if(remainingMinutes == 0) return days + "d";
                var hours = remainingMinutes / 60;
                var remainingMinutesInHourSc = remainingMinutes % 60;
                return days + "d" + hours + "h" + remainingMinutesInHourSc + "m";
            }
            var years = minutes / 525600;
            var remainingMinutesInYear = minutes % 525600;
            if(remainingMinutesInYear == 0) return years + "y";
            var daysInYear = remainingMinutesInYear / 1440;
            var remainingMinutesInDay = remainingMinutesInYear % 1440;
            var hoursInDay = remainingMinutesInDay / 60;
            var remainingMinutesInHour = remainingMinutesInDay % 60;
            var result = years + "y";
            if(daysInYear > 0)
                result += daysInYear + "d";
            if(hoursInDay > 0)
                result += hoursInDay + "h";
            if(remainingMinutesInHour > 0)
                result += remainingMinutesInHour + "m";
            return result;
        }

        public static List<MissionInfo> GetLaunchedMissions(ArtifactsDB artifactsDB) {
            return (artifactsDB.MissionArchive.ToList() ?? new List<MissionInfo>())
                .Concat(artifactsDB.MissionInfos.ToList() ?? new List<MissionInfo>())
                .Where(m => m.Status != Status.Exploring)
                .OrderByDescending(m => m.StartTimeDerived).ToList();
        }

        public static Dictionary<Spaceship, uint> GetShipLevels(ArtifactsDB artifactsDB) {
            var newDic = new Dictionary<Spaceship, uint>();
            GetLaunchedMissions(artifactsDB).GroupBy(m => m.Ship).Select(group => group.OrderByDescending(ship => ship.Level).First()).ToList().ForEach(l => {
                newDic.Add(l.Ship, l.Level);
            });
            return newDic;
        }

        public static int GetShipLevel(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent == null)
                return 0;

            //If they don't have the ship unlocked
            if(!backup.ShipsSent.Any(x => x.ship == ship)) return 0;

            var points = backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Short).count +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Long).count * 1.4 +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Epic).count * 1.8;

            var levelMissionRequirements = Root.Get().missionParameters.First(x => ship.ToString().Equals(x.ship.Replace("_", ""), StringComparison.CurrentCultureIgnoreCase)).levelMissionRequirements;

            for(var i = levelMissionRequirements.Count; i > 0; i--) {
                var sum = levelMissionRequirements.Take(i).Sum();
                if(points >= sum) {
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
                Spaceship.CorellihenCorvette => "Cornish-Hen Corbette",
                Spaceship.Galeggtica => "Galleggtica",
                Spaceship.Chickfiant => "Defihent",
                Spaceship.Voyegger => "Voyegger",
                Spaceship.Henerprise => "Henerprise",
                Spaceship.Atreggies => "Atreggies Henliner",
                _ => "How did you get here?"
            };
        }

        public static Dictionary<DurationType, int> LaunchShipsToNext(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent is null) return new();

            var points = (!backup.ShipsSent.Any(x => x.ship == ship)) ? 0 : backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Short).count +
            backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Long).count * 1.4 +
            backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Epic).count * 1.8;

            var levelMissionRequirements = Root.Get().missionParameters.First(x => ship.ToString().Equals(x.ship.Replace("_", ""), StringComparison.CurrentCultureIgnoreCase)).levelMissionRequirements;

            for(var i = levelMissionRequirements.Count; i > 0; i--) {
                if(points >= levelMissionRequirements.Take(i).Sum()) {
                    return ShipsFromPoints(levelMissionRequirements.Take(i + 1).Sum() - points);
                }
            }
            if(levelMissionRequirements.Count == 0) return new();
            if(points == 0) return ShipsFromPoints(levelMissionRequirements[0]);
            else return ShipsFromPoints(0);
        }

        public static Dictionary<DurationType, int> LaunchShipsToMax(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent is null) return new();

            var points = (!backup.ShipsSent.Any(x => x.ship == ship)) ? 0 : backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Short).count +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Long).count * 1.4 +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Epic).count * 1.8;

            var levelMissionRequirements = Root.Get().missionParameters.First(x => ship.ToString().Equals(x.ship.Replace("_", ""), StringComparison.CurrentCultureIgnoreCase)).levelMissionRequirements;
            return ShipsFromPoints(levelMissionRequirements.Sum() - points);
        }

        public static Dictionary<DurationType, int> ShipsFromPoints(double neededPoints) {
            Dictionary<DurationType, int> newDic = new();
            if(neededPoints == 0) return newDic;

            newDic.Add(DurationType.Short, (int)Math.Ceiling(neededPoints / 1));
            newDic.Add(DurationType.Long, (int)Math.Ceiling(neededPoints / 1.4));
            newDic.Add(DurationType.Epic, (int)Math.Ceiling(neededPoints / 1.8));

            return newDic;
        }

        public static double GetShipProgressNextLevel(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent == null)
                return 0;

            //No stars, always maxed
            if(ship == Spaceship.ChickenOne) return 1;

            var points = (!backup.ShipsSent.Any(x => x.ship == ship)) ? 0 : backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Short).count  +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Long).count * 1.4 +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Epic).count * 1.8 ;

            var levelMissionRequirements = Root.Get().missionParameters.First(x => ship.ToString().Equals(x.ship.Replace("_", ""), StringComparison.CurrentCultureIgnoreCase)).levelMissionRequirements;

            for(var i = levelMissionRequirements.Count; i > 0; i--) {
                var sum = levelMissionRequirements.Take(i).Sum();
                if(points >= sum) {
                    if(levelMissionRequirements.Count > i) {
                        double numLaunchedCurrentLevel = points - sum;
                        double numForNextLevel = levelMissionRequirements[i];
                        return numLaunchedCurrentLevel / numForNextLevel;
                    } else {
                        return 1;
                    }
                    
                }
            }
            return 0;
        }

        public static double GetShipProgressTotal(this CustomBackup backup, Spaceship ship) {
            if(backup.ShipsSent == null)
                return 0;

            var points = (!backup.ShipsSent.Any(x => x.ship == ship)) ? 0 : backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Short).count +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Long).count * 1.4 +
                backup.ShipsSent.FirstOrDefault(x => x.ship == ship && x.type == DurationType.Epic).count * 1.8;

            var levelMissionRequirements = Root.Get().missionParameters.First(x => ship.ToString().Equals(x.ship.Replace("_", ""), StringComparison.CurrentCultureIgnoreCase)).levelMissionRequirements;

            var sum = levelMissionRequirements.Sum();

            return (points == 0 ? 0 : (points / sum > 1 ? 1 : points / sum));
        }

        public static bool HasMaxedShips(CustomBackup backup) {
            return MaxShipLevels.All(x => {
                var currentStars = backup.GetShipLevel(x.Key);
                if(currentStars == x.Value) {
                    return true;
                }
                return false;
            });
        }
    }
}
