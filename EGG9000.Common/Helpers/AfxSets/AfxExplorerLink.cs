using System.Text.RegularExpressions;

namespace EGG9000.Common.Helpers.AfxSets {
    public static class AfxExplorerLink {
        private const string Base = "https://wasmegg-carpet.netlify.app/artifact-explorer/#/artifact/";

        public static string Url(EggIncArtifactInstance instance, bool isStone) {
            var enumName = ((ArtifactNames)instance.Id).ToString();
            var slug = Regex.Replace(enumName, "(?<=.)([A-Z])", "-$1").ToLowerInvariant();
            // Stone Tier is 0-based (0..3); the explorer uses 1..4. Artifact Tier is already 1-based.
            var tier = isStone ? instance.Tier + 1 : instance.Tier;
            return $"{Base}{slug}-{tier}/";
        }
    }
}
