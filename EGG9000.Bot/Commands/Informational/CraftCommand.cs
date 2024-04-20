using Discord;
using Discord.WebSocket;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiAfxConfig;
using EGG9000.Common.JsonData.EiAfxData;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.ArtifactHelpers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static Ei.ArtifactSpec.Types;

namespace EGG9000.Bot.Commands {
    public static class CraftCommand {
        [SlashCommand(Description = "Show you required artifacts to craft the requested artifact.", AllowInDMs = true)]
        public static async Task Craft(FauxCommand command, [SlashParam(Description = "Quantity")] int quantity, [SlashParam] TierInput quality, [SlashParam(AutocompleteHandler = typeof(ArtifactNameAutoComplete))] string artifact, ApplicationDbContext db) {
            var requestedArtifact = EggIncArtifacts.GetEiAfxData().artifact_families.FirstOrDefault(x => x.id == artifact);

            if(requestedArtifact is null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate an artifact with the name {artifact}"), ephemeral: true);
                return;
            }

            await command.DeferAsync();

            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            if(dbUser.EggIncAccounts.Count == 1) {
                await command.RespondAsync(
                    await CraftStringBuilder(dbUser.EggIncAccounts.First(), quantity, quality, requestedArtifact)
                );
            } else {
                var builder = new ComponentBuilder();
                foreach(var account in dbUser.EggIncAccounts) {
                    builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"CraftAccountButton:{account.Id}|{(int)quality}|{quantity}|{artifact}|{command.User.Id}");
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to craft with."; x.Embed = null; x.Components = builder.Build(); });
            }

            dbUser.UpdateAccounts();
            await db.SaveChangesAsync();
        }

