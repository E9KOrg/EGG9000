using Discord;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord.Paging {
    public abstract class TextListPager : Pager {
        protected readonly IReadOnlyList<string> Lines;
        private readonly List<int> _pageStarts;

        protected TextListPager(IReadOnlyList<string> lines, int requestedPage, int maxCharsPerPage = 1000) : base(0) {
            Lines = lines;
            _pageStarts = ComputePageStarts(lines, maxCharsPerPage);
            Page = Math.Clamp(requestedPage, 0, _pageStarts.Count - 1);
        }

        protected abstract string Title { get; }
        protected virtual string Preamble => null;
        protected virtual string WrapBody(string body) => body;
        protected virtual Color EmbedColor => Color.Default;

        public override int PageCount => _pageStarts.Count;

        public override Task<Embed> RenderEmbedAsync() {
            var start = _pageStarts[Page];
            var end = Page + 1 < _pageStarts.Count ? _pageStarts[Page + 1] : Lines.Count;
            var body = WrapBody(string.Join("\n", Lines.Skip(start).Take(end - start)));
            var description = Preamble is null ? body : $"{Preamble}\n{body}";
            var builder = new EmbedBuilder().WithTitle(Title).WithDescription(description).WithColor(EmbedColor);
            if(PageCount > 1) builder.WithFooter(new EmbedFooterBuilder().WithText($"Page {Page + 1}/{PageCount}"));
            return Task.FromResult(builder.Build());
        }

        private static List<int> ComputePageStarts(IReadOnlyList<string> lines, int maxCharsPerPage) {
            var starts = new List<int> { 0 };
            var running = 0;
            for(var i = 0; i < lines.Count; i++) {
                var lineLen = lines[i].Length + 1;
                if(i > starts[^1] && running + lineLen > maxCharsPerPage) {
                    starts.Add(i);
                    running = 0;
                }
                running += lineLen;
            }
            return starts;
        }
    }
}
