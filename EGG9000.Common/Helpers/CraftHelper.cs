using Discord.Interactions;
using EGG9000.Common.JsonData.EiAfxData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    
    public class RequiredIngredient {
        public Tier Tier { get; set; }
        public int Use { get; set; }
        public int Need { get; set; }

        public RequiredIngredient(Tier tier, int use, int need) {
            Tier = tier;
            Use = use;
            Need = need;
        }
    }
    
    public class Crafter {
        private EiAfxDataRoot _data;

        public Crafter() {
            _data = EggIncArtifacts.GetEiAfxData();
        }

        public Dictionary<string, RequiredIngredient>
            GetCraft(int howMany, int tierNumber, string artifactFamilyId) {
            var artifactFamily = _data.artifact_families.First(af => af.id == artifactFamilyId);
            var artifactTier = artifactFamily?.tiers.First(t => t.tier_number == tierNumber);

            if(artifactTier?.recipe is not null) {
                var basket = new Dictionary<string, RequiredIngredient>();
                    GetIngredients(basket, artifactTier.recipe, howMany);
                    return basket;
            } else {
                throw new ArgumentException("Tier number is incorrect!", nameof(tierNumber));
            }
        }

        private int HowManyArtifactsInInventory(string ingredientId, int ingredientTierNumber) {
            //TODO CHECK INVENTORY (also have to save previous request in case we need some artifacts more than one time)
            return 0;
        }


        static int GetDiscountedPrice(double timesCrafted, double initialCost) {
            return (int)(initialCost - 0.9 * initialCost * Math.Pow(timesCrafted / 300, 0.2));
        }

        private int CheckInventory(Ingredient ingredient) {
            return 0;
        }
        
        private void GetIngredients(IDictionary<string, RequiredIngredient> basket, Recipe recipe, int quantity) {
            foreach(var ingredient in recipe.ingredients) {
                var tier = EggIncArtifacts.GetTier(ingredient.afx_id, ingredient.tier_number);
                if(tier.recipe is null) {
                    if(basket.TryGetValue(tier.id, out var value)) {
                        value.Need += quantity * ingredient.count;
                    } else {
                        basket.Add(tier.id, new RequiredIngredient(tier,0,quantity * ingredient.count));
                    }
                } else {
                    GetIngredients(basket, tier.recipe, ingredient.count * quantity);
                }
            }
        }
        
        private void IngredientRecursion(IDictionary<string, (Ingredient, int, int)> ingredients, Ingredient ingredient,
            int count) {
            var hmaii = HowManyArtifactsInInventory(ingredient.id, ingredient.tier_number);
            var difference = count * ingredient.count - hmaii;

            if(difference <= 0) {
                if(ingredients.ContainsKey(ingredient.name)) {
                    var tuple = ingredients[ingredient.name];
                    ingredients[ingredient.name] = (tuple.Item1, tuple.Item2 + ingredient.count, tuple.Item3 + 0);
                } else {
                    ingredients.Add(ingredient.name, (ingredient, ingredient.count, 0));
                }

                return;
            }

            if(ingredient.afx_level == 0) {
                if(ingredients.ContainsKey(ingredient.name)) {
                    var tuple = ingredients[ingredient.name];
                    ingredients[ingredient.name] = (tuple.Item1, tuple.Item2 + hmaii, tuple.Item3 + difference);
                } else {
                    ingredients.Add(ingredient.name, (ingredient, hmaii, difference));
                }

                return;
            }

            if(hmaii != 0) {
                if(ingredients.ContainsKey(ingredient.name)) {
                    var tuple = ingredients[ingredient.name];
                    ingredients[ingredient.name] = (tuple.Item1, tuple.Item2 + hmaii, tuple.Item3 + 0);
                } else {
                    ingredients.Add(ingredient.name, (ingredient, hmaii, 0));
                }
            }


            var recipe = _data.artifact_families.First(af => af.afx_id == ingredient.afx_id)?.tiers
                .First(t => t.afx_level == ingredient.afx_level)?.recipe;

            if(recipe is null) {
                Environment.Exit(1);
            }

            foreach(var i in recipe.ingredients) {
                IngredientRecursion(ingredients, i, difference);
            }
        }

        public static void Print(Dictionary<string, (Ingredient, int, int)> ingredients) {
            Console.WriteLine($"{"Name",-50} {"Using",-30} {"Need",-30}");
            foreach(var ingredient in ingredients) {
                Console.WriteLine(
                    $"{ingredient.Value.Item1.name,-50} {ingredient.Value.Item2,-30} {ingredient.Value.Item3,-30}");
            }
        }
    }
}