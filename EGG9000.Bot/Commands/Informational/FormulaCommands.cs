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
using static Ei.Backup.Types;

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
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null || !user.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var sb = new StringBuilder();

            foreach(var account in user.EggIncAccounts.Where(x => x.Backup is not null)) {
                if(user.EggIncAccounts.Count > 1)
                    sb.AppendLine($"\n**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})**");
                MERCalculate(account, sb, user.DiscordUsername, (int)MERValue);
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

        [SlashCommand(Description = "Calculate your Legendary Luck Coefficient (LLC)", ParentCommand = "formulae", AllowInDMs = true)]
        public static async Task Llc(FauxCommand command, ApplicationDbContext db) {
            await command.RespondAsync("Getting account backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null || !user.EggIncAccounts.Any(x => x.Backup is not null)) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var sb = new StringBuilder();

            foreach(var account in user.EggIncAccounts.Where(x => x.Backup is not null)) {
                if(user.EggIncAccounts.Count > 1)
                    sb.AppendLine($"\n**{account.Backup.UserName} ({account.Backup.EarningsBonus.ToEggString()})**");
                LLCCalculate(account, sb, user.DiscordUsername);
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
        private static void LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {
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
        }

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
                    await command.RespondAsync("⚠️ERROR: Invalid SE value: must end with q, Q, or s");
                    return;
            }

            if(PE <= 0 || PE > 1000) {
                await command.RespondAsync("⚠️ERROR: Invalid PE value");
                return;
            }

            var result = (seValue * 1.5) * Math.Pow(1.1, PE);
            var resultPercentage = result * 100;
            var bonus = Math.Round(Math.Pow((1.05 + 0.01 * 5), PE) * (1.5) * 100, 2);

            await command.ModifyOriginalResponseAsync($"{SIPrefix.GetPrefixFromEB(resultPercentage).RankWithSubRank} (<:Soul_Egg_SE:724341890794913964>`{SE}` and <:Egg_of_Prophecy_PE:669981330477547580>`{PE}`)\nEarning Bonus %: `{resultPercentage.ToEggString(true, 2)}%`\nEarning multiplier: `{result.ToEggString(true, 2)}`\nBonus per soul egg: `{bonus:n}%`");
        }
    }
}