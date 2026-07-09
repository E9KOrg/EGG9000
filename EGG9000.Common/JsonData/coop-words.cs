using System.Collections.Generic;

namespace EGG9000.Common.JsonData {
    // Word pool used to generate random co-op names (two words + a number). Lives here as an embedded
    // resource so it can be edited without recompiling logic and stays consistent with the other
    // JsonData statics. All entries are lowercase 3-5 character words, vetted to avoid pairing into
    // questionable combinations.
    public class CoopWords {
        private static readonly EmbeddedResource<List<string>> _res =
            EmbeddedResource.Json<List<string>>("coop-words.json");

        public static List<string> Get() => _res.Value;
    }
}
