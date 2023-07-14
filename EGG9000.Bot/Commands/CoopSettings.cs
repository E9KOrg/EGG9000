using Bugsnag.Payload;

using Discord;
using Discord.Webhook;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Migrations;
using EGG9000.Common.Services;

using Google.Protobuf;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Bot.Commands {
    public class CoopSettingsCommand {
        #region MainMenu
        [SlashCommand(Description = "Co-op Settings")]
        public static async Task CoopSettings(FauxCommand command, ApplicationDbContext db) {
            await command.RespondAsync("Working, please wait...", ephemeral: true);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = "ERROR: Unable to find user, are you registered?");
            }

            var inCoopChannel = await db.UserCoopXrefs.AnyAsync(x => x.UserId == dbuser.Id && x.Coop.DiscordChannelId == command.ChannelId);


            if(inCoopChannel) {
                var builder = new ComponentBuilder();
                builder.WithButton("This Co-op Only", $"CSCoop:{dbuser.DiscordId}");
                builder.WithButton("This and Future Co-ops", $"CSAccountMenu:{dbuser.DiscordId}");


                await command.ModifyOriginalResponseAsync(x => { x.Content = "Would you like to edit settings for just this co-op or this and future co-ops?"; x.Components = builder.Build(); });
            } else {
                var props = MainMenu(dbuser.CoopSetting ?? new CoopSetting(), "CSAll", "Default Settings", true, dbuser);
                await command.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
            }
        }

        [ComponentCommand]
        public static async Task CSAccountMenu(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[1]);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var props = MainMenu(dbuser.CoopSetting ?? new CoopSetting(), "CSAll", "Default Settings", !openedFromContSets, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        public static List<(string Property, string Description)> options = new List<(string Property, string Description)> {
            ("PingOnFull", "All assigned members have joined the co-op"),
            ("PingOnHighestEB", "Highest assigned EB has joined"),
            ("PingOnFinished", "Co-op has finished"),
            ("PingOnEveryoneCheckedIn", "Co-op is cleared for exit"),
            ("PingOnMessage", "Any non-bot message is sent in channel"),
            ("PingOnCoopCreated", "Additional DM alongside the standard @mention in the co-op channel"),
            ("PingOnTachyonChange", "Get notified when someone adds/removes a Tachyon Deflector"),
            ("PingOnCompleteOnCheckIn", "Get notified when your co-op will complete as soon as everyone checks in"),
        };
        public static MessageProperties MainMenu(CoopSetting coopSetting, string prefix ,string title, bool coopOnly, DBUser dbuser) {
            var props = new MessageProperties();

            var eBuilder = new EmbedBuilder()
                .WithTitle($"Co-op Settings for {title}");

            eBuilder.Description += "\n\nReceive a DM from the bot for any of the following";

            var builder = new ComponentBuilder();

            Console.WriteLine("Prefix: " + prefix);

            foreach(var option in options) {
                if(coopOnly && option.Property == "PingOnCoopCreated")
                    continue;
                eBuilder.AddField($"{option.Property}: {(coopSetting[option.Property] ? "✅Yes" : "No")}", option.Description);
                builder.WithButton(option.Property, $"{prefix}:{option.Property},{dbuser.DiscordId},{!coopOnly}");
            }

            if(!coopOnly) {
                builder.WithButton("↵Contract Settings", $"MCSAccounts:{dbuser.DiscordId}", ButtonStyle.Secondary);
            }

            props.Components = builder.Build();
            props.Embed = eBuilder.Build();

            return props;
        }
        #endregion

        [ComponentCommand]
        public static async Task CSCoop(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var xref = await db.UserCoopXrefs.FirstAsync(x => x.UserId == dbuser.Id && x.Coop.DiscordChannelId == component.ChannelId);
            var props = MainMenu(xref.CoopSetting ?? new CoopSetting(xref, dbuser), "CSCoopOnly", "This Co-op", false, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task CSAll(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var settingName = data.Split(",")[0];

            if(dbuser.CoopSetting == null)
                dbuser.CoopSetting = new CoopSetting();
            dbuser.CoopSetting[settingName] = !dbuser.CoopSetting[settingName];

            dbuser.CoopSetting = dbuser.CoopSetting;

            var xref = await db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbuser.Id && x.Coop.DiscordChannelId == component.ChannelId);
            if(xref is not null) {
                var setting = xref.CoopSetting ?? new CoopSetting(xref, dbuser);
                setting[settingName] = dbuser.CoopSetting[settingName];
                xref.CoopSetting = setting;
            }

            await db.SaveChangesAsync();

            var props = MainMenu(dbuser.CoopSetting, "CSAll", "Default Settings", !openedFromContSets, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task CSCoopOnly(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var openedFromContSets = data.Split(",").Length > 1 && Convert.ToBoolean(data.Split(",")[2]);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var settingName = data.Split(",")[0];

            dbuser.UpdateAccounts();

            var xref = await db.UserCoopXrefs.FirstOrDefaultAsync(x => x.UserId == dbuser.Id && x.Coop.DiscordChannelId == component.ChannelId);
            var setting = xref.CoopSetting ?? new CoopSetting(xref, dbuser);
            setting[settingName] = !setting[settingName];
            xref.CoopSetting = setting;

            await db.SaveChangesAsync();

            var props = MainMenu(setting, "CSCoopOnly", "This Co-op", !openedFromContSets, dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
    }
}
