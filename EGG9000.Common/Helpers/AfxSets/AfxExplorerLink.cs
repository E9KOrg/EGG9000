using System.Text.RegularExpressions;
using EGG9000.Common.Helpers;

namespace EGG9000.Common.Helpers.AfxSets {
    public static class AfxExplorerLink {
        private const string Base = "https://wasmegg-carpet.netlify.app/artifact-explorer/#/artifact/";

        public static string Url(EggIncArtifactInstance instance, bool isStone) {
            var enumName = ((ArtifactNames)instance.Id).ToString();
            var slug = Regex.Replace(enumName, "(?<=.)([A-Z])", "-$1").ToLowerInvariant();
            var tier = isStone ? instance.Tier + 2 : instance.Tier;
            return $"{Base}{slug}-{tier}/";
        }
    }
}
