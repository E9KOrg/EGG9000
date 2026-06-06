using Discord;
using Discord.WebSocket;

using EGG9000.Common.Factories;

using Microsoft.Extensions.Logging;

using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Helpers {
    public static class RoleToggle {
        public enum RoleAction { None, Add, Remove }

        public static RoleAction Decide(bool hasRole, bool shouldHave, bool canAdd = true) {
            if(!hasRole && shouldHave && canAdd) return RoleAction.Add;
            if(hasRole && !shouldHave) return RoleAction.Remove;
            return RoleAction.None;
        }

        public static async Task ApplyAsync(IGuildUser user, SocketRole role, bool shouldHave, string logLabel = null, bool canAdd = true) {
            if(role is null) return;
            var hasRole = user.RoleIds.Any(x => x == role.Id);
            switch(Decide(hasRole, shouldHave, canAdd)) {
                case RoleAction.Add:
                    await user.AddRoleAsync(role);
                    if(logLabel != null) StaticLoggerFactory.GetLogger<DiscordHelpers>().LogInformation("Adding {label} for {user}", logLabel, user.GetName());
                    break;
                case RoleAction.Remove:
                    await user.RemoveRoleAsync(role);
                    if(logLabel != null) StaticLoggerFactory.GetLogger<DiscordHelpers>().LogInformation("Removing {label} for {user}", logLabel, user.GetName());
                    break;
            }
        }
    }
}
