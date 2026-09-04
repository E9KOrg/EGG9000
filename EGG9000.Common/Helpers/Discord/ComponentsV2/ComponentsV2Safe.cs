using System;

namespace Discord {
    // ComponentsV2 equivalent of EGG9000.Common.Helpers.DiscordSafe - truncation guards for Discord's
    // ComponentsV2 length/count limits, plus a guard against the one-way-flag hazard: once a message
    // has MessageFlags.ComponentsV2 set, Discord won't unset it on a later edit, and Content/Embed
    // fields on that edit are silently ignored (not errored). Every reply into an already-V2 message
    // must also be V2 - ThrowIfModeMismatch turns that silent drop into a loud dev-time exception.
    public static class ComponentsV2Safe {
        public const int TextDisplayMax = 4000;
        public const int ComponentsPerMessageMax = 40;

        public sealed class MessageTextBudget {
            private int _remaining = TextDisplayMax;
            public int Remaining => _remaining;
            public string Consume(string content) {
                if(string.IsNullOrEmpty(content) || _remaining <= 0) return "";
                var truncated = content.Length <= _remaining ? content : content[.._remaining];
                _remaining -= truncated.Length;
                return truncated;
            }
        }

        public static T WithTextDisplaySafe<T>(this T container, string content, int? id = null) where T : class, IComponentContainer, IStaticComponentContainer =>
            container.WithTextDisplay(content.Truncate(TextDisplayMax), id);

        public static T WithTextDisplaySafe<T>(this T container, string content, MessageTextBudget budget, int? id = null) where T : class, IComponentContainer, IStaticComponentContainer =>
            container.WithTextDisplay(budget.Consume(content), id);

        public static T WithHeaderSafe<T>(this T container, string title, string accountLine = null) where T : class, IComponentContainer, IStaticComponentContainer {
            var header = accountLine is null ? $"# {title}" : $"# {title}\n{accountLine}";
            return container.WithTextDisplaySafe(header);
        }

        public static T WithHeaderSafe<T>(this T container, string title, MessageTextBudget budget, string accountLine = null) where T : class, IComponentContainer, IStaticComponentContainer {
            var header = accountLine is null ? $"# {title}" : $"# {title}\n{accountLine}";
            return container.WithTextDisplaySafe(header, budget);
        }

        public static bool IsComponentsV2(this MessageFlags? flags) =>
            flags?.HasFlag(MessageFlags.ComponentsV2) ?? false;

        public static bool IsComponentsV2(this IMessage message) => message.Flags.IsComponentsV2();

        public static void ThrowIfModeMismatch(this IMessage message, bool sendingComponentsV2) {
            if(message.IsComponentsV2() && !sendingComponentsV2)
                throw new InvalidOperationException(
                    $"Message {message.Id} is already ComponentsV2; a non-V2 reply's Content/Embed would be silently ignored by Discord. Send with flags: MessageFlags.ComponentsV2 instead.");
        }
    }
}
