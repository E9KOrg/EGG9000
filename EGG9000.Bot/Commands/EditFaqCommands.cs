using Discord;
using Discord.WebSocket;

using EGG9000.Common.Commands;
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
    /// <summary>
    /// `/admin editfaq` - in-Discord FAQ topic editing (the website's FAQ Customization page).
    /// Lists/adds/edits/deletes the guild's <see cref="FAQTopic"/> rows with the same
    /// component+modal UX as <see cref="RankupCommands"/>; writes the same table and invalidates
    /// the same cache. Explanation/preview run through <see cref="MessageFormatter"/>.
    /// </summary>
    public static class EditFaqCommands {
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
            new ModalBuilder().WithTitleSafe("FAQ Topic").WithCustomId(customId)
                .AddTextInputSafe("Name", customId: "name", value: existing?.Name, required: true, maxLength: 100)
                .AddTextInputSafe("Keywords (comma-separated)", customId: "keywords", value: existing is null ? null : string.Join(", ", KeywordsOf(existing)), required: false, maxLength: 300)
                .AddTextInputSafe("Explanation", customId: "explanation", TextInputStyle.Paragraph, value: existing?.Explanation, required: true, maxLength: 1024)
                .AddTextInputSafe("Embed color (6-hex, optional)", customId: "color", value: existing?.EmbedColorHex, required: false, maxLength: 7)
                .AddTextInputSafe("Image URL (optional)", customId: "image", value: existing?.ImageUrl, required: false, maxLength: 400);

        [SlashCommand(Description = "Edit this server's FAQ topics", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "admin")]
        public static async Task EditFaq(FauxCommand command, ApplicationDbContext db, DiscordHostedService client) {
            await command.DeferAsync(ephemeral: true);
            var g = await LoadGuild(db, command.GuildId);
            if(g is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find this server's config record."); });
                return;
            }
            var (embed, components) = await BuildViewAsync(db, client, "list", g);
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeBack(SocketMessageComponent component, ApplicationDbContext db, DiscordHostedService client) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = await BuildViewAsync(db, client, "list", g);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FePick(SocketMessageComponent component, ApplicationDbContext db, DiscordHostedService client) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = await BuildViewAsync(db, client, "detail", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeAdd(SocketMessageComponent component) {
            await component.RespondWithModalAsync(TopicModal("FeModal:new", null).Build());
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeEditText(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data);
            if(t is null) { await component.DeferAsync(); return; }
            await component.RespondWithModalAsync(TopicModal($"FeModal:edit:{data}", t).Build());
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeStaff(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService client) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null) { t.StaffOnly = !t.StaffOnly; await db.SaveChangesAsync(); db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(db, client, "detail", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FePalace(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService client) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null && IsPalaceGuild(g)) { t.PalaceOnly = !t.PalaceOnly; await db.SaveChangesAsync(); db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(db, client, "detail", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeWUp(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService client) =>
            await AdjustWeight(component, data, db, client, +1);

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeWDn(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService client) =>
            await AdjustWeight(component, data, db, client, -1);

        private static async Task AdjustWeight(SocketMessageComponent component, string data, ApplicationDbContext db, DiscordHostedService client, int delta) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null) { t.Weight += delta; await db.SaveChangesAsync(); db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(db, client, "detail", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeDel(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService client) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == data && x.GuildId == g.Id);
            if(t is not null) { db.FAQTopics.Remove(t); await db.SaveChangesAsync(); db.InvalidateFAQTopics(g); }
            var (embed, components) = await BuildViewAsync(db, client, "list", g);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [Modal(AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task FeModal(SocketModal modal, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService client) {
            var g = await LoadGuild(db, modal.GuildId);
            var (op, id) = SplitFirst(data);

            string Field(string cid) => modal.Data.Components.FirstOrDefault(c => c.CustomId == cid)?.Value?.Trim() ?? "";
            var keywords = EditFaqValidation.ParseKeywords(Field("keywords"));
            var kwError = EditFaqValidation.ValidateKeywords(keywords);
            var (color, colorError) = EditFaqValidation.NormalizeColor(Field("color"));
            var error = kwError ?? colorError;

            string section = "list";
            string detailId = null;

            if(error is null) {
                FAQTopic t;
                if(op == "edit") {
                    t = await db.FAQTopics.FirstOrDefaultAsync(x => x.InternalId == id && x.GuildId == g.Id);
                } else {
                    t = new FAQTopic { InternalId = Guid.NewGuid().ToString("N"), GuildId = g.Id, GuildName = g.Name, CreatedById = modal.User.Id, CreatedBy = modal.User.Username, Weight = 0 };
                    db.FAQTopics.Add(t);
                }
                if(t is not null) {
                    t.Name = Field("name");
                    t._keywords = JsonConvert.SerializeObject(keywords);
                    t.Explanation = Field("explanation");
                    t.EmbedColorHex = color;
                    t.ImageUrl = Field("image");
                    await db.SaveChangesAsync();
                    db.InvalidateFAQTopics(g);
                    section = "detail";
                    detailId = t.InternalId;
                }
            }

            var (embed, components) = await BuildViewAsync(db, client, section, g, detailId);
            await modal.UpdateAsync(x => { x.Content = error is null ? "" : $"⚠️ {error}"; x.Embed = embed; x.Components = components; });
        }

        private static (string, string) SplitFirst(string s) {
            var i = s.IndexOf(':');
            return i < 0 ? (s, null) : (s[..i], s[(i + 1)..]);
        }
    }
}
