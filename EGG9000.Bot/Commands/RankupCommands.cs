using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
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
    /// <summary>
    /// `/a rankup` - per-guild rank-up announcement customization: master + exclusive-pool
    /// toggles, the per-group notify filter, and the message pools (Global + one per rank
    /// group). Messages are <see cref="RankupMessage"/> rows scoped by GroupBaseOom and
    /// support {{user}} {{rank}} {{eb}} {{oom}} {{emoji:x}} {{command:x}} tokens. Mirrors the
    /// ConfigureCommands component/modal pattern.
    /// </summary>
    public static class RankupCommands {
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

        [SlashCommand(Description = "Customize this server's rank-up announcements", AdminOnly = StaffOnlyLevel.CluckingCoordinator, ParentCommand = "a")]
        public static async Task Rankup(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: true);
            var g = await LoadGuild(db, command.GuildId);
            if(g is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find this server's config record."); });
                return;
            }
            var (embed, components) = await BuildViewAsync(db, "overview", g);
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuNav(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = await BuildViewAsync(db, component.Data.Values.FirstOrDefault() ?? "overview", g);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuBack(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (section, payload) = SplitFirst(string.IsNullOrEmpty(data) ? "overview" : data);
            var (embed, components) = await BuildViewAsync(db, section, g, payload);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuToggle(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            if(data == nameof(Guild.RankupMessagesEnabled)) g.RankupMessagesEnabled = !g.RankupMessagesEnabled;
            else if(data == nameof(Guild.RankupExclusivePool)) g.RankupExclusivePool = !g.RankupExclusivePool;
            await db.SaveChangesAsync();
            var (embed, components) = await BuildViewAsync(db, "toggles", g);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuFilter(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var enabled = component.Data.Values.Select(int.Parse).ToHashSet();
            g.RankupDisabledGroups = [.. RankRegistry.GroupLeads.Select(l => l.GroupBase).Where(b => !enabled.Contains(b))];
            await db.SaveChangesAsync();
            var (embed, components) = await BuildViewAsync(db, "filter", g);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuPickGroup(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = await BuildViewAsync(db, "pool", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuPickMsg(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = await BuildViewAsync(db, "detail", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task RuAdd(SocketMessageComponent component, [ComponentData] string data) {
            var modal = new ModalBuilder().WithTitle("New rank-up message").WithCustomId($"RuMsgModal:new:{data}")
                .AddTextInput("Message", customId: "text", TextInputStyle.Paragraph, placeholder: "Use {{user}} {{rank}} {{eb}} {{oom}} {{emoji:name}}", required: true, maxLength: 1500)
                .Build();
            await component.RespondWithModalAsync(modal);
        }

        [ComponentCommand]
        public static async Task RuEditBtn(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var msg = await db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == data);
            if(msg is null) { await component.DeferAsync(); return; }
            var modal = new ModalBuilder().WithTitle("Edit rank-up message").WithCustomId($"RuMsgModal:edit:{data}")
                .AddTextInput("Message", customId: "text", TextInputStyle.Paragraph, value: Trunc(msg.Text, 1500), required: true, maxLength: 1500)
                .Build();
            await component.RespondWithModalAsync(modal);
        }

        [ComponentCommand]
        public static async Task RuDelBtn(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var msg = await db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == data && m.GuildId == g.Id);
            var scope = msg?.GroupBaseOom ?? RankupMessage.GlobalPool;
            if(msg is not null) {
                db.RankupMessages.Remove(msg);
                await db.SaveChangesAsync();
                db.InvalidateRankupMessages(g);
            }
            var (embed, components) = await BuildViewAsync(db, "pool", g, scope.ToString());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [Modal]
        public static async Task RuMsgModal(SocketModal modal, [ComponentData] string data, ApplicationDbContext db) {
            var g = await LoadGuild(db, modal.GuildId);
            var (op, arg) = SplitFirst(data);
            var text = modal.Data.Components.FirstOrDefault(c => c.CustomId == "text")?.Value?.Trim() ?? "";
            string section = "pool";
            int scope = RankupMessage.GlobalPool;
            if(op == "edit") {
                var msg = await db.RankupMessages.FirstOrDefaultAsync(m => m.InternalId == arg && m.GuildId == g.Id);
                if(msg is null) {
                    section = "groups";
                } else {
                    msg.Text = text;
                    scope = msg.GroupBaseOom;
                    await db.SaveChangesAsync();
                    db.InvalidateRankupMessages(g);
                }
            } else {
                scope = int.Parse(arg);
                db.RankupMessages.Add(new RankupMessage {
                    InternalId = Guid.NewGuid().ToString("N"),
                    GuildId = g.Id,
                    GuildName = g.Name,
                    GroupBaseOom = scope,
                    Text = text,
                    Weight = 1,
                    CreatedById = modal.User.Id,
                    CreatedBy = modal.User.Username
                });
                await db.SaveChangesAsync();
                db.InvalidateRankupMessages(g);
            }
            var (embed, components) = await BuildViewAsync(db, section, g, section == "pool" ? scope.ToString() : null);
            await modal.UpdateAsync(x => { x.Embed = embed; x.Components = components; });
        }

        private static (string, string) SplitFirst(string s) {
            var i = s.IndexOf(':');
            return i < 0 ? (s, null) : (s[..i], s[(i + 1)..]);
        }
    }
}
