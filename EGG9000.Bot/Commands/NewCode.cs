using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class NewCodeModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {
        [SlashCommand("newcoopcode", "Generate a new co-op code that has never been used for any contract")]
        [DefaultMemberPermissions(Discord.GuildPermission.ManageChannels)]
        [StaffOnly(StaffTier.CluckingCoordinator)]
        public async Task NewCoopCode() {
            await Context.Interaction.DeferAsync();
            var code = await CoopNames.GenerateUnusedAsync(Db, null);
            Db.Coops.Add(new Coop { Name = code, Created = DateTimeOffset.UtcNow, GuildId = Context.Interaction.GuildId.Value });
            await Db.SaveChangesAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = code);
        }

        [SlashCommand("deletecoop", "Delete co-op channel from discord and database ")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [StaffOnly(StaffTier.Admin)]
        [ChannelContext(Coop = true)]
        public async Task DeleteCoop() {
            await Context.Interaction.DeferAsync();
            Db.Remove(CoopChannel);
            await Db.SaveChangesAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedSuccess("Coop deleted from DB."));
            await ((SocketThreadChannel)Context.Channel).ModifyAsync(c => {
                c.Archived = true;
                c.Locked = true;
            });
        }
    }
}
