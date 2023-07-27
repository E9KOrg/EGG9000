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
    public static class ForumlaCommands {
        public enum MERChoice {
            [Discord.Interactions.ChoiceDisplay("Current")] Current = 0,
            [Discord.Interactions.ChoiceDisplay("30")] Twenty = 30,
            [Discord.Interactions.ChoiceDisplay("40")] Thirty = 40,
            [Discord.Interactions.ChoiceDisplay("50")] Forty = 50
        };

        [SlashCommand(Description = "Calculate your Mystical Egg Ratio (MER)", ParentCommand = "formulae")]
        public static async Task Mer(FauxCommand command, ApplicationDbContext db, [SlashParam(Required = true)] MERChoice MERValue) {
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

        /*[SlashCommand(Description = "Calculate your Legendary Luck Coefficient (LLC)", ParentCommand = "formulae")]
        public static async Task Llc(FauxCommand command, ApplicationDbContext db) {
            await command.RespondAsync("Getting account backups...", ephemeral: true);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var contentString = "";

            if(user.EggIncAccounts.Count == 1) {
                contentString = await LLCCalculate(user.EggIncAccounts.FirstOrDefault(), user.DiscordUsername);
                await command.ModifyOriginalResponseAsync(x => { x.Components = null; x.Content = "Success"; });
                await command.Channel.SendMessageAsync(contentString);
            } else {
                var builder = new ComponentBuilder();
                foreach(var account in user.EggIncAccounts) {
                    builder.WithButton($"{account.Name} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"LLCAccountButton:{account.Id}|{user.DiscordUsername} - {account.Name}");
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to check the LLC of."; x.Components = builder.Build(); });
            }
        }

        [ComponentCommand]
        public static async Task LLCAccountButton(SocketMessageComponent component, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            if(user is null) return;
            var dataObjs = data.Split("|");
            var account = user.EggIncAccounts.FirstOrDefault(x => x.Id == dataObjs[0]);
            var discordUsername = dataObjs[1];

            var contentString = await LLCCalculate(account, discordUsername);
            await component.UpdateAsync(x => { x.Components = null; x.Content = "Success"; });
            await component.Channel.SendMessageAsync(contentString);
        }
        private static async Task<string> LLCCalculate(EggIncAccount account, string userName) {
            var stringBuilder = new StringBuilder();
            var backup = account.Backup;
            if(backup == null) {
                return null;
            }

            stringBuilder.Append($"The **LLC** for **{userName}** is ``");
            stringBuilder.AppendLine();
            return stringBuilder.ToString();
        }*/
    }
}