using Discord;
using Discord.Interactions;
using EGG9000.Common.Helpers.Discord;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public partial class BotGroupModule {

        [SlashCommand("dumpperms", "Dump this server's command permission overrides to a file")]
        public async Task DumpPerms() {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var report = await CommandPermissionDump.BuildReportAsync(gateway, Context.Guild.Id);
            var bytes = Encoding.UTF8.GetBytes(report);

            await Context.Interaction.FollowupWithFileAsync(
                new FileAttachment(new MemoryStream(bytes), $"command-perms-{Context.Guild.Id}.txt"),
                text: "Command permission overrides attached.", ephemeral: true);
        }
    }
}
