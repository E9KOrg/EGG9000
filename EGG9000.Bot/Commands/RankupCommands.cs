using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Helpers;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    [Group("a", "Admin commands")]
    [StaffOnly(StaffTier.CluckingCoordinator)]
    public class RankupModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {
        private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? s : s.Length <= n ? s : s[..(n - 1)] + "…";

        private static string ScopeName(int oom) =>
            oom == RankupMessage.GlobalPool ? "Global" : RankRegistry.ForOom(oom).DisplayName;

        private static async Task<Guild> LoadGuild(ApplicationDbContext db, ulong? gid) =>
            gid is null ? null : await db.Guilds.FirstOrDefaultAsync(g => g.Id == gid || g.OverflowServersJson.Contains(gid.Value.ToString()));

        private static SelectMenuBuilder NavMenu() =>
            new SelectMenuBuilder().WithCustomId("RuNav").WithPlaceholder("Rank-up section...")
                .AddOption("Overview", "overview").AddOption("Toggles", "toggles")
                .AddOption("Message Pools", "groups").AddOption("Notify Filter", "filter");

        private static ButtonBuilder BackTo(string target, string label = "← Back") =>
            new ButtonBuilder().WithLabel(label).WithCustomId($"RuBack:{target}").WithStyle(ButtonStyle.Secondary);

        private static async Task<(Embed, MessageComponent)> BuildViewAsync(ApplicationDbContext db, string section, Guild g, string payload = null) {
            var cb = new ComponentBuilder();
            var eb = new EmbedBuilder().WithAuthor("Rank-up Messages").WithColor(Color.Gold);

            switch(section) {
                case "toggles": {
                    cb.WithButton($"Messages Enabled: {(g.RankupMessagesEnabled ? "ON" : "off")}", customId: "RuToggle:RankupMessagesEnabled", style: g.RankupMessagesEnabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0);
                    cb.WithButton($"Exclusive Group Pool: {(g.RankupExclusivePool ? "ON" : "off")}", customId: "RuToggle:RankupExclusivePool", style: g.RankupExclusivePool ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0);
                    cb.WithButton(BackTo("overview"), row: 1);
                    eb.WithTitle("Toggles").WithDescription(
                        "**Messages Enabled** - master switch for rank-up announcements.\n" +
                        "**Exclusive Group Pool** - when a group has its own messages, don't mix in the global pool.");
                    break;
                }
                case "filter": {
                    var leads = RankRegistry.GroupLeads.ToList();
                    var pick = new SelectMenuBuilder().WithCustomId("RuFilter").WithPlaceholder("Groups that announce...")
                        .WithMinValues(0).WithMaxValues(leads.Count);
                    foreach(var lead in leads)
                        pick.AddOption(lead.DisplayName, lead.GroupBase.ToString(), isDefault: !g.RankupDisabledGroups.Contains(lead.GroupBase));
                    cb.WithSelectMenu(pick, row: 0);
                    cb.WithButton(BackTo("overview"), row: 1);
                    eb.WithTitle("Notify Filter").WithDescription("Selected groups announce rank-ups; unselected groups stay silent. (This is on top of which roles your server actually creates.)");
                    break;
                }
                case "pool": {
                    var scope = int.Parse(payload);
                    var messages = await db.RankupMessages.Where(m => m.GuildId == g.Id && m.GroupBaseOom == scope).ToListAsync();
                    if(messages.Count > 0) {
                        var pick = new SelectMenuBuilder().WithCustomId("RuPickMsg").WithPlaceholder("Pick a message to edit or remove...");
                        foreach(var (m, i) in messages.Take(25).Select((m, i) => (m, i)))
                            pick.AddOption($"#{i + 1}: {Trunc(m.Text, 90)}", m.InternalId);
                        cb.WithSelectMenu(pick, row: 0);
                    }
                    cb.WithButton("Add Message", customId: $"RuAdd:{scope}", style: ButtonStyle.Success, row: 1);
                    cb.WithButton(BackTo("groups", "← Pools"), row: 1);
                    eb.WithTitle($"{ScopeName(scope)} pool")
                      .WithDescription(messages.Count == 0
                          ? "_No custom messages for this scope. Without any, the palace defaults are used._"
                          : string.Join("\n", messages.Take(25).Select((m, i) => $"**#{i + 1}** {Trunc(m.Text, 120)}")));
                    break;
                }
                case "detail": {
                    var msg = await db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == payload);
                    if(msg is null) { return await BuildViewAsync(db, "groups", g); }
                    cb.WithButton("Edit", customId: $"RuEditBtn:{msg.InternalId}", style: ButtonStyle.Primary, row: 0);
                    cb.WithButton("Delete", customId: $"RuDelBtn:{msg.InternalId}", style: ButtonStyle.Danger, row: 0);
                    cb.WithButton(BackTo("pool:" + msg.GroupBaseOom, "← Pool"), row: 1);
                    eb.WithTitle($"{ScopeName(msg.GroupBaseOom)} message").WithDescription(Trunc(msg.Text, 4000));
                    break;
                }
                case "groups": {
                    var pick = new SelectMenuBuilder().WithCustomId("RuPickGroup").WithPlaceholder("Pick a pool to edit...");
                    pick.AddOption("Global (all ranks)", RankupMessage.GlobalPool.ToString());
                    foreach(var lead in RankRegistry.GroupLeads)
                        pick.AddOption(lead.DisplayName, lead.GroupBase.ToString());
                    cb.WithSelectMenu(pick, row: 0);
                    cb.WithButton(BackTo("overview"), row: 1);
                    eb.WithTitle("Message Pools").WithDescription("Pick the Global pool or a rank group to edit its messages. Tokens: `{{user}}` `{{rank}}` `{{eb}}` `{{oom}}` `{{emoji:name}}` `{{command:name}}`.");
                    break;
                }
                default: {
                    var total = await db.RankupMessages.CountAsync(m => m.GuildId == g.Id);
                    cb.WithSelectMenu(NavMenu());
                    eb.WithTitle("Overview").WithDescription("Customize this server's rank-up announcements.");
                    eb.AddField("Enabled", g.RankupMessagesEnabled ? "Yes" : "No", inline: true);
                    eb.AddField("Exclusive group pool", g.RankupExclusivePool ? "Yes" : "No", inline: true);
                    eb.AddField("Silenced groups", g.RankupDisabledGroups.Count.ToString(), inline: true);
                    eb.AddField("Custom messages", total.ToString(), inline: true);
                    break;
                }
            }
            return (eb.Build(), cb.Build());
        }

        [SlashCommand("rankup", "Customize this server's rank-up announcements")]
        public async Task Rankup() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(g is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find this server's config record."); });
                return;
            }
            var (embed, components) = await BuildViewAsync(Db, "overview", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuNav")]
        public async Task RuNav(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = await BuildViewAsync(Db, values.FirstOrDefault() ?? "overview", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuBack:*", ignoreGroupNames: true)]
        public async Task RuBack(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (section, payload) = SplitFirst(string.IsNullOrEmpty(data) ? "overview" : data);
            var (embed, components) = await BuildViewAsync(Db, section, g, payload);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuToggle:*")]
        public async Task RuToggle(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(data == nameof(Guild.RankupMessagesEnabled)) g.RankupMessagesEnabled = !g.RankupMessagesEnabled;
            else if(data == nameof(Guild.RankupExclusivePool)) g.RankupExclusivePool = !g.RankupExclusivePool;
            await Db.SaveChangesAsync();
            var (embed, components) = await BuildViewAsync(Db, "toggles", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuFilter")]
        public async Task RuFilter(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var enabled = values.Select(int.Parse).ToHashSet();
            g.RankupDisabledGroups = [.. RankRegistry.GroupLeads.Select(l => l.GroupBase).Where(b => !enabled.Contains(b))];
            await Db.SaveChangesAsync();
            var (embed, components) = await BuildViewAsync(Db, "filter", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuPickGroup")]
        public async Task RuPickGroup(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = await BuildViewAsync(Db, "pool", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuPickMsg")]
        public async Task RuPickMsg(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = await BuildViewAsync(Db, "detail", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("RuAdd:*")]
        public async Task RuAdd(string data) {
            var modal = new ModalBuilder().WithTitleSafe("New rank-up message").WithCustomId($"RuMsgModal:new:{data}")
                .AddTextInputSafe("Message", customId: "text", TextInputStyle.Paragraph, placeholder: "Use {{user}} {{rank}} {{eb}} {{oom}} {{emoji:name}}", required: true, maxLength: 1500)
                .Build();
            await Context.Interaction.RespondWithModalAsync(modal);
        }

        [ComponentInteraction("RuEditBtn:*")]
        public async Task RuEditBtn(string data) {
            var msg = await Db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == data);
            if(msg is null) { await Context.Interaction.DeferAsync(); return; }
            var modal = new ModalBuilder().WithTitleSafe("Edit rank-up message").WithCustomId($"RuMsgModal:edit:{data}")
                .AddTextInputSafe("Message", customId: "text", TextInputStyle.Paragraph, value: Trunc(msg.Text, 1500), required: true, maxLength: 1500)
                .Build();
            await Context.Interaction.RespondWithModalAsync(modal);
        }

        [ComponentInteraction("RuDelBtn:*")]
        public async Task RuDelBtn(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var msg = await Db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == data && m.GuildId == g.Id);
            var scope = msg?.GroupBaseOom ?? RankupMessage.GlobalPool;
            if(msg is not null) {
                Db.RankupMessages.Remove(msg);
                await Db.SaveChangesAsync();
                Db.InvalidateRankupMessages(g);
            }
            var (embed, components) = await BuildViewAsync(Db, "pool", g, scope.ToString());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ModalInteraction("RuMsgModal:*", ignoreGroupNames: true)]
        public async Task RuMsgModal(string data, RankupMessageModal form) {
            var modal = (SocketModal)Context.Interaction;
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (op, arg) = SplitFirst(data);
            var text = form.Text?.Trim() ?? "";
            string section = "pool";
            int scope = RankupMessage.GlobalPool;
            if(op == "edit") {
                var msg = await Db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == arg && m.GuildId == g.Id);
                if(msg is null) {
                    section = "groups";
                } else {
                    msg.Text = text;
                    scope = msg.GroupBaseOom;
                    await Db.SaveChangesAsync();
                    Db.InvalidateRankupMessages(g);
                }
            } else {
                scope = int.Parse(arg);
                Db.RankupMessages.Add(new RankupMessage {
                    InternalId = Guid.NewGuid().ToString("N"),
                    GuildId = g.Id,
                    GuildName = g.Name,
                    GroupBaseOom = scope,
                    Text = text,
                    Weight = 1,
                    CreatedById = Context.User.Id,
                    CreatedBy = Context.User.Username
                });
                await Db.SaveChangesAsync();
                Db.InvalidateRankupMessages(g);
            }
            var (embed, components) = await BuildViewAsync(Db, section, g, section == "pool" ? scope.ToString() : null);
            await modal.UpdateAsync(x => { x.Embed = embed; x.Components = components; });
        }

        private static (string, string) SplitFirst(string s) {
            var i = s.IndexOf(':');
            return i < 0 ? (s, null) : (s[..i], s[(i + 1)..]);
        }
    }

    public class RankupMessageModal : IModal {
        public string Title => "Rank-up message";

        [InputLabel("Message")]
        [ModalTextInput("text", TextInputStyle.Paragraph, maxLength: 1500)]
        public string Text { get; set; }
    }
}
