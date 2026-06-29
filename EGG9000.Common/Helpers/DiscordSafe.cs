using Discord;

namespace EGG9000.Common.Helpers {
    // Discord enforces hard length limits on component text. Exceeding any of them throws
    // ArgumentException mid-build and surfaces to the user as an "Internal Error". These helpers clamp
    // the value to the limit so a long (often user-derived) string auto-shortens instead of crashing
    // the interaction. Prefer the *Safe variants anywhere a label/title/option text is built from data.
    public static class DiscordSafe {
        public const int ButtonLabel = 80;
        public const int SelectOptionLabel = 100;
        public const int SelectOptionDescription = 100;
        public const int TextInputLabel = 45;
        public const int TextInputPlaceholder = 100;
        public const int TextInputValue = 4000;
        public const int ModalTitle = 45;
        public const int CustomId = 100;

        public static ModalBuilder WithTitleSafe(this ModalBuilder modal, string title) =>
            modal.WithTitle(title.Truncate(ModalTitle));

        public static ModalBuilder AddTextInputSafe(this ModalBuilder modal, string label, string customId,
            TextInputStyle style = TextInputStyle.Short, string placeholder = null, int? minLength = null,
            int? maxLength = null, bool? required = null, string value = null) =>
            modal.AddTextInput(
                label.Truncate(TextInputLabel),
                customId.Truncate(CustomId),
                style,
                placeholder.Truncate(TextInputPlaceholder),
                minLength, maxLength, required,
                value.Truncate(TextInputValue));

        public static ComponentBuilder WithButtonSafe(this ComponentBuilder builder, string label, string customId,
            ButtonStyle style = ButtonStyle.Primary, IEmote emote = null, string url = null, bool disabled = false, int row = 0) =>
            builder.WithButton(label.Truncate(ButtonLabel), customId.Truncate(CustomId), style, emote, url, disabled, row);

        public static SelectMenuOptionBuilder SafeOption(string label, string value, string description = null, IEmote emote = null, bool? isDefault = null) =>
            new(label.Truncate(SelectOptionLabel), value.Truncate(CustomId), description.Truncate(SelectOptionDescription), emote, isDefault);
    }
}
