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
            [Discord.Interactions.ChoiceDisplay("30")] Twenty = 30,
            [Discord.Interactions.ChoiceDisplay("40")] Thirty = 40,
            [Discord.Interactions.ChoiceDisplay("50")] Forty = 50
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

        [ComponentCommand]
        private static void LLCCalculate(EggIncAccount account, StringBuilder sb, string userName) {
            var lastShipType = MissionHelpers.MaxShipLevels.Last().Key;
            
            var shipsForLastType = account.Backup.ShipsSent.Where(x => x.ship == lastShipType).ToList();

            var extendedCount = shipsForLastType.Where(x => x.type == Ei.MissionInfo.Types.DurationType.Epic).Sum(x => x.count);
            var standardCount = shipsForLastType.Where(x => x.type == Ei.MissionInfo.Types.DurationType.Long).Sum(x => x.count);
            var shortCount = shipsForLastType.Where(x => x.type == Ei.MissionInfo.Types.DurationType.Short).Sum(x => x.count);

            var craftCount = ArtifactHelpers.GetTotalCraftWithLegendaryPossibility(account.Backup.ArtifactHall);
            var legCount = ArtifactHelpers.GetLegendaryArtifactCount(account.Backup.ArtifactHall);

            double expectedDropL = (double)extendedCount / 25 + (double)standardCount / (4.5 * 25) + (double)shortCount / (6 * 25);
            double expectedCraftL = craftCount * 0.0085;

            var expectedLeg = Math.Round((expectedDropL + expectedCraftL), 2);
            var actualLegPercent = (int)(((double)legCount * 100 / (expectedDropL + expectedCraftL)) - 100);

            var LLC = Math.Round((legCount - expectedDropL - expectedCraftL), 2);

            sb.AppendLine($"The **LLC** for **{userName}** is `{LLC}` (`{actualLegPercent}%`)\n:tools: Total crafts with legendary possibility: `{craftCount}`\n<:Henerprise:801748924146384906> Henerprises: `{extendedCount}` extended / `{standardCount}` standard / `{shortCount}` short\n<:leggy:1113516502516248636> Legendaries: `{expectedLeg}` expected / `{legCount}` acquired");
        }
    }
}