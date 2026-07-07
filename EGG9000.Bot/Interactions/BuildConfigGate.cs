using Discord.Interactions;
using EGG9000.Common.Helpers;
using System;
using System.Linq;

namespace EGG9000.Bot.Interactions {
    // Marks an InteractionModuleBase module as only registerable in specific BuildConfigurations.
    // InteractionRoutingService reads this via reflection before RegisterCommandsGloballyAsync and
    // removes disallowed modules from the InteractionService entirely, so the command never appears
    // in Discord's slash picker outside the allowed configs - unlike a precondition (e.g. StaffOnly),
    // which only blocks execution after the command is already visible and invoked.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BuildConfigOnlyAttribute(params BuildConfiguration[] allowed) : Attribute {
        public BuildConfiguration[] Allowed { get; } = allowed;

        public bool AllowsCurrent => Allowed.Contains(BuildConfig.Current);
    }
}
