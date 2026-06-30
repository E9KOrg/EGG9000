using EGG9000.Common.Commands;
using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    // One-shot maintenance command (NOT release-gated, so it exists on the prod bot). Forces every
    // account whose seasonal mode is not already AlwaysAssign back to AlwaysAssign. Intended to be run
    // once in PROD then removed in the next deploy. Admin-only.
    public static class ResetSeasonalAlwaysCommand {
        [SlashCommand(Description = "[Maintenance] Reset all accounts' seasonal mode to Always Assign", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "test")]
        public static async Task ResetSeasonalAlways(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: true);

            var users = await db.DBUsers.ToListAsync();

            var changedAccounts = 0;
            var changedUsers = 0;
            foreach(var user in users) {
                var userTouched = false;
                // Accessing EggIncAccounts runs the migrate/heal-on-read, so Assignment + Seasonal are
                // guaranteed non-null here.
                foreach(var account in user.EggIncAccounts) {
                    var seasonal = account.Assignment.Seasonal;
                    if(seasonal.Mode == SeasonalMode.AlwaysAssign) continue;
                    seasonal.Mode = SeasonalMode.AlwaysAssign;
                    changedAccounts++;
                    userTouched = true;
                }
                if(userTouched) {
                    user.UpdateAccounts();
                    changedUsers++;
                }
            }

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = EmbedSuccess($"Reset seasonal mode to Always Assign for **{changedAccounts}** account(s) across **{changedUsers}** user(s). " +
                    $"({users.Count} users scanned.)");
            });
        }
    }
}
