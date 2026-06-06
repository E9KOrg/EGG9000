using EGG9000.Common.Database;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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

    public class EIEpicResearch {
        public List<EpicResearchItem> epicResearchItems { get; set; }

        private static EIEpicResearch Instance = null;
        public static EIEpicResearch Get() {
            if (Instance != null) return Instance;

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("ei-epic-research.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            Instance = JsonConvert.DeserializeObject<EIEpicResearch>(json);
            return Instance;
        }
    }
}
