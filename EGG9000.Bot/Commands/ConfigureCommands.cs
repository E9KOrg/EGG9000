using Discord;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    /// <summary>
    /// `/a configure` - the site's Configure Server page, inside Discord. Built
    /// dynamically: channels/roles from <see cref="GuildChannelType"/> + its Description
    /// prefixes (/TC/, /R/), coop toggles from <see cref="GuildCoopSetting"/>, and scalar
    /// settings from <c>[GuildConfig]</c>-annotated <see cref="Guild"/> properties. Adding
    /// a setting = annotating a property or adding an enum value; nothing here changes.
    /// Native channel/role select components; modals only for numbers/text.
    /// </summary>
    public static class ConfigureCommands {
        private static string EnumDesc(GuildChannelType v) =>
            typeof(GuildChannelType).GetField(v.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString();
        private static string EnumDesc(GuildCoopSetting v) =>
            typeof(GuildCoopSetting).GetField(v.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString();

        private static bool IsRole(string desc) => desc.StartsWith("/R/");
        private static bool IsCategory(string desc) => desc.Contains("categor", StringComparison.OrdinalIgnoreCase);

        private static string Pretty(string desc) => desc
            .Replace("/TC/", "").Replace("/R/", "").Replace("Required: ", "").Replace("Optional: ", "").Trim();

        private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

        private static IEnumerable<GuildChannelType> ChannelTypes() =>
            Enum.GetValues<GuildChannelType>().Where(v => !IsRole(EnumDesc(v)));
        private static IEnumerable<GuildChannelType> RoleTypes() =>
            Enum.GetValues<GuildChannelType>().Where(v => IsRole(EnumDesc(v)));

        private static List<ChannelType> PickerTypes(string desc) =>
            IsCategory(desc) ? [ChannelType.Category]
            : desc.StartsWith("/TC/") ? [ChannelType.Text, ChannelType.News, ChannelType.PublicThread, ChannelType.PrivateThread]
            : [ChannelType.Text, ChannelType.News];

        private static async Task<Guild> LoadGuild(ApplicationDbContext db, ulong? gid) =>
            gid is null ? null : await db.Guilds.FirstOrDefaultAsync(g => g.Id == gid || g.OverflowServersJson.Contains(gid.Value.ToString()));

        private static void SetChannel(Guild g, GuildChannelType t, ulong id) {
            var list = g.ChannelDetails;
            list.RemoveAll(x => x.ChannelType == t);
            if(id != 0) list.Add(new ChannelDetail { ChannelType = t, Id = id });
            g.ChannelDetails = list;
        }

        private static void SetCoop(Guild g, GuildCoopSetting s, bool? enabled, bool? locked) {
            var list = g.CoopSettings;
            var cur = list.FirstOrDefault(x => x.CoopSetting == s) ?? new ServerCoopSetting { CoopSetting = s };
            list.RemoveAll(x => x.CoopSetting == s);
            if(enabled.HasValue) cur.Enabled = enabled.Value;
            if(locked.HasValue) cur.Locked = locked.Value;
            list.Add(cur);
            g.CoopSettings = list;
        }

        private static readonly (string Key, string Label)[] Sections = [
            ("overview", "Overview"), ("channels", "Channels"), ("roles", "Roles"),
            ("coop", "Co-op Settings"), ("toggles", "Toggles"), ("numbers", "Numbers & Text"), ("lists", "Lists"),
        ];

        private static SelectMenuBuilder NavMenu(string section) {
            var m = new SelectMenuBuilder().WithCustomId("CfgNav").WithPlaceholder("Configure section...");
            foreach(var (key, label) in Sections)
                m.AddOption(label, key, isDefault: key == section);
            return m;
        }

        private static ButtonBuilder BackTo(string target, string label = "← Back") =>
            new ButtonBuilder().WithLabel(label).WithCustomId($"CfgBack:{target}").WithStyle(ButtonStyle.Secondary);

        // Two-step model: a section first shows only its picker(s) + Back; once an item is
        // chosen (payload set) it shows ONLY that item's setter + Back + Clear, so there are
        // never more than ~2 controls stacked at once. The embed is intentionally minimal -
        // on a setter screen it carries just the item's description (the one thing the
        // selectors don't convey); the live values live in the controls themselves.
        private static (Embed embed, MessageComponent components) BuildView(string section, Guild g, string payload = null) {
            var cb = new ComponentBuilder();
            var eb = new EmbedBuilder().WithAuthor("Server Configuration").WithColor(Color.Blue);

            switch(section) {
                case "channels":
                case "roles": {
                    var roles = section == "roles";
                    var types = (roles ? RoleTypes() : ChannelTypes()).ToList();

                    if(payload is not null && Enum.TryParse<GuildChannelType>(payload, out var t) && IsRole(EnumDesc(t)) == roles) {
                        // detail: one setter + back + clear
                        var desc = EnumDesc(t);
                        var cur = g.GetChannelId(t);
                        var set = new SelectMenuBuilder().WithCustomId($"{(roles ? "CfgSetRole" : "CfgSetChannel")}:{t}")
                            .WithType(roles ? ComponentType.RoleSelect : ComponentType.ChannelSelect)
                            .WithPlaceholder($"Choose a {(roles ? "role" : "channel")}...").WithMinValues(1).WithMaxValues(1);
                        if(!roles) set.WithChannelTypes(PickerTypes(desc));
                        if(cur is > 0) set.WithDefaultValues(new SelectMenuDefaultValue(cur.Value, roles ? SelectDefaultValueType.Role : SelectDefaultValueType.Channel));
                        cb.WithSelectMenu(set, row: 0);
                        cb.WithButton(BackTo(section, $"← {(roles ? "Roles" : "Channels")}"), row: 1);
                        cb.WithButton("Clear", customId: $"CfgClear:{t}", style: ButtonStyle.Danger, row: 1);
                        eb.WithTitle(Pretty(desc)).WithDescription($"{Pretty(desc)}\n\n{(cur is > 0 ? (roles ? $"Current: <@&{cur}>" : $"Current: <#{cur}>") : "_Not set_")}");
                    } else {
                        // list: pickers + back
                        var row = 0;
                        if(roles) {
                            foreach(var (chunk, idx) in types.Chunk(25).Select((c, i) => (c, i))) {
                                var pick = new SelectMenuBuilder().WithCustomId($"CfgPickRole:{idx}").WithPlaceholder("Pick a role to set...");
                                foreach(var rt in chunk) pick.AddOption(Trunc(Pretty(EnumDesc(rt)), 100), rt.ToString(), description: g.GetChannelId(rt) is > 0 ? "Currently set" : "Not set");
                                cb.WithSelectMenu(pick, row: row++);
                            }
                        } else {
                            foreach(var grp in new[] { "Required", "Optional" }) {
                                var groupTypes = types.Where(x => EnumDesc(x).Contains("Required:") == (grp == "Required")).ToList();
                                foreach(var (chunk, idx) in groupTypes.Chunk(25).Select((c, i) => (c, i))) {
                                    var pick = new SelectMenuBuilder().WithCustomId($"CfgPickChannel:{grp}{idx}").WithPlaceholder($"{grp} channels & categories...");
                                    foreach(var ct in chunk) pick.AddOption(Trunc(Pretty(EnumDesc(ct)), 100), ct.ToString(), description: g.GetChannelId(ct) is > 0 ? "Currently set" : "Not set");
                                    cb.WithSelectMenu(pick, row: row++);
                                }
                            }
                        }
                        cb.WithButton(BackTo("overview"), row: row);
                        eb.WithTitle(roles ? "Roles" : "Channels & Categories")
                          .WithDescription($"`{types.Count(x => g.GetChannelId(x) is > 0)}`/`{types.Count}` set - pick one to change it.");
                    }
                    break;
                }
                case "coop": {
                    if(payload is not null && Enum.TryParse<GuildCoopSetting>(payload, out var cs)) {
                        var setting = g.GetCoopSetting(cs);
                        cb.WithButton($"Enabled: {(setting.Enabled ? "ON" : "off")}", customId: $"CfgCoopEn:{cs}", style: setting.Enabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0);
                        cb.WithButton($"Locked: {(setting.Locked ? "ON" : "off")}", customId: $"CfgCoopLock:{cs}", style: setting.Locked ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0);
                        cb.WithButton(BackTo("coop", "← Co-op Settings"), row: 1);
                        eb.WithTitle("Co-op Override").WithDescription($"{EnumDesc(cs)}\n\n**Enabled** = on by default for new players.\n**Locked** = members can't override it.");
                    } else {
                        var pick = new SelectMenuBuilder().WithCustomId("CfgPickCoop").WithPlaceholder("Pick a co-op setting...");
                        foreach(var s in Enum.GetValues<GuildCoopSetting>())
                            pick.AddOption(Trunc(EnumDesc(s), 100), s.ToString(), description: (g.GetCoopSetting(s).Enabled || g.GetCoopSetting(s).Locked) ? "Overridden" : "Default");
                        cb.WithSelectMenu(pick, row: 0);
                        cb.WithButton(BackTo("overview"), row: 1);
                        eb.WithTitle("Co-op Setting Overrides").WithDescription("Pick a setting to toggle its Enabled / Locked state.");
                    }
                    break;
                }
                case "toggles": {
                    var bools = GuildConfigReflection.ByKind(GuildConfigKind.Bool).ToList();
                    var row = 0;
                    var inRow = 0;
                    foreach(var f in bools) {
                        var on = (bool)f.Property.GetValue(g);
                        cb.WithButton($"{f.Label}: {(on ? "ON" : "off")}", customId: $"CfgToggle:{f.PropName}", style: on ? ButtonStyle.Success : ButtonStyle.Secondary, row: row);
                        if(++inRow == 3) { inRow = 0; row++; }
                    }
                    cb.WithButton(BackTo("overview"), row: row + (inRow == 0 ? 0 : 1));
                    eb.WithTitle("Toggles").WithDescription(string.Join("\n", bools.Select(f => $"{((bool)f.Property.GetValue(g) ? "\U0001F7E2" : "⚫")} **{f.Label}** - {f.Description}")));
                    break;
                }
                case "numbers": {
                    var fields = GuildConfigReflection.ByKind(GuildConfigKind.Int, GuildConfigKind.Float, GuildConfigKind.String).ToList();
                    foreach(var f in fields)
                        cb.WithButton(f.Label, customId: $"CfgEdit:{f.PropName}", style: ButtonStyle.Primary, row: 0);
                    cb.WithButton(BackTo("overview"), row: 1);
                    eb.WithTitle("Numbers & Text").WithDescription(string.Join("\n", fields.Select(f => $"**{f.Label}**: `{f.Property.GetValue(g) ?? "(unset)"}`\n-# {f.Description}")));
                    break;
                }
                case "lists": {
                    var lists = GuildConfigReflection.ByKind(GuildConfigKind.CsvCategories, GuildConfigKind.CsvRoles).ToList();
                    var sel = lists.FirstOrDefault(f => f.PropName == payload);
                    if(sel is not null) {
                        var raw = (string)sel.Property.GetValue(g) ?? "";
                        var ids = raw.Split(",", StringSplitOptions.RemoveEmptyEntries);
                        var isCat = sel.Kind == GuildConfigKind.CsvCategories;
                        var defaults = ids.Where(i => ulong.TryParse(i, out _))
                            .Select(i => new SelectMenuDefaultValue(ulong.Parse(i), isCat ? SelectDefaultValueType.Channel : SelectDefaultValueType.Role))
                            .ToArray();
                        var setList = new SelectMenuBuilder()
                            .WithCustomId(isCat ? $"CfgSetCsvCat:{sel.PropName}" : $"CfgSetCsvRole:{sel.PropName}")
                            .WithType(isCat ? ComponentType.ChannelSelect : ComponentType.RoleSelect)
                            .WithPlaceholder($"Set {sel.Label}...").WithMinValues(0).WithMaxValues(20);
                        if(isCat) setList.WithChannelTypes(ChannelType.Category);
                        if(defaults.Length > 0) setList.WithDefaultValues(defaults);
                        cb.WithSelectMenu(setList, row: 0);
                        cb.WithButton(BackTo("lists", "← Lists"), row: 1);
                        eb.WithTitle(sel.Label).WithDescription($"{sel.Description}\n\n-# Multi-select replaces the whole list; pick none to clear.");
                    } else {
                        var pick = new SelectMenuBuilder().WithCustomId("CfgPickList").WithPlaceholder("Pick a list to set...");
                        foreach(var f in lists)
                            pick.AddOption(f.Label, f.PropName, description: Trunc(f.Description, 100));
                        cb.WithSelectMenu(pick, row: 0);
                        cb.WithButton(BackTo("overview"), row: 1);
                        eb.WithTitle("Lists (categories & roles)").WithDescription("Pick a list to edit.");
                    }
                    break;
                }
                default: {
                    cb.WithSelectMenu(NavMenu("overview"));
                    eb.WithTitle("Overview").WithDescription("Configure this server, same as the website. Pick a section below.");
                    eb.AddField("Channels", $"`{ChannelTypes().Count(t => g.GetChannelId(t) is > 0)}`/`{ChannelTypes().Count()}` set", inline: true);
                    eb.AddField("Roles", $"`{RoleTypes().Count(t => g.GetChannelId(t) is > 0)}`/`{RoleTypes().Count()}` set", inline: true);
                    eb.AddField("Co-op overrides", $"`{g.CoopSettings.Count(s => s.Enabled || s.Locked)}` active", inline: true);
                    break;
                }
            }
            return (eb.Build(), cb.Build());
        }

        [SlashCommand(Description = "Configure this server (same as the website)", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "a")]
        public static async Task Configure(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: true);
            var g = await LoadGuild(db, command.GuildId);
            if(g is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find this server's config record."); });
                return;
            }
            var (embed, components) = BuildView("overview", g);
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgNav(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = BuildView(component.Data.Values.FirstOrDefault() ?? "overview", g);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgPickChannel(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = BuildView("channels", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgPickRole(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = BuildView("roles", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgPickCoop(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = BuildView("coop", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgPickList(SocketMessageComponent component, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = BuildView("lists", g, component.Data.Values.FirstOrDefault());
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgSetChannel(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            if(Enum.TryParse<GuildChannelType>(data, out var t)) {
                SetChannel(g, t, component.Data.Channels.FirstOrDefault()?.Id ?? 0);
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("channels", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgSetRole(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            if(Enum.TryParse<GuildChannelType>(data, out var t)) {
                SetChannel(g, t, component.Data.Roles.FirstOrDefault()?.Id ?? 0);
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("roles", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgClear(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            if(Enum.TryParse<GuildChannelType>(data, out var t)) {
                SetChannel(g, t, 0);
                await db.SaveChangesAsync();
            }
            var section = RoleTypes().Any(r => r.ToString() == data) ? "roles" : "channels";
            var (embed, components) = BuildView(section, g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgCoopEn(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            if(Enum.TryParse<GuildCoopSetting>(data, out var s)) {
                SetCoop(g, s, !g.GetCoopSetting(s).Enabled, null);
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("coop", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgCoopLock(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            if(Enum.TryParse<GuildCoopSetting>(data, out var s)) {
                SetCoop(g, s, null, !g.GetCoopSetting(s).Locked);
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("coop", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgToggle(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var f = GuildConfigReflection.Get(data);
            if(f is not null && f.Kind == GuildConfigKind.Bool) {
                f.Property.SetValue(g, !(bool)f.Property.GetValue(g));
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("toggles", g);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgSetCsvCat(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var f = GuildConfigReflection.Get(data);
            if(f is not null) {
                f.Property.SetValue(g, string.Join(",", component.Data.Channels.Select(c => c.Id)));
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("lists", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgSetCsvRole(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var f = GuildConfigReflection.Get(data);
            if(f is not null) {
                f.Property.SetValue(g, string.Join(",", component.Data.Roles.Select(r => r.Id)));
                await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("lists", g, data);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgEdit(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var g = await LoadGuild(db, component.GuildId);
            var f = GuildConfigReflection.Get(data);
            if(f is null) { await component.DeferAsync(); return; }
            var cur = f.Property.GetValue(g)?.ToString() ?? "";
            var hint = f.Kind switch {
                GuildConfigKind.Int => $"{f.Description} (whole number, 0 or more)",
                GuildConfigKind.Float => $"{f.Description} (number, 0 or more)",
                _ => f.Description,
            };
            var modal = new ModalBuilder().WithTitle(Trunc(f.Label, 45)).WithCustomId($"CfgEditModal:{f.PropName}")
                .AddTextInput(Trunc(f.Label, 45), customId: "val", placeholder: Trunc(hint, 100), value: cur, required: false,
                    maxLength: f.Kind == GuildConfigKind.String ? 100 : 20)
                .Build();
            await component.RespondWithModalAsync(modal);
        }

        [Modal]
        public static async Task CfgEditModal(SocketModal modal, [ComponentData] string data, ApplicationDbContext db) {
            var g = await LoadGuild(db, modal.GuildId);
            var f = GuildConfigReflection.Get(data);
            var raw = modal.Data.Components.FirstOrDefault(c => c.CustomId == "val")?.Value?.Trim() ?? "";
            string error = null;
            if(f is not null) {
                switch(f.Kind) {
                    case GuildConfigKind.Int:
                        if(!int.TryParse(raw, out var i)) error = $"**{f.Label}**: `{raw}` is not a whole number.";
                        else if(i < 0) error = $"**{f.Label}**: must be 0 or more.";
                        else f.Property.SetValue(g, i);
                        break;
                    case GuildConfigKind.Float:
                        if(!float.TryParse(raw, out var fl)) error = $"**{f.Label}**: `{raw}` is not a number.";
                        else if(fl < 0) error = $"**{f.Label}**: must be 0 or more.";
                        else f.Property.SetValue(g, fl);
                        break;
                    case GuildConfigKind.String:
                        f.Property.SetValue(g, string.IsNullOrWhiteSpace(raw) ? null : raw);
                        break;
                }
                if(error is null) await db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("numbers", g);
            await modal.UpdateAsync(x => { x.Content = error is null ? "" : $"⚠️ {error}"; x.Embed = embed; x.Components = components; });
        }

        [ComponentCommand]
        public static async Task CfgBack(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            await component.DeferAsync();
            var g = await LoadGuild(db, component.GuildId);
            var (embed, components) = BuildView(string.IsNullOrEmpty(data) ? "overview" : data, g);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }
    }
}
