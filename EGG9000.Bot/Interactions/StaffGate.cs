using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using System;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public enum StaffTier {
        None = 0,
        ChickenTender = 1,
        FarmHand = 2,
        CluckingCoordinator = 3,
        Admin = 4,
    }

    // Drop-in replacement for the legacy `[ComponentCommand(AdminOnly = StaffOnlyLevel.X)]` /
    // `[Modal(AdminOnly = X)]` perm bars. Discord.NET InteractionService only enforces
    // `[DefaultMemberPermissions]` at the slash-command top level - posted buttons / modals are
    // unprotected. Stick this on `[ComponentInteraction]` / `[ModalInteraction]` / `[SlashCommand]`
    // methods (or modules) and the runtime rejects the interaction before the handler runs.
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class StaffOnlyAttribute(StaffTier tier) : PreconditionAttribute {
        public StaffTier Tier { get; } = tier;

        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services) {
            if(Tier == StaffTier.None) return Task.FromResult(PreconditionResult.FromSuccess());
            if(context.User is not SocketGuildUser gu)
                return Task.FromResult(PreconditionResult.FromError("Staff-only commands cannot be used in DMs."));
            var required = Tier switch {
                StaffTier.Admin => GuildPermission.Administrator,
                StaffTier.CluckingCoordinator => GuildPermission.ManageChannels,
                StaffTier.FarmHand => GuildPermission.CreatePrivateThreads,
                StaffTier.ChickenTender => GuildPermission.ModerateMembers,
                _ => GuildPermission.UseApplicationCommands,
            };
            return Task.FromResult(gu.GuildPermissions.Has(required)
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError("You don't have permission to do that."));
        }
    }
}
