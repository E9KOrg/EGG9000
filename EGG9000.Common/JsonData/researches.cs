using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.JsonData {

    public class EiResearch {
        private static List<EiResearchItem> _Instance { get; set; }

        public static List<EiResearchItem> Get() {
            if(_Instance != null) {
                return _Instance;
            }

            var assembly = Assembly.GetExecutingAssembly();

            var resourceName = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith("researches.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            _Instance = JsonConvert.DeserializeObject<List<EiResearchItem>>(json);



            return _Instance;
        }
    }
    public class EiResearchItem {
        [JsonProperty("serial_id")]
        public int SerialId { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("tier")]
        public int Tier { get; set; }

        [JsonProperty("categories")]
        public string Categories { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("effect_type")]
        public string EffectType { get; set; }

        [JsonProperty("levels")]
        public int Levels { get; set; }

        [JsonProperty("per_level")]
        public double PerLevel { get; set; }

        [JsonProperty("levels_compound")]
        public string LevelsCompound { get; set; }

        [JsonProperty("prices")]
        public List<double> Prices { get; set; }

    }
}
