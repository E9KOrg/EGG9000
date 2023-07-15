using Ei;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Helpers {
    public static class MissionHelpers {

        public static Dictionary<Spaceship, uint> MaxShipLevels = new Dictionary<Spaceship, uint> {
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
    }
}
