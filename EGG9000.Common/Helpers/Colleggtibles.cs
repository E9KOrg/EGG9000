// Ignore Spelling: Colleggtibles

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Common.Database.CustomFarm;
using static Ei.GameModifier.Types;

namespace EGG9000.Common.Helpers {
    public class Colleggtibles {
        public static Dictionary<GameDimension, double> GetCollectibleData(List<DBCustomEgg> customEggs, CustomBackup backup) {
            var dimensionColleggtibleEffect = new Dictionary<GameDimension, double>();
            customEggs.Where(x => backup.GetColleggtibleLevel(x.Identifier) != 0)
                .Select(x => {
                    var collegtibleLevel = backup.GetColleggtibleLevel(x.Identifier);
                    return new Colleggtible() {
                        Dimension = x.Modifiers[0].GetGameDimension(),
                        Value = x.Modifiers[(int)collegtibleLevel - 1].Value,
                    };
                }).ToList().ForEach(colleggtible => {
                    if(!dimensionColleggtibleEffect.TryGetValue(colleggtible.Dimension, out double currentValue)) {
                        dimensionColleggtibleEffect[colleggtible.Dimension] = 1.0;
                    }
                    dimensionColleggtibleEffect[colleggtible.Dimension] *= colleggtible.Value;
                });

            // Fill in any missing game dimensions (i.e., dimensions without colleggtibles):
            foreach(GameDimension dimension in Enum.GetValues(typeof(GameDimension))) {
                if(!dimensionColleggtibleEffect.ContainsKey(dimension)) dimensionColleggtibleEffect[dimension] = 1.0;
            }

            return dimensionColleggtibleEffect;
        }
    }
}
