using Discord;
using EGG9000.Common.Commands;

namespace EGG9000.Bot.Interactions {
    public static class StaffPermissions {
        public static GuildPermission For(StaffOnlyLevel level) => level switch {
            StaffOnlyLevel.Admin => GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles,
            StaffOnlyLevel.CluckingCoordinator => GuildPermission.ManageChannels,
            StaffOnlyLevel.FarmHand => GuildPermission.CreatePrivateThreads,
            StaffOnlyLevel.ChickenTender => GuildPermission.ModerateMembers,
            _ => GuildPermission.UseApplicationCommands
        };
    }
}
