using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord.ComponentsV2;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class CoopSettingsModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {

        #region MainMenu
        [SlashCommand("coopsettings", "Co-op Settings")]
        public async Task CoopSettings() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbuser == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ComponentsV2EmbedHelpers.Error($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"); });
                return;
            }

            var inCoopChannel = await Db.UserCoopXrefs.AnyAsync(x => x.UserId == dbuser.Id && x.Coop.ThreadID == Context.Interaction.ChannelId);

            if(inCoopChannel) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ChooserComponents(dbuser); });
            } else {
                var components = MainMenu(dbuser.CoopSetting ?? new CoopSetting(), "CSAll", "Default Settings", false, false, Db, dbuser);
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            }
        }

        [ComponentInteraction("CSAccountMenu:*", ignoreGroupNames: true)]
        public async Task CSAccountMenu(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[1]);
            var coopOnly = data.Split(",").Length > 2 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var components = MainMenu(dbuser.CoopSetting ?? new CoopSetting(), "CSAll", "Default Settings", coopOnly, openedFromContSets, Db, dbuser);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        public static MessageComponent MainMenu(CoopSetting coopSetting, string prefix, string title, bool coopOnly, bool mcs, ApplicationDbContext db, DBUser dbuser) =>
            MainMenu(coopSetting, prefix, title, coopOnly, mcs, db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId), dbuser);

        public static MessageComponent MainMenu(CoopSetting coopSetting, string prefix, string title, bool coopOnly, bool mcs, Guild guild, DBUser dbuser) {
            var page = new MenuPageBuilder($"Co-op Settings for {title}")
                .WithDescription("Receive a DM from the bot for any of the following")
                .AddDivider();

            foreach(var coopSettingEnum in Enum.GetValues<GuildCoopSetting>()) {
                if(typeof(CoopSetting).GetProperty(coopSettingEnum.ToString()) is null)
                    continue;
                var property = coopSettingEnum.ToString();
                if(coopOnly && (property == "PingOnCoopCreated" || property == "PingOnCoopCreatedEvenIfJoined"))
                    continue;
                var fi = coopSettingEnum.GetType().GetField(property);
                var description = (fi.GetCustomAttributes(typeof(DescriptionAttribute), false) is DescriptionAttribute[] attributes && attributes.Any()) ? attributes.First().Description : property;

                var label = PrettySettingName(property);

                var guildOverride = guild.GetCoopSetting(coopSettingEnum);
                var nextToText = guildOverride.Locked ? (guildOverride.Enabled ? "✅ Yes **(Locked by Server)**" : "❌ No **(Locked by Server)**") : (coopSetting[property] ? "✅ Yes" : "❌ No");

                if(guildOverride.Locked)
                    page.AddRow($"{label}: {nextToText}", description);
                else
                    page.AddRow($"{label}: {nextToText}", description, new ButtonBuilder("Toggle", $"{prefix}:{property},{dbuser.DiscordId},{!coopOnly}", ButtonStyle.Primary));
            }

            if(mcs)
                page.WithReturn($"MCSAccounts:{dbuser.DiscordId}", "← Contract Settings");
            return page.Build();
        }

        // Display label only - the raw property name still goes in the button custom ID.
        // "PingOnCoopCreatedEvenIfJoined" -> "Co-op Created Even If Joined".
        public static string PrettySettingName(string property) {
            var name = property.StartsWith("PingOn") ? property["PingOn".Length..] : property;
            return name.SplitPascalCase().Replace("Coop", "Co-op");
        }

        public static MessageComponent ChooserComponents(DBUser dbuser) =>
            new MenuPageBuilder("Co-op Settings")
                .WithDescription("Would you like to edit settings for just this co-op or this and future co-ops?")
                .AddButtons(
                    new ButtonBuilder("This Co-op Only", $"CSCoop:{dbuser.DiscordId}", ButtonStyle.Primary),
                    new ButtonBuilder("This and Future Co-ops", $"CSAccountMenu:{dbuser.DiscordId},false,false", ButtonStyle.Primary))
                .Build();
        #endregion

        [ComponentInteraction("CSCoop:*", ignoreGroupNames: true)]
        public async Task CSCoop(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            if(!component.HasResponded) await component.DeferAsync();
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var dbGuild = Db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            var xref = await Db.UserCoopXrefs.FirstAsync(x => x.UserId == dbuser.Id && x.Coop.ThreadID == component.ChannelId);
            var components = MainMenu(xref.CoopSetting ?? new CoopSetting(xref, dbuser, dbGuild), "CSCoopOnly", "This Co-op", true, false, Db, dbuser);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("CSAll:*", ignoreGroupNames: true)]
        public async Task CSAll(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var dbGuild = Db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            var settingName = data.Split(",")[0];

            dbuser.CoopSetting ??= new CoopSetting();
            dbuser.CoopSetting[settingName] = !dbuser.CoopSetting[settingName];
            dbuser.CoopSetting = dbuser.CoopSetting;

            var xref = await Db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbuser.Id && x.Coop.ThreadID == component.ChannelId);
            if(xref is not null) {
                var setting = xref.CoopSetting ?? new CoopSetting(xref, dbuser, dbGuild);
                setting[settingName] = dbuser.CoopSetting[settingName];
                xref.CoopSetting = setting;
            }

            await Db.SaveChangesAsync();

            var components = MainMenu(dbuser.CoopSetting, "CSAll", "Default Settings", false, openedFromContSets, Db, dbuser);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("CSCoopOnly:*", ignoreGroupNames: true)]
        public async Task CSCoopOnly(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var dbGuild = Db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            var settingName = data.Split(",")[0];

            var xref = await Db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbuser.Id && x.Coop.ThreadID == component.ChannelId);
            var setting = xref.CoopSetting ?? new CoopSetting(xref, dbuser, dbGuild);
            setting[settingName] = !setting[settingName];
            xref.CoopSetting = setting;

            await Db.SaveChangesAsync();

            var components = MainMenu(setting, "CSCoopOnly", "This Co-op", true, openedFromContSets, Db, dbuser);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [SlashCommand("showeb", "Have the bot add your EB to your nickname in this server (will auto update)")]
        public async Task ShowEB() {
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser == null) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"));
                return;
            }
            if(dbUser.showEB) {
                await Context.Interaction.RespondAsync($"The bot is already set to update your EB automatically. It will update every {LeaderboardUpdater.UpdateTime.TotalMinutes} mins when the leaderboard does.", ephemeral: true);
                return;
            }

            var ebs = dbUser.EggIncAccounts.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).Select(x => x.Backup.EarningsBonus.ToEggString());
            var ebString = $" ({string.Join(",", values: ebs)})";
            var newName = ((IGuildUser)Context.User).GetCleanName().Truncate(32 - ebString.Length) + ebString;

            await ((SocketGuildUser)Context.User).ModifyAsync(x => x.Nickname = newName);

            dbUser.showEB = true;
            await Db.SaveChangesAsync();
            await Context.Interaction.RespondAsync($"{Context.User.Mention} will be updated with their EB. To stop this run the command /hideEB", ephemeral: true);
        }

        [SlashCommand("hideeb", "Remove the EB from your nickname")]
        public async Task HideEB() {
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser == null) {
                await Context.Interaction.RespondAsync(text: "", embed: EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"));
                return;
            }

            dbUser.showEB = false;
            await Db.SaveChangesAsync();

            var newName = ((IGuildUser)Context.User).GetCleanName();
            await ((SocketGuildUser)Context.User).ModifyAsync(x => x.Nickname = newName);
            await Context.Interaction.RespondAsync($"{Context.User.Mention} will no longer be updated with their EB.", ephemeral: true);
        }
    }
}
