using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData.EiAfxData;
using Microsoft.Extensions.Logging;
using System.Globalization;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Bot.Commands.ContractCommandsSlash;
using System.Collections.Generic;

namespace EGG9000.Bot.Commands {
    public static class CraftCommand {
        [SlashCommand(Description = "Show you required artifacts to craft the requested artifact.", AllowInDMs = true)]
        public static async Task Craft(FauxCommand command, [SlashParam(Description = "Quantity")] int quantity, [SlashParam] TierInput quality, [SlashParam(AutocompleteHandler = typeof(ArtifactNameAutoComplete))] string artifact, ApplicationDbContext db) {
            var requestedArtifact = EggIncArtifacts.GetEiAfxData().artifact_families.FirstOrDefault(x => x.id == artifact);

            if(requestedArtifact is null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate an artifact with the name {artifact}"), ephemeral: true);
                return;
            }

            //await command.DeferAsync(ephemeral: true);
            await command.DeferAsync();

            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            var contentString = "";

            if(dbUser.EggIncAccounts.Count == 1) {
                contentString = await CraftStringBuilder(dbUser.EggIncAccounts.First(), quantity, quality, requestedArtifact);
                //await command.DeleteOriginalResponseAsync();
                //await command.Channel.SendMessageAsync(contentString);
                await command.RespondAsync(contentString);
            } else {
                var builder = new ComponentBuilder();
                foreach(var account in dbUser.EggIncAccounts) {
                    builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"CraftAccountButton:{account.Id}|{((int)quality)}|{quantity}|{artifact}|{command.User.Id}");
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to craft with."; x.Embed = null; x.Components = builder.Build(); });
            }

            dbUser.UpdateAccounts();
            await db.SaveChangesAsync();
        }

        [ComponentCommand]
        public static async Task CraftAccountButton(SocketMessageComponent component, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {

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

        private async static Task<string> CraftStringBuilder(EggIncAccount account, int quantity, TierInput quality, ArtifactFamily requestedArtifact) {

            var baseCraftingCoefficients = new Dictionary<EggIncArtifactInstance, List<double>>() {
                { new() { Artifact = "Light of Eggendil", Tier = 4 }, new() { 0, 100, 1000.0 } },
                { new() { Artifact = "Book of Basan", Tier = 4 }, new() { 0, 150, 1000.0 } },
                { new() { Artifact = "Tachyon Deflector", Tier = 4 }, new() { 120, 500, 1200.0 } },
                { new() { Artifact = "Ship in a Bottle", Tier = 4 }, new() { 100, 400, 1200.0 } },
                { new() { Artifact = "Titanium Actuator", Tier = 4 }, new() { 0, 200, 1000.0 } },
                { new() { Artifact = "Dilithium Monocle", Tier = 4 }, new() { 0, 150, 1000.0 } },
                { new() { Artifact = "Quantum Metronome", Tier = 4 }, new() { 33, 160, 1000.0 } },
                { new() { Artifact = "Phoenix Feather", Tier = 4 }, new() { 40, 0, 1000.0 } },
                { new() { Artifact = "The Chalice", Tier = 4 }, new() { 0, 150, 1000.0 } },
                { new() { Artifact = "Interstellar Compass", Tier = 4 }, new() { 40, 200, 1000.0 } },
                { new() { Artifact = "Carved Rainstick", Tier = 4 }, new() { 0, 170, 1000.0 } },
                { new() { Artifact = "Beak of Midas", Tier = 4 }, new() { 50, 0, 1500.0 } },
                { new() { Artifact = "Mercury's Lens", Tier = 4 }, new() { 40, 250, 1000.0 } },
                { new() { Artifact = "Neodymium Medallion", Tier = 4 }, new() { 40, 200, 1000.0 } },
                { new() { Artifact = "Gusset", Tier = 4 }, new() { 0, 150, 1000.0 } },
                { new() { Artifact = "Tungsten Ankh", Tier = 4 }, new() { 40, 0, 1000.0 } },
                { new() { Artifact = "Tungsten Ankh", Tier = 3 }, new() { 40, 0, 1000.0 } },
                { new() { Artifact = "Aurelian Brooch", Tier = 4 }, new() { 40, 180, 1000.0 } },
                { new() { Artifact = "Vial of Martian Dust", Tier = 4 }, new() { 40, 0, 1000.0 } },
                { new() { Artifact = "Demeters Necklace", Tier = 4 }, new() { 40, 140, 1000.0 } },
                { new() { Artifact = "Puzzle Cube", Tier = 4 }, new() { 50, 170, 1000.0 } },
            };

            var levelMultipliers = new List<double>() {
                1.00, 1.05, 1.10,
                1.15, 1.20, 1.25,
                1.30, 1.35, 1.40,
                1.45, 1.50, 1.55,
                1.60, 1.65, 1.70,
                1.75, 1.85, 2.00,
                2.25, 2.50, 3.00,
                3.50, 4.00, 4.50,
                5.00, 6.00, 7.00,
                8.00, 9.00, 10.00
            };

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

            var coefficientPair = baseCraftingCoefficients.FirstOrDefault(a => a.Key.Artifact.ToLower() == requestedArtifact.name.ToLower() && a.Key.Tier == (int)quality);
            if(!coefficientPair.Equals(default(KeyValuePair<EggIncArtifactInstance, List<double>>))) {
                var keyAf = coefficientPair.Key;
                var numCrafted = backup.ArtifactHall.Where(a => a.NumberCrafted > 0).FirstOrDefault(a => a.Artifact.Artifact == keyAf.Artifact && a.Artifact.Tier == keyAf.Tier)?.NumberCrafted ?? 0;
                var numCraftedScalar = Math.Min(1.0, (double)(numCrafted / 400.0));
                var craftingLevel = backup.GetCraftingLevel();
                var craftingScalar = levelMultipliers[(int)craftingLevel - 1];

                var baseRates = coefficientPair.Value;

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
                var fixedCommonThresh = (1 - fixedLegThresh - fixedEpicThresh - fixedRareThresh);

                var dispCommon = fixedCommonThresh * 100;
                var dispRare = fixedRareThresh * 100;
                var dispEpic = fixedEpicThresh * 100;
                var dispLeg = fixedLegThresh * 100;

                var percentageStrs = new List<string>();

                if(fixedRareThresh > 0 || fixedEpicThresh > 0 || fixedLegThresh > 0) {
                    stringBuilder.AppendLine("\n\n**Your next craft has the following percentage breakdown**:");
                    //if(fixedCommonThresh > 0) percentageSb.Append($"{dispCommon:f2}% - Common");
                    if(fixedRareThresh > 0) percentageStrs.Add($"{dispRare:f2}% <:Rare:905959988030226453>");
                    if(fixedEpicThresh > 0) percentageStrs.Add($"{dispEpic:f2}% <:Epic:905960149720649748>");
                    if(fixedLegThresh > 0) percentageStrs.Add($"{dispLeg:f2}% <:Legendary:905960165860339722>");

                    stringBuilder.AppendLine(string.Join(" | ", percentageStrs));
                }
            }


            return stringBuilder.ToString();
        }
    }
}