using EGG9000.Common.Database;

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
            EmbeddedResource.Json<EiEpicResearch>("ei-epic-research.json");
        public static EiEpicResearch Get() => _res.Value;
    }
}
