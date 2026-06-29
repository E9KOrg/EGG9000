using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class NewCodeModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient client) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordSocketClient _client = client;

        [SlashCommand("newcoopcode", "Generate a new co-op code, a channel will be created for the co-op")]
        [DefaultMemberPermissions(Discord.GuildPermission.ManageChannels)]
        [EGG9000.Bot.Interactions.StaffOnly(EGG9000.Bot.Interactions.StaffTier.CluckingCoordinator)]
        public async Task NewCoopCode() {
            await Context.Interaction.DeferAsync();
            var words = new Words();
            var wordOne = words.GetRandomWord();
            var wordTwo = words.GetRandomSecondWord(wordOne);
            var code = wordOne + wordTwo + words.GetRandomNumber();

            var guild = _client.Guilds.FirstOrDefault(g => g.Id == Context.Interaction.GuildId);

            var coop = new Coop { Name = code, Created = DateTimeOffset.UtcNow, GuildId = guild.Id };
            Db.Coops.Add(coop);
            await Db.SaveChangesAsync();

            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = code);
        }

        [SlashCommand("deletecoop", "Delete co-op channel from discord and database ")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [EGG9000.Bot.Interactions.StaffOnly(EGG9000.Bot.Interactions.StaffTier.Admin)]
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
