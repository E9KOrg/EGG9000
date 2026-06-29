using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
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
    [Group("a", "Admin commands")]
    [StaffOnly(StaffTier.Admin)]
    public class ConfigureModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {
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

        private static (Embed embed, MessageComponent components) BuildView(string section, Guild g, string payload = null) {
            var cb = new ComponentBuilder();
            var eb = new EmbedBuilder().WithAuthor("Server Configuration").WithColor(Color.Blue);

            switch(section) {
                case "channels":
                case "roles": {
                    var roles = section == "roles";
                    var types = (roles ? RoleTypes() : ChannelTypes()).ToList();

                    if(payload is not null && Enum.TryParse<GuildChannelType>(payload, out var t) && IsRole(EnumDesc(t)) == roles) {
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

        [SlashCommand("configure", "Configure this server (same as the website)")]
        public async Task Configure() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(g is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not find this server's config record."); });
                return;
            }
            var (embed, components) = BuildView("overview", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgNav")]
        public async Task CfgNav(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = BuildView(values.FirstOrDefault() ?? "overview", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgPickChannel:*")]
        public async Task CfgPickChannel(string slot, string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = BuildView("channels", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgPickRole:*")]
        public async Task CfgPickRole(string slot, string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = BuildView("roles", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgPickCoop")]
        public async Task CfgPickCoop(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = BuildView("coop", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgPickList")]
        public async Task CfgPickList(string[] values) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = BuildView("lists", g, values.FirstOrDefault());
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgSetChannel:*")]
        public async Task CfgSetChannel(string data, IChannel[] channels) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(Enum.TryParse<GuildChannelType>(data, out var t)) {
                SetChannel(g, t, channels.FirstOrDefault()?.Id ?? 0);
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("channels", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgSetRole:*")]
        public async Task CfgSetRole(string data, IRole[] roles) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(Enum.TryParse<GuildChannelType>(data, out var t)) {
                SetChannel(g, t, roles.FirstOrDefault()?.Id ?? 0);
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("roles", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgClear:*")]
        public async Task CfgClear(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(Enum.TryParse<GuildChannelType>(data, out var t)) {
                SetChannel(g, t, 0);
                await Db.SaveChangesAsync();
            }
            var section = RoleTypes().Any(r => r.ToString() == data) ? "roles" : "channels";
            var (embed, components) = BuildView(section, g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgCoopEn:*")]
        public async Task CfgCoopEn(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(Enum.TryParse<GuildCoopSetting>(data, out var s)) {
                SetCoop(g, s, !g.GetCoopSetting(s).Enabled, null);
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("coop", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgCoopLock:*")]
        public async Task CfgCoopLock(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            if(Enum.TryParse<GuildCoopSetting>(data, out var s)) {
                SetCoop(g, s, null, !g.GetCoopSetting(s).Locked);
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("coop", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgToggle:*")]
        public async Task CfgToggle(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var f = GuildConfigReflection.Get(data);
            if(f is not null && f.Kind == GuildConfigKind.Bool) {
                f.Property.SetValue(g, !(bool)f.Property.GetValue(g));
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("toggles", g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgSetCsvCat:*")]
        public async Task CfgSetCsvCat(string data, IChannel[] channels) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var f = GuildConfigReflection.Get(data);
            if(f is not null) {
                f.Property.SetValue(g, string.Join(",", channels.Select(c => c.Id)));
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("lists", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgSetCsvRole:*")]
        public async Task CfgSetCsvRole(string data, IRole[] roles) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var f = GuildConfigReflection.Get(data);
            if(f is not null) {
                f.Property.SetValue(g, string.Join(",", roles.Select(r => r.Id)));
                await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("lists", g, data);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgEdit:*")]
        public async Task CfgEdit(string data) {
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var f = GuildConfigReflection.Get(data);
            if(f is null) { await Context.Interaction.DeferAsync(); return; }
            var cur = f.Property.GetValue(g)?.ToString() ?? "";
            var hint = f.Kind switch {
                GuildConfigKind.Int => $"{f.Description} (whole number, 0 or more)",
                GuildConfigKind.Float => $"{f.Description} (number, 0 or more)",
                _ => f.Description,
            };
            var modal = new ModalBuilder().WithTitleSafe(f.Label).WithCustomId($"CfgEditModal:{f.PropName}")
                .AddTextInputSafe(f.Label, customId: "val", placeholder: hint, value: cur, required: false,
                    maxLength: f.Kind == GuildConfigKind.String ? 100 : 20)
                .Build();
            await Context.Interaction.RespondWithModalAsync(modal);
        }

        [ModalInteraction("CfgEditModal:*", ignoreGroupNames: true)]
        public async Task CfgEditModal(string data, ConfigEditModal form) {
            var modal = (SocketModal)Context.Interaction;
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var f = GuildConfigReflection.Get(data);
            var raw = form.Val?.Trim() ?? "";
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
                if(error is null) await Db.SaveChangesAsync();
            }
            var (embed, components) = BuildView("numbers", g);
            await modal.UpdateAsync(x => { x.Content = error is null ? "" : $"⚠️ {error}"; x.Embed = embed; x.Components = components; });
        }

        [ComponentInteraction("CfgBack:*", ignoreGroupNames: true)]
        public async Task CfgBack(string data) {
            await Context.Interaction.DeferAsync();
            var g = await LoadGuild(Db, Context.Guild?.Id);
            var (embed, components) = BuildView(string.IsNullOrEmpty(data) ? "overview" : data, g);
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = components; });
        }
    }

    public class ConfigEditModal : IModal {
        public string Title => "Edit";

        [InputLabel("Value")]
        [ModalTextInput("val", maxLength: 100)]
        [RequiredInput(false)]
        public string Val { get; set; }
    }
}
