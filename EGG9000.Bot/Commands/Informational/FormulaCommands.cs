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
using static EGG9000.Bot.Commands.ContractCommandsSlash;
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

        private class ShipData {
            public string Type { get; set; }
            public string Duration { get; set; }
            public int Level { get; set; }
            public double LegendaryDropRate { get; set; }

            public ShipData(string type, string duration, int level, double legendaryDropRate) {
                Type = type;
                Duration = duration;
                Level = level;
                LegendaryDropRate = legendaryDropRate;
            }
        }

        private class ShipsSent {
            public Dictionary<(MissionInfo.Types.Spaceship, MissionInfo.Types.DurationType, uint), int> ShipCounts { get; private set; }

            public ShipsSent(Backup backup) {
                ShipCounts = new Dictionary<(MissionInfo.Types.Spaceship, MissionInfo.Types.DurationType, uint), int>();

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

            public int GetShipsCount(MissionInfo.Types.Spaceship shipType, MissionInfo.Types.DurationType durationType, uint level) {
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

        public static int GetCompledShipsOfDuration(EggIncAccount account, MissionInfo.Types.DurationType duration) {
            var lastShipType = MissionHelpers.MaxShipLevels.Last().Key;

            var shipsForLastType = account.Backup.ShipsSent
                .Where(x => x.ship == lastShipType && x.type == duration)
                .ToList()
                .Sum(x => x.count);

            var exploringShips = account.Backup.SpaceMissions
                .Where(x => x.Ship == lastShipType && x.Status == MissionInfo.Types.Status.Exploring && x.Duration == duration)
                .ToList()
                .Count;

            return shipsForLastType - exploringShips;
        }

        [ComponentCommand]
        private static async Task LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {
            var shipDataTable = new List<(MissionInfo.Types.Spaceship ship, MissionInfo.Types.DurationType type, uint level, double legendaryDropRate)> {
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Epic, 0, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Short, 1, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Long, 1, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Epic, 1, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Short, 2, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Long, 2, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Epic, 2, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Short, 3, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Long, 3, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Epic, 3, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Short, 4, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Long, 4, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Epic, 4, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Short, 5, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Long, 5, 0.000),
                (MissionInfo.Types.Spaceship.Galeggtica, MissionInfo.Types.DurationType.Epic, 5, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Short, 0, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Long, 0, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Epic, 0, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Short, 1, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Long, 1, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Epic, 1, 482.673),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Short, 2, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Long, 2, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Epic, 2, 1615.316),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Short, 3, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Long, 3, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Epic, 3, 274.828),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Short, 4, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Long, 4, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Epic, 4, 431.604),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Short, 5, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Long, 5, 0.000),
                (MissionInfo.Types.Spaceship.Chickfiant, MissionInfo.Types.DurationType.Epic, 5, 0.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 0, 0.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 0, 579.538),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 0, 270.244),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 1, 0.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 1, 0.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 1, 133.825),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 2, 9010.667),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 2, 934.343),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 2, 119.026),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 3, 8244.540),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 3, 372.407),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 3, 113.645),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 4, 3056.498),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 4, 653.134),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 4, 105.118),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 5, 1212.981),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 5, 0.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 5, 161.565),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Short, 6, 654.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Long, 6, 0.000),
                (MissionInfo.Types.Spaceship.Voyegger, MissionInfo.Types.DurationType.Epic, 6, 143.500),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 0, 2535.522),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 0, 0.000),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 0, 55.675),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 1, 1263.428),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 1, 300.548),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 1, 51.978),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 2, 1410.754),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 2, 203.415),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 2, 36.620),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 3, 594.269),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 3, 319.529),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 3, 38.262),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 4, 501.500),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 4, 165.267),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 4, 30.459),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 5, 615.863),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 5, 87.388),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 5, 27.887),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 6, 422.235),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 6, 84.260),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 6, 25.055),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Short, 7, 483.407),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Long, 7, 103.098),
                (MissionInfo.Types.Spaceship.Henerprise, MissionInfo.Types.DurationType.Epic, 7, 24.977)
            };

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


            var backup = await ContractsAPI.FirstContact(account.Id);
            if(backup?.Backup?.ArtifactsDb?.MissionArchive is not null && account?.Backup?.ArtifactHall is not null) {
                var shipsSent = new ShipsSent(backup.Backup);

                var sumOfRatios = 0.0;
                foreach(var (ship, type, level, legendaryDropRate) in shipDataTable) {
                    var shipsSentCount = shipsSent.GetShipsCount(ship, type, level);
                    var shipsNeeded = legendaryDropRate;

                    var ratio = shipsNeeded != 0.0 ? shipsSentCount / shipsNeeded : 0.0;
                    sumOfRatios += ratio;
                }

                var afHall = account.Backup.ArtifactHall;
                var newLLCSum = 0.0;
                foreach(var craftType in baseCraftingCoefficients) {
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

                var extendedCount = GetCompledShipsOfDuration(account, MissionInfo.Types.DurationType.Epic);
                var standardCount = GetCompledShipsOfDuration(account, MissionInfo.Types.DurationType.Long);
                var shortCount = GetCompledShipsOfDuration(account, MissionInfo.Types.DurationType.Short);

                var craftCount = ArtifactHelpers.GetTotalCraftWithLegendaryPossibility(account.Backup.ArtifactHall);
                var legCount = ArtifactHelpers.GetLegendaryArtifactCount(account.Backup.ArtifactHall);

                /*var oldExpectedLeggies = craftCount * 0.0085 + sumOfRatios;
                var oldLLC = Math.Round(legCount - oldExpectedLeggies, 2);
                var oldLLCPercent = oldExpectedLeggies != 0 ? (int)((legCount * 100 / oldExpectedLeggies) - 100) : 0;
                var oldDisplayPercent = oldLLCPercent == int.MinValue ? "-∞" : $"{oldLLCPercent}%";*/

                var newExpectedLeggies = newLLCSum + sumOfRatios;
                var newLLC = Math.Round(legCount - newExpectedLeggies, 2);
                var newLLCPercent = newExpectedLeggies != 0 ? (int)Math.Round((legCount * 100 / newExpectedLeggies) - 100) : 0;
                var newDisplayPercent = newLLCPercent == int.MinValue ? "-∞" : $"{newLLCPercent}%";

                sb.AppendLine(
                    /*$"\nThe **OLD LLC** for **{userName}** is `{oldLLC:f2}` (`{oldDisplayPercent}`)" +*/
                    $"\nThe **LLC** for **{userName}** is `{newLLC:f2}` (`{newDisplayPercent}`)" +
                    $"\n:tools: Total crafts with legendary possibility: `{craftCount}`" +
                    $"\n<:Henerprise:801748924146384906> Henerprises: `{extendedCount}` extended / `{standardCount}` standard / `{shortCount}` short" +
                    /*$"\n<:leggy:1113516502516248636> Legendaries (OLD): `{Math.Round(oldExpectedLeggies, 2)}` expected / `{legCount}` acquired" +*/
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

        /*private static void LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {
            var extendedCount = GetCompledShipsOfDuration(account, MissionInfo.Types.DurationType.Epic);
            var standardCount = GetCompledShipsOfDuration(account, MissionInfo.Types.DurationType.Long);
            var shortCount = GetCompledShipsOfDuration(account, MissionInfo.Types.DurationType.Short);

            var craftCount = ArtifactHelpers.GetTotalCraftWithLegendaryPossibility(account.Backup.ArtifactHall);
            var legCount = ArtifactHelpers.GetLegendaryArtifactCount(account.Backup.ArtifactHall);

            var expectedDropL = (double)extendedCount / 25 + (double)standardCount / (4.5 * 25) + (double)shortCount / (6 * 25);
            var expectedCraftL = craftCount * 0.0085;

            var expectedLeg = Math.Round((expectedDropL + expectedCraftL), 2);
            int actualLegPercent;

            if((expectedDropL + expectedCraftL) != 0) {
                actualLegPercent = (int)(((double)legCount * 100 / (expectedDropL + expectedCraftL)) - 100);
            } else {
                actualLegPercent = int.MinValue;
            }

            var displayPercent = actualLegPercent == int.MinValue ? "-∞" : $"{actualLegPercent}";

            var LLC = Math.Round((legCount - expectedDropL - expectedCraftL), 2);

            sb.AppendLine($"The **LLC** for **{userName}** is `{LLC}` (`{displayPercent}%`)\n:tools: Total crafts with legendary possibility: `{craftCount}`\n<:Henerprise:801748924146384906> Henerprises: `{extendedCount}` extended / `{standardCount}` standard / `{shortCount}` short\n<:leggy:1113516502516248636> Legendaries: `{expectedLeg}` expected / `{legCount}` acquired");            
        }*/

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