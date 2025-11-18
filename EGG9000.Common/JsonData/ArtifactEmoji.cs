using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.JsonData {
    public class ArtifactEmoji {
        public class ArtifactEmojiItem {
            public byte Id;
            public byte Tier;
            public string Emoji;

        }

        private static List<ArtifactEmojiItem> Instance = null;
        public static List<ArtifactEmojiItem> Get() {
            if(Instance != null ) return Instance;

            var assembly = Assembly.GetExecutingAssembly();

            var resourceName = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith("ArtifactEmoji.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            Instance = JsonConvert.DeserializeObject<List<ArtifactEmojiItem>>(json);
            Instance.ForEach(x => {
                if(x.Emoji.Contains("stone", StringComparison.OrdinalIgnoreCase))
                    x.Tier--;
                }); // Convert to 0-based index
            return Instance;
        }
    }
}
