using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Interactions;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;

namespace EGG9000.Bot.Commands {
    // The /a admin command group. Subcommands are defined as `partial class AdminModule` blocks in
    // each contributing command file. Discord sets default_member_permissions only at the top-level
    // command, so the whole group is gated here at the least-restrictive legacy level (FarmHand ->
    // CreatePrivateThreads)
    [Group("a", "Admin commands")]
    [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
    [StaffOnly(StaffTier.FarmHand)]
#pragma warning disable CS9113 // jobService/serviceProvider are read by sibling AdminModule partial-class files, not this one
    public partial class AdminModule(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<AdminModule> logger, DiscordHostedService client, DiscordSocketClient gateway, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ContractUpdater contractUpdater, Bugsnag.IClient bugsnag, JobService jobService, IServiceProvider serviceProvider) : E9KModuleBase(dbFactory) {
        private readonly ILogger<AdminModule> _logger = logger;
    }
#pragma warning restore CS9113
}
