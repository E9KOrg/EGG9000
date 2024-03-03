using Discord;

namespace EGG9000.Common.Helpers.Discord {
    public class EmbedHelpers {
        public static Embed EmbedInProgress(string text) {
            return new EmbedBuilder().WithColor(Color.Blue).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Please wait...").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedSuccess(string text) {
            return new EmbedBuilder().WithColor(Color.Green).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Success").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedWarning(string warningText) {
            return new EmbedBuilder().WithColor(Color.LightOrange).WithDescription(warningText).WithAuthor(new EmbedAuthorBuilder().WithName("Warning").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedError(string errorText) {
            return new EmbedBuilder().WithColor(Color.Red).WithDescription(errorText).WithAuthor(new EmbedAuthorBuilder().WithName("Error").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedInternalError(string errorText) {
            return new EmbedBuilder().WithColor(Color.Red).WithDescription(errorText).WithAuthor(new EmbedAuthorBuilder().WithName("Internal Error").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }
    }
}
