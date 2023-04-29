using Discord;

using EGG9000.Bot;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBot.Commands {
    public class NextRankTest {
        [SlashCommand(Description = "How many SE/PE needed for next rank up")]
        public static async Task NextRank(FauxCommand command, ApplicationDbContext db) {
            await command.RespondAsync("Getting backups...", ephemeral: true);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }
            var builder = new EmbedBuilder();
            builder.Title = $"Next Rank Details";
            foreach(var id in user.EggIncAccounts) {
                var backup = user.Backups.FirstOrDefault(x => x.EggIncId == id.Id);
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup);
                var nextSubRank = SIPrefix.GetNextRankInfo(backup, true);

                var nextRankText = "";
                foreach(var subrank in nextSubRank.Take(5)) {
                    nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                    if(subrank.SoulsEggs < 0)
                        break;
                }
                builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (user.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextSubRank.First().Rank} [{nextSubRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });

                var nextRank = SIPrefix.GetNextRankInfo(backup, false);
                var currentRank = SIPrefix.GetPrefixFromEB(backup.EarningsBonus);
                if(nextRank.First().SoulsEggs != nextSubRank.First().SoulsEggs) {
                    nextRankText = "";
                    foreach(var subrank in nextRank.Take(5)) {
                        nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                        if(subrank.SoulsEggs < 0)
                            break;
                    }
                    builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (user.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextRank.First().Rank} [{nextRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });
                }
                var ge = backup.GoldenEggsEarned - backup.GoldenEggsSpent;
                builder.AddField(new EmbedFieldBuilder { IsInline = false, Name = "Current Details", Value = @$"{currentRank.RankWithSubRank}
<:Egg_of_Prophecy_PE:669981330477547580>{backup.EggsOfProphecy}
<:Soul_Egg_SE:724341890794913964>{backup.SoulEggs.ToEggString(numberOfDecimalPlaces: 3)}
EB {backup.EarningsBonus.ToEggString(numberOfDecimalPlaces: 3)}
Prestiges {backup.NumPrestiges}
<:Soul_Egg_SE:724341890794913964>/Prestige {(backup.SoulEggs / backup.NumPrestiges).ToEggString(numberOfDecimalPlaces: 3)}
<:Golden_Egg_GE:692439755798872075> {(ge > 1_000_000_000 ? ge.ToEggString(numberOfDecimalPlaces: 3) : ge.ToString("n0"))}
<:Piggy_bank:724396277676113955>  {(backup.TotalGEInPiggyBank > 1_000_000_000 ? backup.TotalGEInPiggyBank.ToEggString(numberOfDecimalPlaces: 3) : backup.TotalGEInPiggyBank.ToString("n0"))}
<:Drone:755719353529270342> {backup.DroneTakedowns.ToString("n0")}
<:Drone:755719353529270342> Elite {backup.DroneTakedownsElite.ToString("n0")}
Last Backup <t:{backup.LastBackupTime}:R>
" });
            }
            //await command.Channel.SendMessageAsync($"{command.User.Mention} used the command `/nextrank`", embed: builder.Build());
            //await command.DeleteResponseFix();
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = builder.Build(); });
        }
    }
}
