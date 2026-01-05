using Discord;
using EGG9000.Bot.Helpers;
using EGG9000.Common.API;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiAfxConfig;
using EGG9000.Common.Services;
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
    public static class ForumlaCommands {
        public enum MERChoice {
            [Discord.Interactions.ChoiceDisplay("Current")] Current = 0,
            //[Discord.Interactions.ChoiceDisplay("20")] Twenty = 20,
            [Discord.Interactions.ChoiceDisplay("30")] Thirty = 30,
            [Discord.Interactions.ChoiceDisplay("40")] Forty = 40,
            [Discord.Interactions.ChoiceDisplay("50")] Fifty = 50
        };

        [SlashCommand(Description = "Calculate your Mystical Egg Ratio (MER)", ParentCommand = "formulae", AllowInDMs = true)]
        public static async Task Mer(FauxCommand command, ApplicationDbContext db, [SlashParam(Required = false)] MERChoice MERValue = MERChoice.Current) {
            await command.RespondAsync("Getting account backups...");
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            } else if(!dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to retrieve your backup. Please try again later."); });
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

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embeds.ToArray(); });
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

        private class ShipsSent {
            public Dictionary<(Spaceship, DurationType, uint), int> ShipCounts { get; private set; }

            public ShipsSent(Backup backup) {
                ShipCounts = [];

                if(backup?.ArtifactsDb?.MissionArchive is not null) {
                    foreach(var mission in backup.ArtifactsDb.MissionArchive) {
                        var key = (mission.Ship, mission.DurationType, mission.Level);
                        if(ShipCounts.ContainsKey(key)) {
                            ShipCounts[key]++;
                        } else {
                            ShipCounts[key] = 1;
                        }
                    }
                }
            }

            public int GetShipsCount(Spaceship shipType, DurationType durationType, uint level) {
                var key = (shipType, durationType, level);
                return ShipCounts.TryGetValue(key, out var count) ? count : 0;
            }
        }

        [SlashCommand(Description = "Calculate your Legendary Luck Coefficient (LLC)", ParentCommand = "formulae", AllowInDMs = true)]
        public static async Task Llc(FauxCommand command, ApplicationDbContext db, IMemoryCache _cache, ILogger _logger) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            } else if(!dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to retrieve your backup. Please try again later."); });
                return;
            }

            var embeds = new List<Embed>();
            foreach(var account in dbUser.EggIncAccounts.Where(x => x.Backup is not null)) {
                var embed = await LLCCalculate(account, dbUser.DiscordUsername, _cache, _logger);
                var newBuilder = embed.ToEmbedBuilder();
                newBuilder.Title = $"**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})** {(string.IsNullOrWhiteSpace(embed.Title) ? "" : " " )}" + embed.Title;
                embed = newBuilder.Build();
                embeds.Add(embed);
            }
            
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embeds = embeds.ToArray(); });
        }

        public static (int, int) GetCompledShipsOfDuration(EggIncAccount account, DurationType duration) {
            var maxShipLevels = MissionHelpers.MaxShipLevels.ToList();
            var lastShipType = maxShipLevels[^1].Key;
            var secondToLastShipType = maxShipLevels[^2].Key;

            var shipsForLastType = account.Backup.ShipsSent
                .Where(x => x.ship == lastShipType && x.type == duration)
                .Sum(x => x.count);
            var exploringShipsLastType = account.Backup.SpaceMissions
                .Where(x => x.Ship == lastShipType && x.Status == Status.Exploring && x.Duration == duration)
                .Count();
            var resultLastType = shipsForLastType - exploringShipsLastType;

            var shipsForSecondToLastType = account.Backup.ShipsSent
                .Where(x => x.ship == secondToLastShipType && x.type == duration)
                .Sum(x => x.count);
            var exploringShipsSecondToLastType = account.Backup.SpaceMissions
                .Where(x => x.Ship == secondToLastShipType && x.Status == Status.Exploring && x.Duration == duration)
                .Count();
            var resultSecondToLastType = shipsForSecondToLastType - exploringShipsSecondToLastType;

            return (resultLastType, resultSecondToLastType);
        }

        [ComponentCommand]
        private static async Task<Embed> LLCCalculate(EggIncAccount account, string userName, IMemoryCache _cache, ILogger _logger) {
            var backup = await EggIncAPI.FirstContact(account.Id);

            if(backup?.Backup?.ArtifactsDb?.MissionArchive is null || account?.Backup?.ArtifactHall is null) {
                return EmbedError($"Unable to retrieve backup, please try again later.");
            }

            var shipCoefficientTable = await GetShipDataTable(_cache, _logger);
            var baseCraftingCoefficients = Root.Get().baseCraftingCoefficients;

            //Catch the case where the cache is invalidated, and the API returns an error
            if(shipCoefficientTable is null) {
                return EmbedError($"Ship coefficients were not cached, and Menno's API did not respond to refresh them. Please try again later.");
            }

            var shipsSent = new ShipsSent(backup.Backup);

            var sumOfRatios = 0.0;
            foreach(var (ship, type, dropRates) in shipCoefficientTable) {
                var rateIndex = 0;
                foreach(var rate in dropRates) {
                    if(rate == 0.0) {
                        rateIndex++;
                        continue;
                    }
                    sumOfRatios += shipsSent.GetShipsCount(ship, type, (uint)rateIndex) / rate;
                    rateIndex++;
                }
            }

            var afHall = account.Backup.ArtifactHall;
            var newLLCSum = 0.0;
            foreach(var craftType in baseCraftingCoefficients.Where(c => c.Value[2] != 0)) {

                //Don't account for Lunar totems in LLC calc, re: sync with Menno data
                if(craftType.Key.Artifact == "Lunar Totem" && craftType.Key.Tier == 4) continue;

                //Get the number of crafts that have been performed for this artifact
                var numCrafted = (double)(afHall.Where(a => a.NumberCrafted > 0).FirstOrDefault(a => a.Artifact.Tier == craftType.Key.Tier && a.Artifact.Artifact == craftType.Key.Artifact)?.NumberCrafted ?? 0.0);
                if(numCrafted == 0) continue;
                //Calculate an assumed XP per craft
                var assumedXpPerCraft = account.Backup.CraftingXP / numCrafted;
                //Sum up total legendary coefficients
                for(var i = 0; i < numCrafted; i++) {

                    //Calculate "repetitive craft" scalar
                    var craftingCountCoefficient = Math.Min(1.0, (double)(i / 400.0));
                    var fixedCraftingCountCoefficient = 1.0 - craftingCountCoefficient * 0.3; //Where these numbers come from, I have no idea

                    //Get the base Legendary rate for the artifact in question
                    var baseLegRate = craftType.Value[2];

                    //Calculate "assumed" or simulated crafting level
                    var simulatedCraftingLevel = GetCraftingLevel(assumedXpPerCraft * i);
                    //Retrieve the multiplier offset for this level (-1 due to returning "display value" vs. index)
                    var simulatedMultiplier = Root.Get().craftingLevelMultipliers[(int)simulatedCraftingLevel - 1];

                    //Calculate the true rate based on simulated multiplier
                    var simulatedRate = Math.Max(10.0, baseLegRate / simulatedMultiplier);
                    var simulatedRatio = 1.0 / simulatedRate;

                    //Calculate the legendary threshold based on the simulated ratio
                    var simulatedThreshold = Math.Pow(simulatedRatio, fixedCraftingCountCoefficient);
                    //Ceiling at 0.1
                    var ceilingedThreshold = Math.Min(0.1, simulatedThreshold);

                    //Add the threshold to the running sum
                    newLLCSum += ceilingedThreshold;
                }
            }

            var (linerEpicCount, henEpicCount) = GetCompledShipsOfDuration(account, DurationType.Epic);
            var (linerLongCount, henLongCount) = GetCompledShipsOfDuration(account, DurationType.Long);
            var (linerShortCount, henShortCount) = GetCompledShipsOfDuration(account, DurationType.Short);

            var craftCount = GetTotalCraftWithLegendaryPossibility(account.Backup.ArtifactHall);
            var legCount = GetLegendaryArtifactCount(account.Backup.ArtifactHall, llcCount: true);

            var newExpectedLeggies = newLLCSum + sumOfRatios;
            var newLLC = Math.Round(legCount - newExpectedLeggies, 2);
            var newLLCPercent = newExpectedLeggies != 0 ? (int)Math.Round((legCount * 100 / newExpectedLeggies) - 100) : 0;
            var newDisplayPercent = newLLCPercent == int.MinValue ? "-∞" : $"{newLLCPercent}%";
            var description = $"\n:tools: **Possible <:leggy:1113516502516248636> crafts** `{craftCount}`" +
                $"\n<:Henerprise:801748924146384906> **Henerprises** `{henEpicCount}` extended / `{henLongCount}` standard / `{henShortCount}` short" +
                $"\n<:Atreggies:1215022229826314380> **Atreggies** `{linerEpicCount}` extended / `{linerLongCount}` standard / `{linerShortCount}` short" +
                $"\n<:leggy:1113516502516248636> **Legendaries** `{Math.Round(newExpectedLeggies, 2)}` expected / `{legCount}` acquired";

            return new EmbedBuilder()
                .WithTitle($"`{newLLC:f2}` (`{newDisplayPercent}`)")
                .WithColor(Color.DarkBlue)
                .WithDescription(description)
                .WithAuthor(new EmbedAuthorBuilder()
                .WithName("Legendary Luck Coefficient")
                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
                .Build();
        }

        [SlashCommand(Description = "Calculate the EB% based on SE and PE inputs", ParentCommand = "formulae", AllowInDMs = true)]
        public static async Task Eb(FauxCommand command, [SlashParam(Description = "SE")] string SE, [SlashParam(Description = "PE", PositiveOnly = true)] int PE) {
            await command.RespondAsync("Calculating...");

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
                await command.RespondAsync(content: "", embed: EmbedError($"Invalid SE value: must end with {string.Join(", ", parserDict.Keys.ToList())}."));
                return;
            }

            if(PE <= 0 || PE > 1000) {
                await command.RespondAsync(content: "", embed: EmbedError("Invalid PE value: must be a positive integer less than 1000."));
                return;
            }

            var result = (seValue * 1.5) * Math.Pow(1.1, PE);
            var resultPercentage = result * 100;
            var bonus = Math.Round(Math.Pow((1.05 + 0.01 * 5), PE) * (1.5) * 100, 2);

            await command.ModifyOriginalResponseAsync($"{SIPrefix.GetPrefixFromEB(resultPercentage).RankWithSubRank} (<:Soul_Egg_SE:724341890794913964>`{SE}` and <:Egg_of_Prophecy_PE:669981330477547580>`{PE}`)\nEarning Bonus %: `{resultPercentage.ToEggString(true, 2)}%`\nEarning multiplier: `{result.ToEggString(true, 2)}`\nBonus per soul egg: `{bonus:n}%`");
        }
    }
}