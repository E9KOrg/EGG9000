using System;
using System.Collections.Generic;

namespace EGG9000.Common.JsonData {
    public class ArtifactEmoji {
        public class ArtifactEmojiItem {
            public byte Id;
            public byte Tier;
            public string Emoji;

        }

        private static readonly EmbeddedResource<List<ArtifactEmojiItem>> _res =
            EmbeddedResource.Json<List<ArtifactEmojiItem>>("ArtifactEmoji.json", PostProcess);

        public static List<ArtifactEmojiItem> Get() => _res.Value;

        private static List<ArtifactEmojiItem> PostProcess(List<ArtifactEmojiItem> items) {
            items.ForEach(x => {
                if(x.Emoji.Contains("stone", StringComparison.OrdinalIgnoreCase))
                    x.Tier--; // Convert to 0-based index
            });
            return items;
        }
    }
}
