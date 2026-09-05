using Discord;
using Discord.Interactions;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.JsonData.EiAfxConfig;
using Ei;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.ArtifactHelpers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static Ei.MissionInfo.Types;

namespace EGG9000.Bot.Commands.Informational {
    [Group("formulae", "Game formula calculators (MER, LLC, EB)")]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
    public class FormulaeModule(IDbContextFactory<ApplicationDbContext> dbFactory, IMemoryCache cache, ILogger<FormulaeModule> logger) : E9KModuleBase(dbFactory) {
        private readonly IMemoryCache _cache = cache;
        private readonly ILogger<FormulaeModule> _logger = logger;

        public enum MERChoice {
            [ChoiceDisplay("Current")] Current = 0,
            [ChoiceDisplay("30")] Thirty = 30,
            [ChoiceDisplay("40")] Forty = 40,
            [ChoiceDisplay("50")] Fifty = 50
        };

        [SlashCommand("mer", "Calculate your Mystical Egg Ratio (MER)")]
        public async Task Mer([Summary("mervalue")] MERChoice MERValue = MERChoice.Current) {
            await Context.Interaction.RespondAsyncGettingMessage("Getting account backups...");
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"); });
                return;
            } else if(!dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to retrieve your backup. Please try again later."); });
                return;
            }

