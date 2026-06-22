using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    [Group("a", "Admin commands")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class EditFaqModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client) : E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;

        private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? s : s.Length <= n ? s : s[..(n - 1)] + "…";

        private static bool IsPalaceGuild(Guild g) {
#if DEV9002
            return g.DiscordSeverId == 1108127105088241746;
#else
            return g.DiscordSeverId == 656455567858073601;
#endif
        }

        private static async Task<Guild> LoadGuild(ApplicationDbContext db, ulong? gid) =>
            gid is null ? null : await db.Guilds.FirstOrDefaultAsync(g => g.Id == gid || g.OverflowServersJson.Contains(gid.Value.ToString()));

        private static List<string> KeywordsOf(FAQTopic t) {
            try { return t.Keywords ?? []; } catch { return []; }
        }

        private static ButtonBuilder BackToList() =>
            new ButtonBuilder().WithLabel("← Topics").WithCustomId("FeBack").WithStyle(ButtonStyle.Secondary);

        private static async Task<(Embed, MessageComponent)> BuildViewAsync(ApplicationDbContext db, DiscordHostedService client, string section, Guild g, string payload = null) {
            var cb = new ComponentBuilder();
            var eb = new EmbedBuilder().WithAuthor("FAQ Topics").WithColor(Color.Teal);

            if(section == "detail") {
                var t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == payload && x.GuildId == g.Id);
                if(t is null) { return await BuildViewAsync(db, client, "list", g); }

                cb.WithButton("Edit Text", customId: $"FeEditText:{t.InternalId}", style: ButtonStyle.Primary, row: 0);
                cb.WithButton($"Staff-Only: {(t.StaffOnly ? "ON" : "off")}", customId: $"FeStaff:{t.InternalId}", style: t.StaffOnly ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0);
                if(IsPalaceGuild(g))
                    cb.WithButton($"Palace-Only: {(t.PalaceOnly ? "ON" : "off")}", customId: $"FePalace:{t.InternalId}", style: t.PalaceOnly ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0);
                cb.WithButton("Weight -", customId: $"FeWDn:{t.InternalId}", style: ButtonStyle.Secondary, row: 1);
                cb.WithButton("Weight +", customId: $"FeWUp:{t.InternalId}", style: ButtonStyle.Secondary, row: 1);
                cb.WithButton("Delete", customId: $"FeDel:{t.InternalId}", style: ButtonStyle.Danger, row: 1);
                cb.WithButton(BackToList(), row: 2);

                var preview = await MessageFormatter.FormatAsync(t.Explanation, client, g.Id);
                eb.WithTitle(Trunc(t.Name, 250))
                  .WithDescription(Trunc(preview, 2000))
                  .AddField("Keywords", KeywordsOf(t).Count > 0 ? string.Join(", ", KeywordsOf(t)) : "_none_", inline: true)
                  .AddField("Weight", t.Weight.ToString(), inline: true)
                  .AddField("Color", string.IsNullOrEmpty(t.EmbedColorHex) ? "_default_" : $"#{t.EmbedColorHex}", inline: true);
                if(!string.IsNullOrEmpty(t.ImageUrl)) eb.WithThumbnailUrl(t.ImageUrl);
                return (eb.Build(), cb.Build());
            }

            var topics = await db.FAQTopics.Where(x => x.GuildId == g.Id).OrderByDescending(x => x.Weight).ToListAsync();
            if(topics.Count > 0) {
                var pick = new SelectMenuBuilder().WithCustomId("FePick").WithPlaceholder("Pick a topic to edit...");
                foreach(var t in topics.Take(25))
                    pick.AddOption(Trunc(string.IsNullOrEmpty(t.Name) ? "(unnamed)" : t.Name, 100), t.InternalId, description: Trunc(t.Explanation, 100));
                cb.WithSelectMenu(pick, row: 0);
            }
            cb.WithButton("Add Topic", customId: "FeAdd", style: ButtonStyle.Success, row: 1);

            eb.WithTitle("FAQ Topics").WithDescription(
                topics.Count == 0 ? "_No topics yet. Add one below._"
                : $"`{topics.Count}` topic(s){(topics.Count > 25 ? " (showing first 25)" : "")}. Pick one to edit, or add a new one.");
            return (eb.Build(), cb.Build());
        }

        private static ModalBuilder TopicModal(string customId, FAQTopic existing) =>
            new ModalBuilder().WithTitle("FAQ Topic").WithCustomId(customId)
                .AddTextInput("Name", customId: "name", value: existing?.Name, required: true, maxLength: 100)
                .AddTextInput("Keywords (comma-separated)", customId: "keywords", value: existing is null ? null : string.Join(", ", KeywordsOf(existing)), required: false, maxLength: 300)
                .AddTextInput("Explanation", customId: "explanation", TextInputStyle.Paragraph, value: existing?.Explanation, required: true, maxLength: 1024)
                .AddTextInput("Embed color (6-hex, optional)", customId: "color", value: existing?.EmbedColorHex, required: false, maxLength: 7)
                .AddTextInput("Image URL (optional)", customId: "image", value: existing?.ImageUrl, required: false, maxLength: 400);

        [SlashCommand("editfaq", "Edit this server's FAQ topics")]
        public async Task EditFaq() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(g is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find this server's config record."); });
                return;
            }
            var (embed, components) = await BuildViewAsync(Db, _client, "list", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("FeBack")]
        public async Task FeBack() {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = await BuildViewAsync(Db, _client, "list", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("FePick")]
        public async Task FePick(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = await BuildViewAsync(Db, _client, "detail", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("FeAdd")]
        public async Task FeAdd() {
            await Context.Interaction.RespondWithModalAsync(TopicModal("FeModal:new", null).Build());
        }

        [ComponentInteraction("FeEditText:*")]
        public async Task FeEditText(string data) {
            var t = await Db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data);
            if(t is null) { await Context.Interaction.DeferAsync(); return; }
            await Context.Interaction.RespondWithModalAsync(TopicModal($"FeModal:edit:{data}", t).Build());
        }

        [ComponentInteraction("FeStaff:*")]
        public async Task FeStaff(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var t = await Db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null) { t.StaffOnly = !t.StaffOnly; await Db.SaveChangesAsync(); Db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(Db, _client, "detail", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("FePalace:*")]
        public async Task FePalace(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var t = await Db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null && IsPalaceGuild(g)) { t.PalaceOnly = !t.PalaceOnly; await Db.SaveChangesAsync(); Db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(Db, _client, "detail", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("FeWUp:*")]
        public async Task FeWUp(string data) => await AdjustWeight(data, +1);

        [ComponentInteraction("FeWDn:*")]
        public async Task FeWDn(string data) => await AdjustWeight(data, -1);

        private async Task AdjustWeight(string data, int delta) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var t = await Db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null) { t.Weight += delta; await Db.SaveChangesAsync(); Db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(Db, _client, "detail", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("FeDel:*")]
        public async Task FeDel(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var t = await Db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null) { Db.FAQTopics.Remove(t); await Db.SaveChangesAsync(); Db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(Db, _client, "list", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ModalInteraction("FeModal:*")]
        public async Task FeModal(string data, FaqTopicModal form) {
            var modal = (SocketModal)Context.Interaction;
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (op, id) = SplitFirst(data);

            var keywords = EditFaqValidation.ParseKeywords(form.Keywords);
            var kwError = EditFaqValidation.ValidateKeywords(keywords);
            var (color, colorError) = EditFaqValidation.NormalizeColor(form.Color);
            var error = kwError ?? colorError;

            string section = "list";
            string detailId = null;

            if(error is null) {
                FAQTopic t;
                if(op == "edit") {
                    t = await Db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == id && x.GuildId == g.Id);
                } else {
                    t = new FAQTopic { InternalId = Guid.NewGuid().ToString("N"), GuildId = g.Id, GuildName = g.Name, CreatedById = Context.User.Id, CreatedBy = Context.User.Username, Weight = 0 };
                    Db.FAQTopics.Add(t);
                }
                if(t is not null) {
                    t.Name = form.Name;
                    t._keywords = JsonConvert.SerializeObject(keywords);
                    t.Explanation = form.Explanation;
                    t.EmbedColorHex = color;
                    t.ImageUrl = form.Image;
                    await Db.SaveChangesAsync();
                    Db.InvalidateFAQTopics(g);
                    section = "detail";
                    detailId = t.InternalId;
                }
            }

            var (embed, components) = await BuildViewAsync(Db, _client, section, g, detailId);
            await modal.UpdateAsync(x => { x.Content = error is null ? "" : $"⚠️ {error}"; x.Embed = embed; x.Components = components; });
        }

        private static (string, string) SplitFirst(string s) {
            var i = s.IndexOf(':');
            return i < 0 ? (s, null) : (s[..i], s[(i + 1)..]);
        }
    }

    public class FaqTopicModal : IModal {
        public string Title => "FAQ Topic";

        [InputLabel("Name")]
        [ModalTextInput("name", maxLength: 100)]
        public string Name { get; set; }

        [InputLabel("Keywords (comma-separated)")]
        [ModalTextInput("keywords", maxLength: 300)]
        [RequiredInput(false)]
        public string Keywords { get; set; }

        [InputLabel("Explanation")]
        [ModalTextInput("explanation", TextInputStyle.Paragraph, maxLength: 1024)]
        public string Explanation { get; set; }

        [InputLabel("Embed color (6-hex, optional)")]
        [ModalTextInput("color", maxLength: 7)]
        [RequiredInput(false)]
        public string Color { get; set; }

        [InputLabel("Image URL (optional)")]
        [ModalTextInput("image", maxLength: 400)]
        [RequiredInput(false)]
        public string Image { get; set; }
    }
}
