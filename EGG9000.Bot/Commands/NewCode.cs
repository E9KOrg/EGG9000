using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class NewCodeModule(IDbContextFactory<ApplicationDbContext> dbFactory) : Interactions.E9KModuleBase(dbFactory) {
        [SlashCommand("deletecoop", "Delete co-op channel from discord and database ")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [Interactions.StaffOnly(Interactions.StaffTier.Admin)]
        public async Task DeleteCoop() {
            await Context.Interaction.DeferAsync();
            var coop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedError($"Unable to find co-op, is this being run in a co-op thread?"));
                return;
            }
            Db.Remove(coop);
            await Db.SaveChangesAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedSuccess("Coop deleted from DB."));
            await ((SocketThreadChannel)Context.Channel).ModifyAsync(c => {
                c.Archived = true;
                c.Locked = true;
            });
        }
    }
}
