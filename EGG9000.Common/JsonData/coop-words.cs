using System.Collections.Generic;

namespace EGG9000.Common.JsonData {
    // Entries are vetted to avoid pairing into questionable combinations.
    public class CoopWords {
        private static readonly EmbeddedResource<List<string>> _res =
            EmbeddedResource.Json<List<string>>("coop-words.json");

        public static List<string> Get() => _res.Value;
    }
}
