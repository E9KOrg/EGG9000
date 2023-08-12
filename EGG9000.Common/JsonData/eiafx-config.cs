using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.JsonData.EiAfxConfig {


    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class ArtifactParameter {
        public Spec spec { get; set; }
        public double baseQuality { get; set; }
        public double oddsMultiplier { get; set; }
        public double value { get; set; }
        public double craftingPrice { get; set; }
        public double craftingPriceLow { get; set; }
        public int craftingPriceDomain { get; set; }
        public double craftingPriceCurve { get; set; }
    }

    public class Duration {
        public string durationType { get; set; }
        public int seconds { get; set; }
        public double quality { get; set; }
        public double minQuality { get; set; }
        public double maxQuality { get; set; }
        public int capacity { get; set; }
        public int levelCapacityBump { get; set; }
        public double levelQualityBump { get; set; }
    }

    public class MissionParameter {
        public string ship { get; set; }
        public List<Duration> durations { get; set; }
        public List<int> levelMissionRequirements { get; set; }
        public int capacityDEPRECATED { get; set; }
    }

    public class Root {
        public List<MissionParameter> missionParameters { get; set; }
        public List<ArtifactParameter> artifactParameters { get; set; }

        public static Root Instance = null;
        public static Root Get() {
            if(Instance != null) {
                return Instance;
            }

            var assembly = Assembly.GetExecutingAssembly();

            string resourceName = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith("eiafx-config.json"));

            using(Stream stream = assembly.GetManifestResourceStream(resourceName))
            using(StreamReader reader = new StreamReader(stream)) {
                string json = reader.ReadToEnd();
                Instance = JsonConvert.DeserializeObject<Root>(json);
                return Instance;
            }
        }
    }

    public class Spec {
        public string name { get; set; }
        public string level { get; set; }
        public string rarity { get; set; }
        public string egg { get; set; }
    }



}
