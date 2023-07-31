using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Helpers;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData.EiAfxData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using static Ei.Backup.Types;

namespace EGG9000.Bot.Commands {
    public static class CraftCommand {
        [SlashCommand(Description = "Show you required artifacts to craft the requested artifact.", AllowInDMs = true)]
        public static async Task Craft(FauxCommand command, [SlashParam(Description = "Quantity")] int quantity, [SlashParam] TierInput quality, [SlashParam(AutocompleteHandler = typeof(EggIncArtifacts.ArtifactNameAutoComplete))] string artifact, ApplicationDbContext db, ILogger logger) {
            var requestedArtifact = EggIncArtifacts.GetEiAfxData().artifact_families.FirstOrDefault(x => x.id == artifact);

            if(requestedArtifact is null) {
                await command.RespondAsync($"Unable to locate an artifact with the name {artifact}", ephemeral: true);
                return;
            }

            await command.RespondAsync("Getting backups...", ephemeral: true);

            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var contentString = "";

            if(user.EggIncAccounts.Count == 1) {
                contentString = await CraftStringBuilder(user.EggIncAccounts.First(), quantity, quality, requestedArtifact);
                await command.DeleteOriginalResponseAsync();
                await command.Channel.SendMessageAsync(contentString);
            } else {
                var builder = new ComponentBuilder();
                foreach(var account in user.EggIncAccounts) {
                    builder.WithButton($"{account.Name} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"CraftAccountButton:{account.Id}|{((int)quality)}|{quantity}|{artifact}");
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to craft with."; x.Components = builder.Build(); });
            }

            user.UpdateAccounts();
            await db.SaveChangesAsync();
        }

        [ComponentCommand]
        public static async Task CraftAccountButton(SocketMessageComponent component, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            //await component.UpdateAsync(x => { x.Content = "Working..."; x.Components = null; });
            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            if(user is null) return;
            var dataObjs = data.Split("|");
            var account = user.EggIncAccounts.FirstOrDefault(x => x.Id == dataObjs[0]);
            var quality = (TierInput)int.Parse(dataObjs[1]);
            var quantity = int.Parse(dataObjs[2]);
            var requestedArtifact = EggIncArtifacts.GetEiAfxData().artifact_families.FirstOrDefault(x => x.id == dataObjs[3]);

            var contentString = await CraftStringBuilder(account, quantity, quality, requestedArtifact);
            await component.UpdateAsync(x => { x.Components = null; x.Content = "Success"; });
            await component.Channel.SendMessageAsync(contentString);
        }

        private async static Task<string> CraftStringBuilder(EggIncAccount account, int quantity, TierInput quality, ArtifactFamily requestedArtifact) {
            var stringBuilder = new StringBuilder();
            var backup = account.Backup;
            if(backup == null) {
                return null;
            }

            backup = new CustomBackup((await ContractsAPI.FirstContact(account.Id)).Backup);
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
            return stringBuilder.ToString();
        }
    }
}