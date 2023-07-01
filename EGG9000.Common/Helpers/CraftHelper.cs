using Discord.Interactions;
using EGG9000.Common.JsonData.EiAfxData;
using Ei;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public class RequiredIngredient {
        public Tier Tier { get; set; }
        public int Use { get; set; }
        public int Need { get; set; }
        
        public int ShadowNeed { get; set; }
        
        public uint TimesCrafted { get; set; }
        
        public int Cost { get; set; }

        public RequiredIngredient(Tier tier, int use, int need, int shadowNeed, uint timesCrafted) {
            Tier = tier;
            Use = use;
            Need = need;
            ShadowNeed = shadowNeed;
            TimesCrafted = timesCrafted;
            Cost = 0;
        }

        public string GetUse() {
            if(Tier.tier_number == 1) {
                return Need.ToString();
            } else if(Need == 0 && ShadowNeed == 0) {
                return 0.ToString();
            } else {
                return $"({ShadowNeed.ToString()})";
            }
        }
    }

    public class Crafter {
        private EiAfxDataRoot _data;
        private IList<ArtifactCount> _artifactHall;

        public Crafter(IList<ArtifactCount> artifactHall) {
            _data = EggIncArtifacts.GetEiAfxData();
            _artifactHall = artifactHall;
        }

        public Basket GetCraft(int howMany, int tierNumber, string artifactFamilyId) {
            var artifactFamily = _data.artifact_families.First(af => af.id == artifactFamilyId);
            if(artifactFamily.type.Equals("Ingredient", StringComparison.OrdinalIgnoreCase) && tierNumber == 4) {
                tierNumber = 3;
            }
            var artifactTier = artifactFamily?.tiers.First(t => t.tier_number == tierNumber);

            if(artifactTier?.recipe is not null) {
                uint timesCrafted = 0;
                CheckInventory(artifactTier, 0, ref timesCrafted);
                var cost = Basket.GetDiscountedPrice(timesCrafted, artifactTier.recipe.crafting_price.initial, howMany);
                var basket = new Basket(cost);
                GetIngredients(basket, artifactTier.recipe, howMany);
                basket.ComputeCosts();
                return basket;
            } else {
                throw new ArgumentException("Tier number is incorrect!", nameof(tierNumber));
            }
        }
        
        static int GetDiscountedPrice(double timesCrafted, double initialCost) {
            return (int)(initialCost - 0.9 * initialCost * Math.Pow(timesCrafted / 300, 0.2));
        }

        private int CheckInventory(Tier tier, int quantity, ref uint timesCrafted) {

            var artifactCount = _artifactHall.Where(ac => ac.Artifact.Rarity == 1).FirstOrDefault(ac => EggIncArtifacts.GetTierName(ac.Artifact) == tier.name);

            if(artifactCount is null) {
                return 0;
            }
            
            timesCrafted = artifactCount.NumberCrafted;
            
            if(artifactCount.Count == 0) {
                return 0;
            }

            if(artifactCount.Count >= quantity) {
                artifactCount.Count -= quantity;
                return quantity;
            } else {
                var count = artifactCount.Count;
                artifactCount.Count = 0;
                return count;
            }
        }

        private void GetIngredients(Basket basket, Recipe recipe, int quantity) {
            foreach(var ingredient in recipe.ingredients) {
                var tier = EggIncArtifacts.GetTier(ingredient.afx_id, ingredient.tier_number);
                uint timesCrafted = 0;
                var count = CheckInventory(tier, quantity * ingredient.count, ref timesCrafted);
                if(count != 0) {
                    basket.AddIngredient(tier, count, 0, timesCrafted);
                }

                var difference = ingredient.count * quantity - count;
                if(tier.recipe is null) {
                    if(difference != 0) {
                        basket.AddIngredient(tier, 0, difference, timesCrafted);
                    }
                } else {
                    if(difference != 0) {
                        basket.AddIngredient(tier, 0, 0, timesCrafted, difference);
                        GetIngredients(basket, tier.recipe, difference);
                    }
                }
            }
        }
    }

    public class Basket {
        private Dictionary<string, RequiredIngredient> _ingredients;
        private int _totalCost;

        public Basket(int cost) {
            _ingredients = new Dictionary<string, RequiredIngredient>();
            _totalCost = cost;
        }

        public void AddIngredient(Tier tier, int use, int need, uint timesCrafted, int shadowNeed = 0) {
            if(_ingredients.TryGetValue(tier.id, out var value)) {
                value.Use += use;
                value.Need += need;
                value.ShadowNeed += shadowNeed;
            } else {
                _ingredients.Add(tier.id, new RequiredIngredient(tier, use, need,  shadowNeed, timesCrafted));
            }
        }

        public uint Fx(uint x) {
            return x > 300 ? 300 : x;
        }
        
        public static int GetDiscountedPrice(double timesCrafted, double initialCost, int ShadowNeed) {
            if(timesCrafted >= 300) {
                return (int)(initialCost - 0.9 * initialCost * Math.Pow(1, 0.2)) * ShadowNeed;
            } else {
                var cost = 0;
                for(var i = 0; i < ShadowNeed; i++) {
                    cost += (int)(initialCost - 0.9 * initialCost * Math.Pow((timesCrafted >= 300 ? 1 : (timesCrafted / 300)), 0.2));
                    timesCrafted++;
                }

                return cost;
            }
        }

        public void ComputeCosts() {
            foreach(var ingredient in _ingredients) {
                if(ingredient.Value.Tier.recipe is not null) {
                    ingredient.Value.Cost = GetDiscountedPrice(ingredient.Value.TimesCrafted, ingredient.Value.Tier.recipe.crafting_price.initial, ingredient.Value.ShadowNeed);
                    _totalCost += ingredient.Value.Cost;
                }
            }
        }

        public Dictionary<string, RequiredIngredient> GetIngredients() {
            return _ingredients;
        }

        public int GetTotalCost() {
            return _totalCost;
        }
    }
}