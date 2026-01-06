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
        public static async Task SubscriptionLevelChanged(DiscordSocketClient _client, SocketGuild guild, Guild dbGuild, DBUser user, EggIncAccount account, ILogger? _logger = null) {
#nullable disable
            await SendUltraLogMessage(_client, dbGuild, user, account, (int?)account.SubscriptionLevel ?? -1, (int)account.Backup.SubscriptionLevel);

            var standardRoleId = dbGuild.ChannelDetails?.FirstOrDefault(x => x.ChannelType == GuildChannelType.StandardSubscription)?.Id ?? default;
            var proRoleId = dbGuild.ChannelDetails?.FirstOrDefault(x => x.ChannelType == GuildChannelType.ProSubscription)?.Id ?? default;

            var discorduser = guild.GetUser(user.DiscordId);
            if(discorduser is not null) {
                await CheckRole(standardRoleId, user, false, discorduser, _logger);
                await CheckRole(proRoleId, user, true, discorduser, _logger);
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
            var message = $"<@{user.DiscordId}>'s {(user.EggIncAccounts.Count > 1 && (account.Backup.UserName?.Length ?? 0) > 0 ? $" (`{account.Backup.UserName}`) " : "")}ULTRA status changed from `{LevelText(oldLevel)}` to `{LevelText(intNewLevel)}`.";
            _ = await ChannelHelper.DetermineAndSend(_client, dbGuild, GuildChannelType.UltraLog, new() { Text = message });
        }

#nullable enable
        private static async Task CheckRole(ulong roleid, DBUser dbuser, bool pro, SocketGuildUser user, ILogger? _logger = null) {
#nullable disable
            if(roleid == default || dbuser is null || dbuser.EggIncAccounts?.Count == 0 || user is null)
                return;
            var needsRole = dbuser.EggIncAccounts.Any(y => y.SubscriptionLevel == (pro ? UserSubscriptionInfo.Types.Level.Pro : UserSubscriptionInfo.Types.Level.Standard) && y.HasActiveSubscription());
            var hasRole = user?.Roles?.Any(x => x.Id == roleid) ?? false;

            if(hasRole && !needsRole) {
                await user.RemoveRoleAsync(roleid);
                _logger?.LogInformation("Removed {level} subscription role from {user}", pro ? "pro" : "standard", user.GetCleanName());
            } else if(!hasRole && needsRole) {
                await user.AddRoleAsync(roleid);
                _logger?.LogInformation("Added {level} subscription role to {user}", pro ? "pro" : "standard", user.GetCleanName());
            }
        }

    }
}
