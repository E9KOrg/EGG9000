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

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Bot.Commands {
    public class ShipReturnDMSettings {
        [ComponentCommand]
        public static async Task SRDMenu(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        public static MessageProperties MainMenu(DBUser user, Color color = default) {
            var props = new MessageProperties();

            var eBuilder = new EmbedBuilder()
                .WithTitle($"Ship Return DMs");

            eBuilder.Description += "\n\nReceive a DM whenever a ship is due to return. The DM will also let you know the current fuel tank values.";
            if(color != default)
                eBuilder.Color = color;

            var builder = new ComponentBuilder();

            if(!user.DMOnShipReturn) {
                builder.WithButton("Enable Ship DMs", "SRDEnable");
            } else {
                eBuilder.Description += "\n\nYou will receive a DM a set number of minutes before a ship is set to return depending on whether the next ship is fully fueled or not. You have the option for a second DM for ships that need fueling, one sent at the 'Needs Fueling' time and a second sent at the 'Full Ship' time.";
                eBuilder.AddField("If Ship Is Fully Fueled", $"DM sent {user.ShipReturnMinutes} mins before ship is set to return");

                string needsFueling = $"DM sent {(user.ShipReturnStillFuelingMinutes > 0 ? user.ShipReturnStillFuelingMinutes : user.ShipReturnMinutes)} mins before ship is set to return.";
                if(user.ShipReturnDMAfterFuel) {
                    needsFueling += $"\nYou will receive a second DM at {user.ShipReturnMinutes} mins before ship is set to return";
                }
                eBuilder.AddField("Or If Ship Needs Fueling", needsFueling);

                builder.WithButton("Disable Ship DMs", "SRDDisable");
                builder.WithButton("Set Time For Full Ship", "SRDSetFueledTime");
                builder.WithButton("Set Time For Ship Needs Fueling", "SRDSetNotFueledTime");
                builder.WithButton($"{(user.ShipReturnDMAfterFuel ? "Disable" : "Receive")} Second DM For Ship Needs Fueling", "SRDSecondDM");
            }
            builder.WithButton("↵ Contract Settings", "MCSAccounts", ButtonStyle.Secondary);

            props.Components = builder.Build();
            props.Embed = eBuilder.Build();

            return props;
        }

        [ComponentCommand]
        public static async Task SRDEnable(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            dbuser.DMOnShipReturn = true;
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task SRDDisable(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            dbuser.DMOnShipReturn = false;
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task SRDSecondDM(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            dbuser.ShipReturnDMAfterFuel = !dbuser.ShipReturnDMAfterFuel;
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task SRDSetFueledTime(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);

            var modal = GetModal("Set Time For Full Ship", "SRDFueledTime", "Number of Minutes", (dbuser.ShipReturnMinutes > 0 ? dbuser.ShipReturnMinutes : 1).ToString(), "mins");
            await component.RespondWithModalAsync(modal);
        }

        [Modal]
        public static async Task SRDFueledTime(SocketModal modal, ApplicationDbContext db) {
            await modal.DeferAsync();
            var minsText = modal.Data.Components.First(x => x.CustomId == "mins").Value;
            var isNum = int.TryParse(minsText, out int mins);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == modal.User.Id);
            if(!isNum || mins < 0) {
                var embedBuilder = new EmbedBuilder().WithTitle("⚠️Input needs to be a positive integer").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", "SRDSetFueledTime").WithButton("Cancel", "SRDMenu").Build();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else {
                dbuser.ShipReturnMinutes = mins;
                await db.SaveChangesAsync();
                var props = MainMenu(dbuser);
                await modal.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
            }
        }

        [ComponentCommand]
        public static async Task SRDSetNotFueledTime(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);

            var modal = GetModal("Set Time For Ship Needs Fueling", "SRDNotFueledTime", "Number of Minutes", (dbuser.ShipReturnStillFuelingMinutes > 0 ? dbuser.ShipReturnStillFuelingMinutes : 10).ToString(), "mins");
            await component.RespondWithModalAsync(modal);
        }

        [Modal]
        public static async Task SRDNotFueledTime(SocketModal modal, ApplicationDbContext db) {
            await modal.DeferAsync();
            var minsText = modal.Data.Components.First(x => x.CustomId == "mins").Value;
            var isNum = int.TryParse(minsText, out int mins);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == modal.User.Id);
            if(!isNum || mins < 0) {
                var embedBuilder = new EmbedBuilder().WithTitle("⚠️Input needs to be a positive integer").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", "SRDSetNotFueledTime").WithButton("Cancel", "SRDMenu").Build();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else if(mins < dbuser.ShipReturnMinutes) {
                var embedBuilder = new EmbedBuilder().WithTitle($"⚠️Input needs to be greater or equal to Ship Fueled Time of {dbuser.ShipReturnMinutes} mins").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", "SRDSetNotFueledTime").WithButton("Cancel", "SRDMenu").Build();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else {
                dbuser.ShipReturnStillFuelingMinutes = mins;
                await db.SaveChangesAsync();
                var props = MainMenu(dbuser);
                await modal.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
            }
        }

        private static Modal GetModal(string title, string modalid, string inputDescrption, string inputValue, string inputName) {
            return new ModalBuilder().WithTitle(title).WithCustomId(modalid).AddTextInput(label: inputDescrption, value: inputValue, customId: inputName, required: true).Build();
        }
    }
}