        [ComponentCommand]
        public static async Task CraftAccountButton(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {

            var dataObjs = data.Split("|");
            var originalUserId = ulong.Parse(dataObjs[4]);

            if(component.User.Id != originalUserId) {
                await component.RespondAsync(embed: EmbedError("This wasn't yours to run - don't click others' commands!"), ephemeral: true);
                return;
            }

            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            if(user is null) return;
            var account = user.EggIncAccounts.FirstOrDefault(x => x.Id == dataObjs[0]);
            var quality = (TierInput)int.Parse(dataObjs[1]);
            var quantity = int.Parse(dataObjs[2]);
            var requestedArtifact = EggIncArtifacts.GetEiAfxData().artifact_families.FirstOrDefault(x => x.id == dataObjs[3]);

            var contentString = await CraftStringBuilder(account, quantity, quality, requestedArtifact);
            //await component.UpdateAsync(x => { x.Components = null; x.Content = "Success"; });
            //await component.Channel.SendMessageAsync(contentString);
            await component.UpdateAsync(x => { x.Components = null; x.Content = contentString; });
        }

        private static async Task<string> CraftStringBuilder(EggIncAccount account, int quantity, TierInput quality, ArtifactFamily requestedArtifact) {
            var stringBuilder = new StringBuilder();
            var backup = account.Backup;
            if(backup == null) {
                return null;
            }

            backup = new CustomBackup((await ContractsAPI.FirstContact(account.Id)).Backup, backup);
            stringBuilder.Append($"For **{(string.IsNullOrWhiteSpace(backup.UserName) ? $"Blank account with {backup.EarningsBonus.ToEggString()} EB" : backup.UserName)}** to craft {quantity} T{(int)quality} {requestedArtifact.id}:");
            stringBuilder.AppendLine();

            var crafter = new Crafter(backup.ArtifactHall);
            var basket = crafter.GetCraft(quantity, (int)quality, requestedArtifact.id);

            stringBuilder.AppendFormat($"```{"Name",-20}{"Using",-8}{"Need",-8}{"Cost",-8}");
            stringBuilder.AppendLine();
            stringBuilder.Append("―――――――――――――――――――――――――――――――――――――――――――");
            stringBuilder.AppendLine();

            var ingredients = from kvp in basket.GetIngredients()
                              orderby EggIncArtifacts.GetFamilyShorthand(kvp.Value.Tier.family) ascending, kvp.Value.Tier.tier_number descending
                              select kvp;
            foreach(var ingredient in ingredients) {
                stringBuilder.AppendFormat($"{$"T{ingredient.Value.Tier.tier_number} {EggIncArtifacts.GetFamilyShorthand(ingredient.Value.Tier.family)}",-20}");
                stringBuilder.AppendFormat($"{ingredient.Value.Use.Format(),-8}");
                stringBuilder.AppendFormat($"{ingredient.Value.GetNeed(),-8}");
                stringBuilder.AppendFormat($"{ingredient.Value.Cost.Format(),-8}");
                stringBuilder.AppendLine();
            }

            stringBuilder.AppendLine("```");
            stringBuilder.Append($"Total Cost: **{basket.GetTotalCost().ToString("#,0", new CultureInfo("en-US"))} GE**");
            stringBuilder.AppendLine();
            var goldenEggs = backup.GoldenEggsEarned - backup.GoldenEggsSpent;
            stringBuilder.Append(goldenEggs >= basket.GetTotalCost() ? "You have enough GE!" : "You do not have enough GE!");

            var baseCraftingCoefficients = Root.Get().baseCraftingCoefficients;
            var coefficientPair = baseCraftingCoefficients.FirstOrDefault(a => a.Key.Artifact.ToLower() == requestedArtifact.name.ToLower() && a.Key.Tier == (int)quality);
            if(!coefficientPair.Equals(default(KeyValuePair<EggIncArtifactInstance, List<double>>))) {
                var secondStringBuilder = new StringBuilder();
                var keyAf = coefficientPair.Key;
                var numCrafted = backup.ArtifactHall.Where(a => a.NumberCrafted > 0).FirstOrDefault(a => a.Artifact.Artifact == keyAf.Artifact && a.Artifact.Tier == keyAf.Tier)?.NumberCrafted ?? 0;
                var baseRates = coefficientPair.Value;
                var percentages = GetCraftPercentages(numCrafted, backup.GetCraftingLevel(), baseRates);
                var percentageStrs = new List<string>();
                if(percentages[Rarity.Rare][0] > 0 || percentages[Rarity.Epic][0] > 0 || percentages[Rarity.Legendary][0] > 0) {
                    secondStringBuilder.AppendLine("\n\n**Your next craft has the following percentage breakdown**:");
                    if(percentages[Rarity.Rare][0] > 0) percentageStrs.Add($"{percentages[Rarity.Rare][1]:f2}% <:Rare:905959988030226453>");
                    if(percentages[Rarity.Epic][0] > 0) percentageStrs.Add($"{percentages[Rarity.Epic][1]:f2}% <:Epic:905960149720649748>");
                    if(percentages[Rarity.Legendary][0] > 0) percentageStrs.Add($"{percentages[Rarity.Legendary][1]:f2}% <:Legendary:905960165860339722>");
                    secondStringBuilder.AppendLine(string.Join(" | ", percentageStrs));
                }

                if(quantity > 1) {
                    var secondNumCrafted = (uint)(numCrafted + quantity);
                    var secondPercentages = GetCraftPercentages(secondNumCrafted, backup.GetCraftingLevel(), baseRates);
                    var secondPercentageStrs = new List<string>();

                    var quantityCraftText = "";
                    if(quantity >= 11 && quantity <= 13) {
                        switch(quantity) {
                            case 11: quantityCraftText = "11th"; break;
                            case 12: quantityCraftText = "12th"; break;
                            case 13: quantityCraftText = "13th"; break;
                        }
                    } else {
                        switch(quantity.ToString().Last()) {
                            case '1': quantityCraftText = $"{quantity}st"; break;
                            case '2': quantityCraftText = $"{quantity}nd"; break;
                            case '3': quantityCraftText = $"{quantity}rd"; break;
                            case '4':
                            case '5':
                            case '6':
                            case '7':
                            case '8':
                            case '9':
                            case '0': quantityCraftText = $"{quantity}th"; break;
                        }
                    }

                    if(secondPercentages[Rarity.Rare][0] > 0 || secondPercentages[Rarity.Epic][0] > 0 || secondPercentages[Rarity.Legendary][0] > 0) {
                        secondStringBuilder.AppendLine($"\n**Your {quantityCraftText} craft from now has the following percentage breakdown**:");
                        if(secondPercentages[Rarity.Rare][0] > 0) secondPercentageStrs.Add($"{secondPercentages[Rarity.Rare][1]:f2}% <:Rare:905959988030226453>");
                        if(secondPercentages[Rarity.Epic][0] > 0) secondPercentageStrs.Add($"{secondPercentages[Rarity.Epic][1]:f2}% <:Epic:905960149720649748>");
                        if(secondPercentages[Rarity.Legendary][0] > 0) secondPercentageStrs.Add($"{secondPercentages[Rarity.Legendary][1]:f2}% <:Legendary:905960165860339722>");
                        secondStringBuilder.AppendLine(string.Join(" | ", secondPercentageStrs));
                    }
                }

                if(stringBuilder.Length + secondStringBuilder.Length < 2000) {
                    stringBuilder.Append(secondStringBuilder);
                } else {
                    stringBuilder.AppendLine("\n**(Message too long to show rarity chances)**");
                }
            }


            return stringBuilder.ToString();
        }

        private static Dictionary<Rarity, List<double>> GetCraftPercentages(uint numCrafted, uint craftingLevel, List<double> baseRates) {
            var numCraftedScalar = Math.Min(1.0, (double)(numCrafted / 400.0));
            var craftingScalar = Root.Get().craftingLevelMultipliers[(int)craftingLevel - 1];

            var baseRareRate = baseRates[0];
            var baseEpicRate = baseRates[1];
            var baseLegRate = baseRates[2];

            var scaledRareRate = Math.Max(10.0, baseRareRate / craftingScalar);
            var scaledEpicRate = Math.Max(10.0, baseEpicRate / craftingScalar);
            var scaledLegRate = Math.Max(10.0, baseLegRate / craftingScalar);

            var rareThreshold = (baseRareRate > 0) ? Math.Min(0.1, Math.Pow(1.0 / scaledRareRate, 1.0 - numCraftedScalar * 0.3)) : 0.0;
            var epicThreshold = (baseEpicRate > 0) ? Math.Min(0.1, Math.Pow(1.0 / scaledEpicRate, 1.0 - numCraftedScalar * 0.3)) : 0.0;
            var legThreshold = (baseLegRate > 0) ? Math.Min(0.1, Math.Pow(1.0 / scaledLegRate, 1.0 - numCraftedScalar * 0.3)) : 0.0;

            var fixedLegThresh = legThreshold;
            var fixedEpicThresh = Math.Max(0, epicThreshold - fixedLegThresh);
            var fixedRareThresh = Math.Max(0, rareThreshold - epicThreshold);
            var fixedCommonThresh = 1 - fixedLegThresh - fixedEpicThresh - fixedRareThresh;

            var dispCommon = fixedCommonThresh * 100;
            var dispRare = fixedRareThresh * 100;
            var dispEpic = fixedEpicThresh * 100;
            var dispLeg = fixedLegThresh * 100;

            return new() {
                { Rarity.Common , [fixedCommonThresh, dispCommon] },
                { Rarity.Rare, [fixedRareThresh, dispRare] },
                { Rarity.Epic, [fixedEpicThresh, dispEpic] },
                { Rarity.Legendary, [fixedLegThresh, dispLeg] },
            };
        }
    }
}