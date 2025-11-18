using CsvHelper;

using EGG9000.Bot;
using EGG9000.Common.Database.Entities;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EGG9000.Common.JsonData {

    public class EiResearch {
        private static List<EiResearchItem> _Instance { get; set; }

        public static List<EiResearchItem> Get(List<ResearchCostSubmission> researchCostSubmissions) {
            //if(_Instance != null) {
            //    return _Instance;
            //}

            var assembly = Assembly.GetExecutingAssembly();

            var resourceName = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith("researches.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            _Instance = JsonConvert.DeserializeObject<List<EiResearchItem>>(json);


            resourceName = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith("curiosity_research.csv"));
            using var stream2 = assembly.GetManifestResourceStream(resourceName);
            using(var reader2 = new StreamReader(stream2)) {
                using(var csv = new CsvReader(reader2, CultureInfo.InvariantCulture)) {
                    // Optional: Configure CsvHelper if needed (e.g., for headers)
                    // csv.Configuration.HasHeaderRecord = true;


                    var records = csv.GetRecords<dynamic>();
                    foreach(var record in records) {
                        if(int.TryParse(record.Tier, out int tier)) {
                            var researchItem = _Instance.FirstOrDefault(r => r.Tier == tier && r.Name.Equals(record.Name, StringComparison.OrdinalIgnoreCase));
                            if(researchItem != null) {
                                if(researchItem.EoVPrices == null) researchItem.EoVPrices = new List<double>();
                                // Ensure the list is large enough
                                while(researchItem.EoVPrices.Count < int.Parse(record.Level)) {
                                    researchItem.EoVPrices.Add(0);
                                }
                                var existing = researchCostSubmissions.OrderByDescending(x => x.SubmittedAt).FirstOrDefault(x => x.ID == researchItem.Id && x.Level == int.Parse(record.Level));
                                if(existing != null) {
                                    researchItem.EoVPrices[int.Parse(record.Level) - 1] = existing.Cost;
                                    continue;
                                } 
                                if(record.CostRaw == "") {
                                    researchItem.EoVPrices[int.Parse(record.Level) - 1] = -1;
                                } else {
                                    try {
                                        researchItem.EoVPrices[int.Parse(record.Level) - 1] = ((string)record.CostRaw).FromEggString() * 2;
                                    } catch(UnableToParseNumberExecption) {
                                        researchItem.EoVPrices[int.Parse(record.Level) - 1] = -1;
                                    }
                                }
                            }
                        } else {

                        }
                    }
                }
            }

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
        [Newtonsoft.Json.JsonIgnore]
        public List<double> EoVPrices { get; set; }

    }
}
