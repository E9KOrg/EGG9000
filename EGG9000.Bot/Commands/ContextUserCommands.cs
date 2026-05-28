using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class ContextUserModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;

        [UserCommand("Userstatus")]
        [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
        public async Task Userstatus(SocketGuildUser target) {
            await UserStatusCommands._userstatus(Context.Interaction, Db, _client, target, true, false);
        }

        [UserCommand("Contract Settings")]
        [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
        public async Task ContractSettings(SocketGuildUser target) {
            await ContractSettingsCommands.OpenContractSettings(Context.Interaction, Db, target);
        }

        [UserCommand("Rockets Tracker")]
        [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
        public async Task RocketsTrackerLinks(SocketGuildUser target) {
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == target.Id);
            if(dbUser == null) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError($"Unable to locate DBUser entry for <@{target.Id}>"));
                return;
            } else {
                var sb = new StringBuilder();
                foreach(var id in dbUser.EggIncAccounts) {
                    var backup = dbUser.EggIncAccounts.FirstOrDefault(x => x.Id == id.Id).Backup;
                    if(sb.ToString() != "") sb.Append("\n\n");
                    sb.Append(backup.UserName + ": " + $"<https://wasmegg-carpet.netlify.app/rockets-tracker/?playerId={id.Id}>");
                }
                await Context.Interaction.RespondAsync(sb.ToString(), ephemeral: true);
            }
        }
    }
}
