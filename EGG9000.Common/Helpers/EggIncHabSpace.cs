using EGG9000.Common.Database;
using EggSharp.GameData;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public static class EggIncHabSpace {
        private static readonly Lazy<FrozenDictionary<int, double>> _capacityByHabId = new(() =>
            GameDataSource.Provider.All(Families.Hab).ToFrozenDictionary(e => e.MetaInt("habId"), e => e.Magnitude));

        public static double BaseCapacity(int habId) => _capacityByHabId.Value.GetValueOrDefault(habId);

        public static double GetBaseHabSpace(CustomFarm farm) => farm?.Habs?.Sum(habId => BaseCapacity(habId)) ?? 0;
    }
}
