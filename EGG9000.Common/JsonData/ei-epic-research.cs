using EGG9000.Common.Database;

using System.Collections.Generic;

namespace EGG9000.Common.JsonData.EIEpicResearch {

    public class EpicResearchItem {
        public int order { get; set; }
        public string id { get; set; }
        public int _firstCost { get; set; }
        public int _lastCost { get; set; }
        public int _numLevels { get; set; }
        public List<int> _overrideCosts { get; set; }
        public List<int> Costs {
            get {
                if(_overrideCosts?.Count > 0) { //Items that don't follow the forumla (fuel tank)
                    return _overrideCosts;
                } else if(_numLevels == 1) { //Used for one-time-purchase items (hyperloop)
                    return [_firstCost];
                } else { // All other ER
                    var _costs = new List<int>();
                    for(var level = 1; level <= _numLevels; level++) {
                        // Calculate cost for the current level using the formula:
                        // firstCost + [(level - 1) * (lastCost - firstCost)] / (levels - 1)
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
