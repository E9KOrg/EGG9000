using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Commands {
    // The /admin command group. Subcommands are defined as `partial class AdminGroupModule` blocks
    // in each contributing command file.
    [Group("admin", "Admin commands")]
    [DefaultMemberPermissions(Discord.GuildPermission.Administrator)]
    [StaffOnly(StaffTier.Admin)]
    public partial class AdminGroupModule(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<AdminGroupModule> logger, DiscordHostedService client, DiscordSocketClient gateway) : E9KModuleBase(dbFactory) {
        private readonly ILogger<AdminGroupModule> _logger = logger;
        private readonly DiscordHostedService _client = client;
    }
}
