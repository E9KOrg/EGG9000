using Discord;
using Discord.WebSocket;
using EGG9000.Common.Helpers.Discord;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public static class InteractionExtensions {
        public static async Task<IUserMessage> RespondAsyncGettingMessage(this SocketInteraction i, string content = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null) {
            if(i.HasResponded)
                return await i.ModifyOriginalResponseAsync(x => { x.Content = content; x.Embeds = embeds; x.AllowedMentions = allowedMentions; x.Components = components; x.Embed = embed; });
            await i.RespondAsync(content, embeds, isTTS, ephemeral, allowedMentions, components, embed, options);
            return await i.GetOriginalResponseAsync();
        }

        public static async Task<IUserMessage> ModifyOriginalResponseAsync(this SocketInteraction i, string content) =>
            await i.ModifyOriginalResponseAsync(x => x.Content = content);

        public static async Task<IUserMessage> RespondWithFilesAsyncGettingMessage(this SocketInteraction i, IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            if(i.HasResponded)
                return await i.ModifyOriginalResponseAsync(o => { o.Content = text; o.Embeds = embeds; o.AllowedMentions = allowedMentions; o.Components = components; o.Embed = embed; o.Attachments = attachments.ToList(); });
            await i.RespondWithFilesAsync(attachments, text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll);
            return await i.GetOriginalResponseAsync();
        }

        public static async Task RespondWithPremiumRequiredAsync(this SocketInteraction i, RequestOptions options = null) =>
            await i.RespondAsyncGettingMessage("", embed: EmbedHelpers.MakeCustomEmbed(EmbedHelpers.EmbedType.Error, "How did you get here...?", "Nothing in E9K is behind a paywall. If you're seeing this, there's been an error."), options: options);

        public static async Task DeleteResponseFix(this SocketInteraction i) {
            var response = await i.GetOriginalResponseAsync();
            if(response is null) return;
            await response.DeleteAsync();
        }
    }
}
