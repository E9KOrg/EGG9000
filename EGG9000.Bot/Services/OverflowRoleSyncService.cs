using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class OverflowRoleSyncService(DiscordHostedService discord, IServiceProvider provider, ILogger<OverflowRoleSyncService> logger) : IHostedService {

        public Task StartAsync(CancellationToken cancellationToken) {
            discord.Gateway.RoleUpdated += OnRoleUpdated;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            discord.Gateway.RoleUpdated -= OnRoleUpdated;
            return Task.CompletedTask;
        }

        private Task OnRoleUpdated(SocketRole originalRole, SocketRole updatedRole) {
            _ = HandleAsync(originalRole, updatedRole);
            return Task.CompletedTask;
        }

        private async Task HandleAsync(SocketRole originalRole, SocketRole updatedRole) {
            try {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await OverflowSyncing.SyncRoleUpdateAsync(originalRole, updatedRole, db, discord, logger);
            } catch(Exception e) {
                logger.LogError(e, "Error syncing role update for {RoleName} in guild {GuildId}", updatedRole?.Name, originalRole?.Guild?.Id);
            }
        }
    }
}
