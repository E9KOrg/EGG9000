using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;

namespace EGG9000.Bot.Commands {
    // The /bot command group. Subcommands are defined as `partial class BotGroupModule` blocks in
    // each contributing command file.
    [Group("bot", "Bot status and service management commands")]
    [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
    [StaffOnly(StaffTier.FarmHand)]
    public partial class BotGroupModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient gateway, IServiceProvider serviceProvider, ILogger<BotGroupModule> logger, JobService jobService) : E9KModuleBase(dbFactory) {
        private readonly ILogger<BotGroupModule> _logger = logger;
    }
}