            var embeds = new List<Embed>();
            foreach(var account in dbUser.EggIncAccounts.Where(x => x.Backup is not null)) {
                var embed = MERCalculate(account, (int)MERValue);
                var newBuilder = embed.ToEmbedBuilder();
                newBuilder.Title = $"**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})** {(string.IsNullOrWhiteSpace(embed.Title) ? "" : " ")}" + embed.Title;
                embed = newBuilder.Build();
                embeds.Add(embed);
            }

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embeds.ToArray(); });
        }

        private static Embed MERCalculate(EggIncAccount account, int MERValue) {
            var pe = account.Backup.EggsOfProphecy;
            var seQ = account.Backup.SoulEggs / 1e18;
            var MER = account.Backup.MER;

            var MERgoal = MERValue != 0 ? MERValue : Math.Max(30, Math.Min(50, (long)Math.Round(MER / 10) * 10));
            var description = $"<:Egg_of_Prophecy_PE:669981330477547580>`{pe}` & <:Soul_Egg_SE:724341890794913964>`{account.Backup.SoulEggs.ToEggString()}`";

            if(MERgoal > MER) {
                var MERse = Math.Pow(10, (10 * MERgoal - 200 + pe) / 91.0) * 1e18 - account.Backup.SoulEggs;
                description += $"\nAn additional <:Soul_Egg_SE:724341890794913964>`{MERse.ToEggString()}` is needed for MER {MERgoal}";
            } else {
                var MERpe = (-10 * MERgoal) + (91 * Math.Log10(seQ)) + 200 - pe;
                description += $"\nYou can maintain MER {MERgoal} for another <:Egg_of_Prophecy_PE:669981330477547580>`{MERpe:n0}`";
            }

            return new EmbedBuilder()
                .WithTitle($"`{MER}`")
                .WithColor(Color.Gold)
                .WithDescription(description)
                .WithAuthor(new EmbedAuthorBuilder()
                .WithName("Mystical Egg Ratio")
                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
                .Build();
        }

        private class ShipData(string type, string duration, int level, double legendaryDropRate) {
            public string Type { get; set; } = type;
            public string Duration { get; set; } = duration;
            public int Level { get; set; } = level;
            public double LegendaryDropRate { get; set; } = legendaryDropRate;
        }

        [SlashCommand("llc", "Calculate your Legendary Luck Coefficient (LLC)")]
        public async Task Llc() {
            await Context.Interaction.DeferAsync();
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"); });
                return;
            } else if(!dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to retrieve your backup. Please try again later."); });
                return;
            }

            var embeds = new List<Embed>();
            foreach(var account in dbUser.EggIncAccounts.Where(x => x.Backup is not null)) {
                var embed = await LLCCalculate(account, dbUser.DiscordUsername, _cache, _logger);
                var newBuilder = embed.ToEmbedBuilder();
                newBuilder.Title = $"**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})** {(string.IsNullOrWhiteSpace(embed.Title) ? "" : " ")}" + embed.Title;
                embed = newBuilder.Build();
                embeds.Add(embed);
            }

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embeds.ToArray(); });
        }

        private static async Task<Embed> LLCCalculate(EggIncAccount account, string userName, IMemoryCache _cache, ILogger _logger) {
            var backup = await EggIncApi.FirstContact(account.Id);

            if(backup?.Backup?.ArtifactsDb?.MissionArchive is null || account?.Backup?.ArtifactHall is null) {
                return EmbedError($"Unable to retrieve backup, please try again later.");
            }

            var shipCoefficientTable = await GetShipDataTable(_cache, _logger);

            if(shipCoefficientTable is null) {
                return EmbedError($"Ship coefficients were not cached, and Menno's API did not respond to refresh them. Please try again later.");
            }

            var llc = GetLegendaryLuckCoefficient(account, backup.Backup, shipCoefficientTable);

            var (linerEpicCount, henEpicCount) = GetCompletedShipsOfDuration(account, DurationType.Epic);
            var (linerLongCount, henLongCount) = GetCompletedShipsOfDuration(account, DurationType.Long);
            var (linerShortCount, henShortCount) = GetCompletedShipsOfDuration(account, DurationType.Short);

            var newDisplayPercent = llc.LLCPercent == int.MinValue ? "-∞" : $"{llc.LLCPercent}%";
            var description = $"\n:tools: **Possible <:leggy:1113516502516248636> crafts** `{llc.PossibleCraftCount}`" +
                $"\n<:Henerprise:801748924146384906> **Henerprises** `{henEpicCount}` extended / `{henLongCount}` standard / `{henShortCount}` short" +
                $"\n<:Atreggies:1215022229826314380> **Atreggies** `{linerEpicCount}` extended / `{linerLongCount}` standard / `{linerShortCount}` short" +
                $"\n<:leggy:1113516502516248636> **Legendaries** `{Math.Round(llc.ExpectedLeggies, 2)}` expected / `{llc.LegCount}` acquired";

            return new EmbedBuilder()
                .WithTitle($"`{llc.LLC:f2}` (`{newDisplayPercent}`)")
                .WithColor(Color.DarkBlue)
                .WithDescription(description)
                .WithAuthor(new EmbedAuthorBuilder()
                .WithName("Legendary Luck Coefficient")
                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
                .Build();
        }

        [SlashCommand("eb", "Calculate the EB% based on SE and PE inputs")]
        public async Task Eb([Summary("se", "SE")] string SE, [Summary("pe", "PE")][MinValue(0)] int PE) {
            await Context.Interaction.RespondAsyncGettingMessage("Calculating...");

            double seValue;
            var parserDict = new Dictionary<string, double>() {
                {"K", 1e3},
                {"M", 1e6},
                {"B", 1e9},
                {"T", 1e12},
                {"q", 1e15},
                {"Q", 1e18},
                {"s", 1e21},
                {"S", 1e24}
            };

            if(parserDict.TryGetValue(SE.Last().ToString(), out var mult)) {
                seValue = double.Parse(SE.TrimEnd(SE.Last())) * mult;
            } else {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"Invalid SE value: must end with {string.Join(", ", parserDict.Keys.ToList())}."));
                return;
            }

            if(PE <= 0 || PE > 1000) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError("Invalid PE value: must be a positive integer less than 1000."));
                return;
            }

            var result = (seValue * 1.5) * Math.Pow(1.1, PE);
            var resultPercentage = result * 100;
            var bonus = Math.Round(Math.Pow((1.05 + 0.01 * 5), PE) * (1.5) * 100, 2);

            await Context.Interaction.ModifyOriginalResponseAsync($"{SIPrefix.GetPrefixFromEB(resultPercentage).RankWithSubRank} (<:Soul_Egg_SE:724341890794913964>`{SE}` and <:Egg_of_Prophecy_PE:669981330477547580>`{PE}`)\nEarning Bonus %: `{resultPercentage.ToEggString(true, 2)}%`\nEarning multiplier: `{result.ToEggString(true, 2)}`\nBonus per soul egg: `{bonus:n}%`");
        }
    }
}
