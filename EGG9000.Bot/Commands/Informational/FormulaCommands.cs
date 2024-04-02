using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.ArtifactHelpers;
using static Ei.MissionInfo.Types;
using System.Collections.Generic;
using EGG9000.Bot.EggIncAPI;
using Ei;

namespace EGG9000.Bot.Commands {
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

            var sb = new StringBuilder();

            foreach(var account in dbUser.EggIncAccounts.Where(x => x.Backup is not null)) {
                if(dbUser.EggIncAccounts.Count > 1)
                    sb.AppendLine($"\n**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})**");
                MERCalculate(account, sb, dbUser.DiscordUsername, (int)MERValue);
            }

            await command.ModifyOriginalResponseAsync(x => x.Content = sb.ToString());
        }

        private static void MERCalculate(EggIncAccount account, StringBuilder sb, string userName, int MERValue) {
            double calculateMER(double se, long pe) {
                var result = (91 * (Math.Log10(se)) + 200 - pe) / 10;
                return result;
            }

            string calculateNeededSE(long MER, double se, long pe) {
                var result = Math.Pow(10, ((10 * MER - 200 + pe) / 91.0)) * 1e18;
                result -= se;
                return result.ToEggString();
            }

            double seQ;
            double calculateNeededPE(long MER, double se, long pe) {
                var result = (-10 * MER) + (91 * Math.Log10(seQ)) + 200;
                result -= pe;
                return result;
            }

            var backup = account.Backup;

            var seStr = backup.SoulEggs.ToEggString();
            seQ = backup.SoulEggs / 1e18; // Convert to quintillions
            var seTotal = backup.SoulEggs;
            long pe = backup.EggsOfProphecy;
            var MER = Math.Round(calculateMER(seQ, pe), 2);

            long MERgoal;
            if(MERValue != 0) {
                MERgoal = MERValue;
            } else {
                double value = Math.Round(calculateMER(seQ, pe), 1);
                if(value < 30) {
                    MERgoal = 30;
                } else if(value < 40) {
                    MERgoal = 40;
                } else {
                    MERgoal = 50;
                }
            }

            if(MERgoal > MER) {
                var MERse = calculateNeededSE(MERgoal, seTotal, pe);
                sb.AppendLine($"The **MER** for **{userName}** is `{MER}` (<:Egg_of_Prophecy_PE:669981330477547580>`{pe}` and<:Soul_Egg_SE:724341890794913964>`{seStr}`)\nAn additional <:Soul_Egg_SE:724341890794913964>`{MERse}` is needed for MER {MERgoal}");
            } else {
                var MERpe = Math.Round(calculateNeededPE(MERgoal, seQ, pe), 1);
                sb.AppendLine($"The **MER** for **{userName}** is `{MER}` (<:Egg_of_Prophecy_PE:669981330477547580>`{pe}` and<:Soul_Egg_SE:724341890794913964>`{seStr}`)\nYou're able to maintain MER {MERgoal} for another <:Egg_of_Prophecy_PE:669981330477547580>`{MERpe}`");
            }
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
                return ShipCounts.TryGetValue(key, out int count) ? count : 0;
            }
        }

        [SlashCommand(Description = "Calculate your Legendary Luck Coefficient (LLC)", ParentCommand = "formulae", AllowInDMs = true)]
        public static async Task Llc(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            } else if(!dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to retrieve your backup. Please try again later."); });
                return;
            }

            var sb = new StringBuilder();


            foreach(var account in dbUser.EggIncAccounts.Where(x => x.Backup is not null)) {
                if(dbUser.EggIncAccounts.Count > 1) {
                    sb.AppendLine($"\n**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})**");
                }
                await LLCCalculate(account, sb, dbUser.DiscordUsername);
            }
            
            await command.ModifyOriginalResponseAsync(x => x.Content = sb.ToString());
        }

        public static (int, int) GetCompledShipsOfDuration(EggIncAccount account, DurationType duration) {
            var maxShipLevels = MissionHelpers.MaxShipLevels.ToList();
            var lastShipType = maxShipLevels[maxShipLevels.Count - 1].Key;
            var secondToLastShipType = maxShipLevels[maxShipLevels.Count - 2].Key;

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
        private static async Task LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {

            var shipDataTable = new List<(Spaceship ship, DurationType type, List<double> legendaryDropRates)> {
                (Spaceship.Galeggtica, DurationType.Short, [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]),
                (Spaceship.Galeggtica, DurationType.Long, [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]),
                (Spaceship.Galeggtica, DurationType.Epic, [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]),
                (Spaceship.Chickfiant, DurationType.Short, [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]),
                (Spaceship.Chickfiant, DurationType.Long, [0.0, 0.0, 0.0, 0.0, 0.0, 0.0]),
                (Spaceship.Chickfiant, DurationType.Epic, [0.0, 482.673, 1615.316, 274.828, 431.604, 0.0]),
                (Spaceship.Voyegger, DurationType.Short, [0.0, 0.0, 9010.667, 8244.540, 3056.498, 1212.981, 654.000]),
                (Spaceship.Voyegger, DurationType.Long, [579.538, 0.0, 934.343, 372.407, 653.134, 0.0, 0.0]),
                (Spaceship.Voyegger, DurationType.Epic, [270.244, 133.825, 119.026, 113.645, 105.118, 161.565, 143.500]),
                (Spaceship.Henerprise, DurationType.Short, [2535.522, 1263.428, 1410.754, 594.269, 501.500, 615.863, 422.235, 483.407]),
                (Spaceship.Henerprise, DurationType.Long, [0.0, 300.548, 203.415, 319.529, 165.267, 87.388, 84.260, 103.098]),
                (Spaceship.Henerprise, DurationType.Epic, [55.675, 51.978, 36.620, 38.262, 30.459, 27.887, 25.055, 24.977]),
                //These are the Henerprise values as it is likely a decent (initial) estimation that the drop rates are similar
                (Spaceship.Atreggies, DurationType.Short, [2535.522, 1263.428, 1410.754, 594.269, 501.500, 615.863, 422.235, 483.407]),
                (Spaceship.Atreggies, DurationType.Long, [0.0, 300.548, 203.415, 319.529, 165.267, 87.388, 84.260, 103.098]),
                (Spaceship.Atreggies, DurationType.Epic, [55.675, 51.978, 36.620, 38.262, 30.459, 27.887, 25.055, 24.977]),
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


            var backup = await ContractsAPI.FirstContact(account.Id);
            if(backup?.Backup?.ArtifactsDb?.MissionArchive is not null && account?.Backup?.ArtifactHall is not null) {
                var shipsSent = new ShipsSent(backup.Backup);

                var sumOfRatios = 0.0;
                foreach(var (ship, type, dropRates) in shipDataTable) {
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
                foreach(var craftType in BaseCraftingCoefficients.Where(c => c.Value[2] != 0)) {
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
                        var simulatedMultiplier = levelMultipliers[simulatedCraftingLevel - 1];

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
                var legCount = GetLegendaryArtifactCount(account.Backup.ArtifactHall);

                var newExpectedLeggies = newLLCSum + sumOfRatios;
                var newLLC = Math.Round(legCount - newExpectedLeggies, 2);
                var newLLCPercent = newExpectedLeggies != 0 ? (int)Math.Round((legCount * 100 / newExpectedLeggies) - 100) : 0;
                var newDisplayPercent = newLLCPercent == int.MinValue ? "-∞" : $"{newLLCPercent}%";

                sb.AppendLine(
                    $"\nThe **LLC** for **{userName}** is `{newLLC:f2}` (`{newDisplayPercent}`)" +
                    $"\n:tools: Total crafts with legendary possibility: `{craftCount}`" +
                    $"\n<:Henerprise:801748924146384906> Henerprises: `{henEpicCount}` extended / `{henLongCount}` standard / `{henShortCount}` short" +
                    $"\n<:Atreggies:1215022229826314380> Atreggies: `{linerEpicCount}` extended / `{linerLongCount}` standard / `{linerShortCount}` short" +
                    $"\n<:leggy:1113516502516248636> Legendaries: `{Math.Round(newExpectedLeggies, 2)}` expected / `{legCount}` acquired"
                );
            } else {
                sb.AppendLine($"Unable to retrieve backup for {userName}. Please try again later.");
            }
        }
        private static int GetCraftingLevel(double CraftingXP) {
            var currentLevel = 1;
            long[] xpThresholds = [ 0, 500, 3000, 8000, 18000, 43000, 93000, 193000, 443000, 943000, 1943000,
                               3943000, 7943000, 15943000, 30943000, 50943000, 85943000, 145943000, 245943000,
                               395943000, 595943000, 845943000, 1145943000, 1470943000, 1820943000, 2220943000,
                               2720943000, 3320943000, 4070943000, 5070943000 ];

            for(var i = xpThresholds.Length - 1; i >= 0; i--) {
                if(CraftingXP >= xpThresholds[i]) {
                    currentLevel = i + 1;
                    break;
                }
            }
            return currentLevel;
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