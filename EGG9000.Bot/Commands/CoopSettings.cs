using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class CoopSettingsCommand {
        #region MainMenu
        [SlashCommand(Description = "Co-op Settings")]
        public static async Task CoopSettings(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: true);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
            }

            var inCoopChannel = await db.UserCoopXrefs.AnyAsync(x => x.UserId == dbuser.Id && (x.Coop.ThreadID == command.ChannelId || x.Coop.DiscordChannelId == command.ChannelId));


            if(inCoopChannel) {
                var builder = new ComponentBuilder();
                builder.WithButton("This Co-op Only", $"CSCoop:{dbuser.DiscordId}");
                builder.WithButton("This and Future Co-ops", $"CSAccountMenu:{dbuser.DiscordId},false,false");


                await command.ModifyOriginalResponseAsync(x => { x.Content = "Would you like to edit settings for just this co-op or this and future co-ops?"; x.Components = builder.Build(); x.Embed = null; });
            } else {
                var props = MainMenu(dbuser.CoopSetting ?? new CoopSetting(), "CSAll", "Default Settings", false, false, db, dbuser);
                await command.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
            }
        }

        [ComponentCommand]
        public static async Task CSAccountMenu(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[1]);
            var coopOnly = data.Split(",").Length > 2 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var props = MainMenu(dbuser.CoopSetting ?? new CoopSetting(), "CSAll", "Default Settings", coopOnly, openedFromContSets, db, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        public static MessageProperties MainMenu(CoopSetting coopSetting, string prefix ,string title, bool coopOnly, bool mcs, ApplicationDbContext db, DBUser dbuser) {
            var props = new MessageProperties();

            var eBuilder = new EmbedBuilder()
                .WithTitle($"Co-op Settings for {title}");

            eBuilder.Description += "\n\nReceive a DM from the bot for any of the following";
            var builder = new ComponentBuilder();

            var guild = db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            foreach(var coopSettingEnum in Enum.GetValues<GuildCoopSetting>()) {
                if(typeof(CoopSetting).GetProperty(coopSettingEnum.ToString()) is null)
                    continue;
                var fi = coopSettingEnum.GetType().GetField(coopSettingEnum.ToString());
                var description = (fi.GetCustomAttributes(typeof(DescriptionAttribute), false) is DescriptionAttribute[] attributes && attributes.Any()) ? attributes.First().Description : coopSettingEnum.ToString();
                var option = new {
                    Property = coopSettingEnum.ToString(),
                    Description = description
                };

                var guildOverride = guild.GetCoopSetting(coopSettingEnum);
                var nextToText = guildOverride.Locked ? (guildOverride.Enabled ? "✅ Yes **(Locked by Server)**" : "❌ No **(Locked by Server)**") : (coopSetting[option.Property] ? "✅ Yes" : "❌ No");

                if(coopOnly && option.Property == "PingOnCoopCreated")
                    continue;
                eBuilder.AddField($"{option.Property}: {nextToText}", option.Description);
                if(!guildOverride.Locked) {
                    builder.WithButton(option.Property, $"{prefix}:{option.Property},{dbuser.DiscordId},{!coopOnly}");
                }
            }

            if(mcs) {
                builder.WithButton("↵Contract Settings", $"MCSAccounts:{dbuser.DiscordId}", ButtonStyle.Secondary);
            }

            props.Components = builder.Build();
            props.Embed = eBuilder.Build();

            return props;
        }
        #endregion

        [ComponentCommand]
        public static async Task CSCoop(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            if(!component.HasResponded) await component.DeferAsync();
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var dbGuild = db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            var xref = await db.UserCoopXrefs.FirstAsync(x => x.UserId == dbuser.Id && (x.Coop.ThreadID == component.ChannelId || x.Coop.DiscordChannelId == component.ChannelId));
            var props = MainMenu(xref.CoopSetting ?? new CoopSetting(xref, dbuser, dbGuild), "CSCoopOnly", "This Co-op", true, false, db, dbuser);
            await component.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task CSAll(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var dbGuild = db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            var settingName = data.Split(",")[0];

            dbuser.CoopSetting ??= new CoopSetting();
            dbuser.CoopSetting[settingName] = !dbuser.CoopSetting[settingName];
            dbuser.CoopSetting = dbuser.CoopSetting;

            var xref = await db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbuser.Id && (x.Coop.ThreadID == component.ChannelId || x.Coop.DiscordChannelId == component.ChannelId));
            if(xref is not null) {
                var setting = xref.CoopSetting ?? new CoopSetting(xref, dbuser, dbGuild);
                setting[settingName] = dbuser.CoopSetting[settingName];
                xref.CoopSetting = setting;
            }

            await db.SaveChangesAsync();

            var props = MainMenu(dbuser.CoopSetting, "CSAll", "Default Settings", false, openedFromContSets, db, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task CSCoopOnly(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var dbGuild = db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);

            var settingName = data.Split(",")[0];

            dbuser.UpdateAccounts();

            var xref = await db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbuser.Id && (x.Coop.ThreadID == component.ChannelId || x.Coop.DiscordChannelId == component.ChannelId));
            var setting = xref.CoopSetting ?? new CoopSetting(xref, dbuser, dbGuild);
            setting[settingName] = !setting[settingName];
            xref.CoopSetting = setting;

            await db.SaveChangesAsync();

            var props = MainMenu(setting, "CSCoopOnly", "This Co-op", true, openedFromContSets, db, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [SlashCommand(Description = "Have the bot add your EB to your nickname in this server (will auto update)")]
        public static async Task ShowEB(FauxCommand command, ApplicationDbContext db) {
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"));
                return;
            }
            if(dbUser.showEB) {
                await command.RespondAsync($"The bot is already set to update your EB automatically. It will update every {LeaderboardUpdater.UpdateTime.TotalMinutes} mins when the leaderboard does.", ephemeral: true);
                return;
            }

            var ebs = dbUser.EggIncAccounts.Where(x => x.Backup is not null).OrderByDescending(x => x.Backup.EarningsBonus).Select(x => x.Backup.EarningsBonus.ToEggString());
            var ebString = $" ({string.Join(",", values: ebs)})";
            var newName = ((IGuildUser)command.User).GetCleanName().Truncate(32 - ebString.Length) + ebString;

            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);

            dbUser.showEB = true;
            await db.SaveChangesAsync();
            await command.RespondAsync($"{command.User.Mention} will be updated with their EB. To stop this run the command /hideEB", ephemeral: true);
        }

        [SlashCommand(Description = "Remove the EB from your nickname")]
        public static async Task HideEB(FauxCommand command, ApplicationDbContext db) {
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"));
                return;
            }

            dbUser.showEB = false;
            await db.SaveChangesAsync();

            var newName = ((IGuildUser)command.User).GetCleanName();
            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);
            await command.RespondAsync($"{command.User.Mention} will no longer be updated with their EB.", ephemeral: true);
        }
    }
}
