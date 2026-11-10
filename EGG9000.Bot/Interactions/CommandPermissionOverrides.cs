using Discord;
using Discord.WebSocket;
using EGG9000.Common.Helpers.Discord;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public class CommandPermissionOverrides(DiscordSocketClient gateway, IMemoryCache cache, ILogger<CommandPermissionOverrides> logger) {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

        public async Task<bool> AllowsAsync(SocketGuildUser user, ulong commandId) {
            var overrides = await GetAsync(user.Guild.Id);
            var rules = overrides.FirstOrDefault(o => o.CommandId == commandId)?.Permissions;
            return rules is not null && Allows(rules, user);
        }

        public static bool Allows(IReadOnlyCollection<ApplicationCommandPermission> rules, SocketGuildUser user) {
            var userRule = rules.FirstOrDefault(p => p.TargetType == ApplicationCommandPermissionTarget.User && p.TargetId == user.Id);
            if(userRule is not null) return userRule.Permission;

            var everyoneId = user.Guild.Id;
            var roleRules = rules
                .Where(p => p.TargetType == ApplicationCommandPermissionTarget.Role && p.TargetId != everyoneId && user.Roles.Any(r => r.Id == p.TargetId))
                .ToList();
            if(roleRules.Count > 0) return roleRules.Any(p => p.Permission);

            var everyoneRule = rules.FirstOrDefault(p => p.TargetType == ApplicationCommandPermissionTarget.Role && p.TargetId == everyoneId);
            return everyoneRule is not null && everyoneRule.Permission;
        }

        private async Task<IReadOnlyCollection<GuildApplicationCommandPermission>> GetAsync(ulong guildId) {
            var key = $"command-permission-overrides:{guildId}";
            if(cache.TryGetValue(key, out IReadOnlyCollection<GuildApplicationCommandPermission> cached)) return cached;
            try {
                var fetched = await CommandPermissionDump.FetchAsync(gateway, guildId);
                cache.Set(key, fetched, CacheDuration);
                return fetched;
            } catch(Exception e) {
                logger.LogWarning(e, "Could not fetch command permission overrides for guild {GuildId}", guildId);
                return [];
            }
        }
    }
}
