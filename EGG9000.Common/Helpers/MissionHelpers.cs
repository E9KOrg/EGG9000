using EGG9000.Common.Database;
using EGG9000.Common.JsonData.EiAfxConfig;

using Ei;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Helpers {
    public static class MissionHelpers {

        public readonly static Dictionary<Spaceship, uint> MaxShipLevels = new() {
            { Spaceship.ChickenOne, 0 },
            { Spaceship.ChickenNine, 2 },
            { Spaceship.ChickenHeavy, 3 },
            { Spaceship.Bcr, 4 },
            { Spaceship.MilleniumChicken, 4 },
            { Spaceship.CorellihenCorvette, 4 },
            { Spaceship.Galeggtica, 5 },
            { Spaceship.Chickfiant, 5 },
            { Spaceship.Voyegger, 6 },
            { Spaceship.Henerprise, 7 }
        };

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
