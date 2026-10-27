using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

using System.Collections.Generic;

namespace EGG9000.Common.JsonData {

    public class EpicResearchItem {
        public int order { get; set; }
        public string id { get; set; }
        public int _firstCost { get; set; }
        public int _lastCost { get; set; }
        public int _numLevels { get; set; }
        public List<int> _overrideCosts { get; set; }
        public List<int> Costs {
            get {
                if(_overrideCosts?.Count > 0) { //Items that don't follow the formula (fuel tank)
                    return _overrideCosts;
                } else if(_numLevels == 1) { //One-time-purchase items (hyperloop)
                    return [_firstCost];
                } else {
                    var _costs = new List<int>();
                    for(var level = 1; level <= _numLevels; level++) {
                        _costs.Add(
                            _firstCost + (int)((level - 1) * (double)(_lastCost - _firstCost) / (_numLevels - 1))
                        );
                    }
                    return _costs;
                }
            }
        }
        public string title { get; set; }
        public string description { get; set; }
        public CustomResearch MappedBackupResearch { get; set; }

    }

    public class EiEpicResearch {
        public List<EpicResearchItem> epicResearchItems { get; set; }

        private static readonly EmbeddedResource<EiEpicResearch> _res =
            EmbeddedResource.Json<EiEpicResearch>("ei-epic-research.json", ApplyGameData);
        public static EiEpicResearch Get() => _res.Value;

        private static EiEpicResearch ApplyGameData(EiEpicResearch data) {
            foreach(var item in data.epicResearchItems) {
                var effect = Research.FindGameData(item.id);
                if(effect is null || !effect.MetaBool("epic"))
                    continue;
                item._firstCost = (int)effect.MetaDouble("epicPriceStart");
                item._lastCost = (int)effect.MetaDouble("epicPriceEnd");
                item._numLevels = effect.MaxLevel ?? item._numLevels;
                if(effect.TryMeta("name", out var name))
                    item.title = (string)name;
                if(effect.TryMeta("description", out var description))
                    item.description = (string)description;
            }
            return data;
        }
    }
}
