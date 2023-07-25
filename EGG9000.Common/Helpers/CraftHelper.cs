using Discord.Interactions;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData.EiAfxData;
using Ei;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public class RequiredIngredient {
        public Tier Tier { get; set; }
        public uint Use { get; set; }
        public uint Need { get; set; }
        
        public uint ShadowNeed { get; set; }
        
        public uint TimesCrafted { get; set; }
        
        public uint Cost { get; set; }

        public RequiredIngredient(Tier tier, uint use, uint need, uint shadowNeed, uint timesCrafted) {
            Tier = tier;
            Use = use;
            Need = need;
            ShadowNeed = shadowNeed;
            TimesCrafted = timesCrafted;
            Cost = 0;
        }

        public string GetNeed() {
            if(Tier.tier_number == 1) {
                return Need.Format();
            }

            if(Need == 0 && ShadowNeed == 0) {
                return 0.ToString();
            }

            return $"({ShadowNeed.Format()})";
        }
    }

    public class Crafter {
        private readonly EiAfxDataRoot _data;
        private readonly IList<ArtifactCount> _artifactHall;

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
        private readonly Dictionary<string, RequiredIngredient> _ingredients;
        private uint _totalCost;
        public Basket(uint cost) {
            _ingredients = new Dictionary<string, RequiredIngredient>();
            _totalCost = cost;
        }

        public void AddIngredient(Tier tier, int use, int need, uint timesCrafted, int shadowNeed = 0) {
            if(_ingredients.TryGetValue(tier.id, out var value)) {
                value.Use += (uint)use;
                value.Need += (uint)need;
                value.ShadowNeed +=  (uint)shadowNeed;
            } else {
                _ingredients.Add(tier.id, new RequiredIngredient(tier, (uint)use, (uint)need,   (uint)shadowNeed, timesCrafted));
            }
        }

        public static uint GetDiscountedPrice(double timesCrafted, double initialCost, uint shadowNeed) {
            if(timesCrafted >= 300) {
                return (uint)(initialCost - 0.9 * initialCost * Math.Pow(1, 0.2)) * shadowNeed;
            } else {
                uint cost = 0;
                for(var i = 0; i < shadowNeed; i++) {
                    cost += (uint)(initialCost - 0.9 * initialCost * Math.Pow((timesCrafted >= 300 ? 1 : (timesCrafted / 300)), 0.2));
                    timesCrafted++;
                }

                return cost;
            }
        }
        
        public static uint GetDiscountedPrice(double timesCrafted, double initialCost, int shadowNeed) {
            if(timesCrafted >= 300) {
                return (uint)((initialCost - 0.9 * initialCost * Math.Pow(1, 0.2)) * shadowNeed);
            } else {
                uint cost = 0;
                for(var i = 0; i < shadowNeed; i++) {
                    cost += (uint)(initialCost - 0.9 * initialCost * Math.Pow((timesCrafted >= 300 ? 1 : (timesCrafted / 300)), 0.2));
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

        public uint GetTotalCost() {
            return _totalCost;
        }
    }

    public enum TierInput {
        [ChoiceDisplay("T2")]
        T2 = 2,
        [ChoiceDisplay("T3")]
        T3 = 3,
        [ChoiceDisplay("T4")]
        T4 = 4
    }
}