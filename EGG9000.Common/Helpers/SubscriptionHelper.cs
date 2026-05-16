using Discord.WebSocket;

using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.Database.Entities;

using Ei;

using Microsoft.Extensions.Logging;

using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class SubscriptionHelper {

#nullable enable
        public static async Task SubscriptionLevelChanged(DiscordSocketClient gateway, SocketGuild guild, Guild dbGuild, DBUser user, EggIncAccount account, ILogger? logger = null) {
#nullable disable

            var orignalLevel = account.SubscriptionLevel;

            account.SubscriptionLevel = account.Backup.SubscriptionLevel;
            account.SubscriptionEnds = account.Backup.SubscriptionEnds;

            var standardRoleId = dbGuild.ChannelDetails?.FirstOrDefault(x => x.ChannelType == GuildChannelType.StandardSubscription)?.Id ?? default;
            var proRoleId = dbGuild.ChannelDetails?.FirstOrDefault(x => x.ChannelType == GuildChannelType.ProSubscription)?.Id ?? default;

            var discordUser = guild?.GetUser(user.DiscordId);
            if(discordUser is not null) {
                var changed = false;
                if(await CheckRole(standardRoleId, user, false, discordUser, logger)) {
                    changed = true;
                }
                if(await CheckRole(proRoleId, user, true, discordUser, logger)) {
                    changed = true;
                }
                if(changed) {
                    await SendUltraLogMessage(gateway, dbGuild, user, account, (int?)orignalLevel ?? -1, (int?)account.Backup?.SubscriptionLevel ?? -1);
                }
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

        private static async Task SendUltraLogMessage(DiscordSocketClient gateway, Guild dbGuild, DBUser user, EggIncAccount account, int oldLevel, int newLevel) {
            var accountName = account.Backup?.UserName;
            var accountTag = user.EggIncAccounts.Count > 1 && (accountName?.Length ?? 0) > 0 ? $" (`{accountName}`) " : "";
            var message = $"<@{user.DiscordId}>{accountTag}ULTRA status changed from `{LevelText(oldLevel)}` to `{LevelText(newLevel)}`.";
            _ = await ChannelHelper.DetermineAndSend(gateway, dbGuild, GuildChannelType.UltraLog, new() { Text = message });
        }

#nullable enable
        public static async Task<bool> CheckRole(ulong roleId, DBUser dbUser, bool pro, SocketGuildUser user, ILogger? logger = null) {
#nullable disable
            if(roleId == default || dbUser is null || dbUser.EggIncAccounts?.Count == 0 || user is null)
                return false;
            var needsRole = dbUser.EggIncAccounts.Any(y => y.SubscriptionLevel == (pro ? UserSubscriptionInfo.Types.Level.Pro : UserSubscriptionInfo.Types.Level.Standard) && (y.HasActiveSubscription() ));
            var hasRole = user?.Roles?.Any(x => x.Id == roleId) ?? false;
            var changed = false;
            if(hasRole && !needsRole) {
                await user.RemoveRoleAsync(roleId);
                logger?.LogInformation("Removed {level} subscription role from {user}", pro ? "pro" : "standard", user.Username);
                changed = true;
            } else if(!hasRole && needsRole) {
                await user.AddRoleAsync(roleId);
                logger?.LogInformation("Added {level} subscription role to {user}", pro ? "pro" : "standard", user.Username);
                changed = true;
            } else {
                logger?.LogInformation("No role change needed for {user} (hasRole: {hasRole}, needsRole: {needsRole})", user.Username, hasRole, needsRole);
            }
            return changed;
        }
    }
}
