
using Discord.WebSocket;

using EGG9000.Common.Services;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;

using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EGG9000.Common.Commands;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class ContextUserCommands {
        [UserCommand(Name = "Userstatus", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task Userstatus(SocketUserCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            await RegisterCommandsSlash._userstatus(command, db, _client, apiLink, command.Data.Member, true, false);
        }

        [UserCommand(Name = "Contract Settings", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task ContractSettings(SocketUserCommand command, ApplicationDbContext db) {
            await ContractSettingsCommands.ContractSettings(command, db, (command.Data.Member as SocketGuildUser));
        }

        [UserCommand(Name = "Rockets Tracker", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RocketsTrackerLinks(SocketUserCommand command, ApplicationDbContext db)
        {
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.Data.Member.Id);
            if(dbUser == null) {
                await command.RespondAsync(text: "", embed: EmbedError($"Unable to locate DBUser entry for <@{command.Data.Member.Id}>"));
                return;
            } else {
                var sb = new StringBuilder();
                foreach(var id in dbUser.EggIncAccounts) {
                    var backup = dbUser.EggIncAccounts.FirstOrDefault(x => x.Id == id.Id).Backup;
                    if(sb.ToString() != "") sb.Append("\n\n");
                    sb.Append(backup.UserName + ": " + $"<https://wasmegg-carpet.netlify.app/rockets-tracker/?playerId={id.Id}>");
                }
                await command.RespondAsync(sb.ToString(), ephemeral: true);
            }
        }
    }
}


