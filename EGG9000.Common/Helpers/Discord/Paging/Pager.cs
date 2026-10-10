using Discord;
using Discord.WebSocket;

using EGG9000.Common.Helpers.Discord;

using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Common.Helpers.Discord.Paging {
    public abstract class Pager {
        public int Page { get; protected set; }
        public abstract int PageCount { get; }
        protected abstract string CustomIdPrefix { get; }
        protected abstract string KeySuffix { get; }

        protected Pager(int page) {
            Page = page;
        }

        public abstract Task<Embed> RenderEmbedAsync();

        private string CustomId(int page) => $"{CustomIdPrefix}:{KeySuffix},{page}";

        protected ButtonBuilder PrevButton(string label = "◀") =>
            new ButtonBuilder(label, CustomId(Page - 1), ButtonStyle.Secondary).WithDisabled(Page <= 0);

        protected ButtonBuilder NextButton(string label = "▶") =>
            new ButtonBuilder(label, CustomId(Page + 1), ButtonStyle.Secondary).WithDisabled(Page >= PageCount - 1);

        public virtual MessageComponent BuildComponents() {
            if(PageCount <= 1) return null;
            return new ComponentBuilder().WithButton(PrevButton()).WithButton(NextButton()).Build();
        }

        public async Task<(Embed Embed, MessageComponent Components)> RenderAsync() =>
            (await RenderEmbedAsync(), BuildComponents());

        public async Task SendAsync(SocketInteraction interaction, bool ephemeral = false) {
            var (embed, components) = await RenderAsync();
            await interaction.RespondAsyncGettingMessage(embed: embed, components: components, ephemeral: ephemeral);
        }

        public async Task UpdateComponentAsync(SocketMessageComponent component) {
            var (embed, components) = await RenderAsync();
            await component.UpdateAsync(x => { x.Embed = embed; x.Components = components; });
        }

        public static async Task RejectNonInvokerAsync(SocketMessageComponent component) {
            if(component.HasResponded)
                await component.ModifyOriginalResponseAsync(x => { x.Content = null; x.Embed = EmbedError("This wasn't yours to run - don't click others' commands!"); x.Components = null; });
            else
                await component.RespondAsync(embed: EmbedError("This wasn't yours to run - don't click others' commands!"), ephemeral: true);
        }
    }
}
