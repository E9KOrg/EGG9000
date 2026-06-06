using Newtonsoft.Json;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace EGG9000.Common.JsonData {

    public record ArtifactFamily(
        string id,
        int afx_id,
        string name,
        int afx_type,
        string type,
        int sort_key,
        IReadOnlyList<int> child_afx_ids,
        string effect,
        string effect_target,
        IReadOnlyList<Tier> tiers
    );

    public record CraftingPrice(
        double @base,
        double low,
        int domain,
        double curve,
        int initial,
        int minimum
    );

    public record Effect(
        int afx_rarity,
        string rarity,
        string effect,
        string effect_target,
        string effect_size,
        double effect_delta,
        string family_effect,
        int? slots
    );

    public record Family(
        string id,
        int afx_id,
        string name,
        int afx_type,
        string type,
        int sort_key,
        IReadOnlyList<int> child_afx_ids
    );

    public record HardDependency(
        string id,
        int afx_id,
        int afx_level,
        string name,
        int tier_number,
        string tier_name,
        int afx_type,
        string type,
        string icon_filename,
        int count
    );

    public record Ingredient(
        string id,
        int afx_id,
        int afx_level,
        string name,
        int tier_number,
        string tier_name,
        int afx_type,
        string type,
        string icon_filename,
        int count
    );

    public record Recipe(
        IReadOnlyList<Ingredient> ingredients,
        CraftingPrice crafting_price
    );

    public class EiAfxDataRoot {
        public IReadOnlyList<ArtifactFamily> artifact_families;

        private static EiAfxDataRoot _instance = null;
        public static EiAfxDataRoot Instance {
            get {
                if(_instance != null) return _instance;

                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .Single(str => str.EndsWith("eiafx-data.json"));

                using var stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                _instance = JsonConvert.DeserializeObject<EiAfxDataRoot>(json);

                return _instance;
            }
        }
    }

    public record Tier(
        Family family,
        string id,
        int afx_id,
        int afx_level,
        string name,
        int tier_number,
        string tier_name,
        int afx_type,
        string type,
        string icon_filename,
        double quality,
        bool craftable,
        IReadOnlyList<double> base_crafting_prices,
        bool has_rarities,
        IReadOnlyList<int> possible_afx_rarities,
        bool has_effects,
        bool available_from_missions,
        IReadOnlyList<Effect> effects,
        Recipe recipe,
        bool ingredients_available_from_missions,
        IReadOnlyList<HardDependency> hard_dependencies
    );

}
