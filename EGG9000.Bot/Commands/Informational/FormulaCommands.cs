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
            if(dbUser == null || !dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"));
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
            public Dictionary<(Ei.MissionInfo.Types.Spaceship, Ei.MissionInfo.Types.DurationType, uint), int> ShipCounts { get; private set; }

            public ShipsSent(Ei.Backup backup) {
                ShipCounts = new Dictionary<(Ei.MissionInfo.Types.Spaceship, Ei.MissionInfo.Types.DurationType, uint), int>();

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

            public int GetShipsCount(Ei.MissionInfo.Types.Spaceship shipType, Ei.MissionInfo.Types.DurationType durationType, uint level) {
                var key = (shipType, durationType, level);
                return ShipCounts.TryGetValue(key, out int count) ? count : 0;
            }
        }

        [SlashCommand(Description = "Calculate your Legendary Luck Coefficient (LLC)", ParentCommand = "formulae", AllowInDMs = true)]
        public static async Task Llc(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null || !dbUser.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
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

        public static int GetCompledShipsOfDuration(EggIncAccount account, Ei.MissionInfo.Types.DurationType duration) {
            var lastShipType = MissionHelpers.MaxShipLevels.Last().Key;

            var shipsForLastType = account.Backup.ShipsSent
                .Where(x => x.ship == lastShipType && x.type == duration)
                .ToList()
                .Sum(x => x.count);

            var exploringShips = account.Backup.SpaceMissions
                .Where(x => x.Ship == lastShipType && x.Status == Ei.MissionInfo.Types.Status.Exploring && x.Duration == duration)
                .ToList()
                .Count;

            return shipsForLastType - exploringShips;
        }

        [ComponentCommand]
        private static async Task LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {
            var shipDataTable = new List<(Ei.MissionInfo.Types.Spaceship ship, Ei.MissionInfo.Types.DurationType type, uint level, double legendaryDropRate)> {
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Epic, 0, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Short, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Long, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Epic, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Short, 2, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Long, 2, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Epic, 2, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Short, 3, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Long, 3, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Epic, 3, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Short, 4, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Long, 4, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Epic, 4, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Short, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Long, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Galeggtica, Ei.MissionInfo.Types.DurationType.Epic, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Short, 0, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Long, 0, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Epic, 0, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Short, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Long, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Epic, 1, 482.673),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Short, 2, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Long, 2, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Epic, 2, 1615.316),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Short, 3, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Long, 3, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Epic, 3, 274.828),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Short, 4, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Long, 4, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Epic, 4, 431.604),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Short, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Long, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Chickfiant, Ei.MissionInfo.Types.DurationType.Epic, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 0, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 0, 579.538),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 0, 270.244),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 1, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 1, 133.825),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 2, 9010.667),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 2, 934.343),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 2, 119.026),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 3, 8244.540),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 3, 372.407),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 3, 113.645),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 4, 3056.498),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 4, 653.134),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 4, 105.118),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 5, 1212.981),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 5, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 5, 161.565),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Short, 6, 654.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Long, 6, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Voyegger, Ei.MissionInfo.Types.DurationType.Epic, 6, 143.500),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 0, 2535.522),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 0, 0.000),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 0, 55.675),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 1, 1263.428),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 1, 300.548),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 1, 51.978),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 2, 1410.754),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 2, 203.415),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 2, 36.620),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 3, 594.269),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 3, 319.529),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 3, 38.262),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 4, 501.500),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 4, 165.267),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 4, 30.459),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 5, 615.863),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 5, 87.388),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 5, 27.887),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 6, 422.235),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 6, 84.260),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 6, 25.055),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Short, 7, 483.407),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Long, 7, 103.098),
                (Ei.MissionInfo.Types.Spaceship.Henerprise, Ei.MissionInfo.Types.DurationType.Epic, 7, 24.977)
            };

            var backup = await ContractsAPI.FirstContact(account.Id);
            if(backup?.Backup?.ArtifactsDb?.MissionArchive is not null) {
                var shipsSent = new ShipsSent(backup.Backup);

                var sumOfRatios = 0.0;
                foreach(var (ship, type, level, legendaryDropRate) in shipDataTable) {
                    var shipsSentCount = shipsSent.GetShipsCount(ship, type, level);
                    var shipsNeeded = legendaryDropRate;

                    var ratio = shipsNeeded != 0.0 ? shipsSentCount / shipsNeeded : 0.0;
                    sumOfRatios += ratio;
                }

                var extendedCount = GetCompledShipsOfDuration(account, Ei.MissionInfo.Types.DurationType.Epic);
                var standardCount = GetCompledShipsOfDuration(account, Ei.MissionInfo.Types.DurationType.Long);
                var shortCount = GetCompledShipsOfDuration(account, Ei.MissionInfo.Types.DurationType.Short);

                var craftCount = ArtifactHelpers.GetTotalCraftWithLegendaryPossibility(account.Backup.ArtifactHall);
                var legCount = ArtifactHelpers.GetLegendaryArtifactCount(account.Backup.ArtifactHall);

                var expectedLeggies = craftCount * 0.0085 + sumOfRatios;
                var LLC = Math.Round((legCount - expectedLeggies), 2);
                var LLCPercent = expectedLeggies != 0 ? (int)((legCount * 100 / expectedLeggies) - 100) : 0;

                var displayPercent = LLCPercent == int.MinValue ? "-∞" : $"{LLCPercent}%";
                sb.AppendLine($"The **LLC** for **{userName}** is `{LLC}` (`{displayPercent}`)\n:tools: Total crafts with legendary possibility: `{craftCount}`\n<:Henerprise:801748924146384906> Henerprises: `{extendedCount}` extended / `{standardCount}` standard / `{shortCount}` short\n<:leggy:1113516502516248636> Legendaries: `{Math.Round(expectedLeggies, 2)}` expected / `{legCount}` acquired");
            } else {
                sb.AppendLine($"Unable to locate DBUser entry for {userName}.\nAre you registered?");
            }
        }

        /*private static void LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {
            var extendedCount = GetCompledShipsOfDuration(account, Ei.MissionInfo.Types.DurationType.Epic);
            var standardCount = GetCompledShipsOfDuration(account, Ei.MissionInfo.Types.DurationType.Long);
            var shortCount = GetCompledShipsOfDuration(account, Ei.MissionInfo.Types.DurationType.Short);

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
        public static async Task Eb(FauxCommand command, [SlashParam(Description = "SE")] string SE, [SlashParam(Description = "PE")] int PE) {
            await command.RespondAsync("Calculating...");

            double seValue;

            switch(SE.Last()) {
                case 'q':
                    seValue = double.Parse(SE.TrimEnd('q')) * 1e15;
                    break;
                case 'Q':
                    seValue = double.Parse(SE.TrimEnd('Q')) * 1e18;
                    break;
                case 's':
                    seValue = double.Parse(SE.TrimEnd('s')) * 1e21;
                    break;
                default:
                    await command.RespondAsync(content: "", embed: EmbedError("Invalid SE value: must end with q, Q, or s"));
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