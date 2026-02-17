using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database.Entities;
using Ei;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public static class SubscriptionUpdater {

        public static async Task UpdateSubscriptionForAccount(DiscordSocketClient _client, SocketGuild guild, Guild dbGuild, DBUser user, EggIncAccount account, ILogger logger = null) {
            // Use API-based subscription check since the proto SubInfo field is not available on iOS yet
            // Once SubInfo is available in the proto for all platforms, this can be updated to read from Backup
            try {
                var subscriptionStatus = await ContractsAPI.GetUserSubscription(account.Id);
                if(subscriptionStatus is null) {
                    return;
                }

                if(subscriptionStatus.HasStatus && (subscriptionStatus.Status == UserSubscriptionInfo.Types.Status.Active || subscriptionStatus.Status == UserSubscriptionInfo.Types.Status.GracePeriod) && subscriptionStatus.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                    if(account.SubscriptionLevel != subscriptionStatus.SubscriptionLevel) {
                        await SendUltraLogMessage(_client, dbGuild, user, account, (int?)account.SubscriptionLevel ?? -1, (int)subscriptionStatus.SubscriptionLevel);
                        account.SubscriptionLevel = subscriptionStatus.SubscriptionLevel;
                    }
                    if(account.SubscriptionEnds != subscriptionStatus.PeriodEnd) {
                        account.SubscriptionEnds = subscriptionStatus.PeriodEnd;
                    }
                } else if(account.SubscriptionLevel.HasValue) {
                    await SendUltraLogMessage(_client, dbGuild, user, account, (int?)account.SubscriptionLevel ?? -1, -1);
                    account.SubscriptionLevel = null;
                }
            } catch(Exception ex) {
                // Log but don't fail - subscription check is not critical for backup updates
                logger?.LogWarning(ex, "Failed to update subscription for account {accountId}", account.Id);
            }
        }

        private static string LevelText(int level) {
            return level switch {
                -1 => "Not Subscribed",
                0 => "ULTRA Standard",
                1 => "ULTRA Pro",
                _ => "???"
            };
        }

        private static async Task SendUltraLogMessage(DiscordSocketClient _client, Guild dbGuild, DBUser user, EggIncAccount account, int oldLevel, int intNewLevel) {
            var message = $"<@{user.DiscordId}>'s {(user.EggIncAccounts.Count > 1 && (account.Backup?.UserName?.Length ?? 0) > 0 ? $" (`{account.Backup.UserName}`) " : "")}ULTRA status changed from `{LevelText(oldLevel)}` to `{LevelText(intNewLevel)}`.";
            try {
                _ = await ChannelHelper.DetermineAndSend(_client, dbGuild, GuildChannelType.UltraLog, new() { Text = message });
            } catch(Exception) {
                // Silently ignore log message failures
            }
        }
    }
}
