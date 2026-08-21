using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord {
    public static class CommandPermissionDump {

        // Discord.NET only exposes per-command permission reads via RestGuildCommand, which globally-registered commands aren't.
        // The guild-wide batch read (GET /guilds/{id}/commands/permissions) lives on the internal InteractionHelper, so we can reach it by reflection.
        private static readonly MethodInfo GetGuildCommandPermissions = typeof(BaseDiscordClient).Assembly
            .GetType("Discord.Rest.InteractionHelper")
            .GetMethod("GetGuildCommandPermissionsAsync", BindingFlags.Public | BindingFlags.Static);

        public static async Task<IReadOnlyCollection<GuildApplicationCommandPermission>> FetchAsync(DiscordSocketClient gateway, ulong guildId) {
            var task = (Task)GetGuildCommandPermissions.Invoke(null, [gateway.Rest, guildId, null]);
            await task.ConfigureAwait(false);
            return (IReadOnlyCollection<GuildApplicationCommandPermission>)((dynamic)task).Result;
        }

        public static async Task<string> BuildReportAsync(DiscordSocketClient gateway, ulong guildId) {
            var overrides = await FetchAsync(gateway, guildId);
            var globals = await gateway.GetGlobalApplicationCommandsAsync();
            var nameById = globals.ToDictionary(c => c.Id, c => c.Name);

            var sb = new StringBuilder();
            sb.AppendLine($"Command permission overrides for guild {guildId}");
            sb.AppendLine("Commands with no entry below use their default permissions.");
            sb.AppendLine();

            foreach(var cmd in overrides.OrderBy(o => nameById.GetValueOrDefault(o.CommandId, o.CommandId.ToString()))) {
                var name = nameById.TryGetValue(cmd.CommandId, out var n) ? $"/{n}" : $"(unknown id {cmd.CommandId})";
                sb.AppendLine(name);
                foreach(var p in cmd.Permissions) {
                    var target = p.TargetType switch {
                        ApplicationCommandPermissionTarget.Role => p.TargetId == guildId ? "@everyone" : $"role {p.TargetId}",
                        ApplicationCommandPermissionTarget.User => $"user {p.TargetId}",
                        ApplicationCommandPermissionTarget.Channel => $"channel {p.TargetId}",
                        _ => p.TargetId.ToString()
                    };
                    sb.AppendLine($"    {target}: {(p.Permission ? "allow" : "deny")}");
                }
                sb.AppendLine();
            }

            if(overrides.Count == 0) sb.AppendLine("(no overrides set on this server)");
            return sb.ToString();
        }
    }
}
