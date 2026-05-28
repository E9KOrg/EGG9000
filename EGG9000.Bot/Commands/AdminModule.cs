using Discord.Interactions;
using Discord.WebSocket;
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
    // CreatePrivateThreads, matching the old dispatcher's Min-over-subcommands behavior). Do NOT add
    // [DefaultMemberPermissions] to individual subcommand methods - Discord does not support it there.
    //
    // Services: this constructor injects the union of what the /a subcommands need. Add fields here as
    // subcommands are migrated in. Trim any that end up unused once the batch is complete.
    [Group("a", "Admin commands")]
    [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
    public partial class AdminModule : E9KModuleBase {
        private readonly ILogger<AdminModule> _logger;
        private readonly DiscordHostedService _client;
        private readonly DiscordSocketClient _gateway;
        private readonly ThreadsCoopStatusUpdater _coopStatusUpdaterThreads;
        private readonly Bugsnag.IClient _bugsnag;
        private readonly JobService _jobService;
        private readonly IServiceProvider _serviceProvider;

        public AdminModule(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<AdminModule> logger, DiscordHostedService client, DiscordSocketClient gateway, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, Bugsnag.IClient bugsnag, JobService jobService, IServiceProvider serviceProvider) : base(dbFactory) {
            _logger = logger;
            _client = client;
            _gateway = gateway;
            _coopStatusUpdaterThreads = coopStatusUpdaterThreads;
            _bugsnag = bugsnag;
            _jobService = jobService;
            _serviceProvider = serviceProvider;
        }
    }
}
