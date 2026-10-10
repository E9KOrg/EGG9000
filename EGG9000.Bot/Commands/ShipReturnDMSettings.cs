using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord.ComponentsV2;
using Microsoft.EntityFrameworkCore;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class ShipReturnDmModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {

        [ComponentInteraction("SRDMenu:*", ignoreGroupNames: true)]
        public async Task SRDMenu(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = MainMenu(dbuser); });
        }

        public static MessageComponent MainMenu(DBUser user, Color color = default) {
            var page = new MenuPageBuilder("Ship Return DMs")
                .WithDescription("Receive a DM whenever a ship is due to return. The DM will also let you know the current fuel tank values.");
            if(color != default)
                page.WithAccent(color);

            if(!user.DMOnShipReturn) {
                page.AddButtons(new ButtonBuilder("Enable Ship DMs", $"SRDEnable:{user.DiscordId}", ButtonStyle.Primary));
            } else {
                page.AddText("You will receive a DM a set number of minutes before a ship is set to return depending on whether the next ship is fully fueled or not. You have the option for a second DM for ships that need fueling, one sent at the 'Needs Fueling' time and a second sent at the 'Full Ship' time.");
                page.AddDivider();
                page.AddRow("If Ship Is Fully Fueled", $"DM sent {user.ShipReturnMinutes} mins before ship is set to return",
                    new ButtonBuilder("Set Time For Full Ship", $"SRDSetFueledTime:{user.DiscordId}", ButtonStyle.Primary));

                var needsFueling = $"DM sent {(user.ShipReturnStillFuelingMinutes > 0 ? user.ShipReturnStillFuelingMinutes : user.ShipReturnMinutes)} mins before ship is set to return.";
                if(user.ShipReturnDMAfterFuel)
                    needsFueling += $"\nYou will receive a second DM at {user.ShipReturnMinutes} mins before ship is set to return";
                page.AddRow("Or If Ship Needs Fueling", needsFueling,
                    new ButtonBuilder("Set Time For Ship Needs Fueling", $"SRDSetNotFueledTime:{user.DiscordId}", ButtonStyle.Primary));

                page.AddButtons(
                    new ButtonBuilder($"{(user.ShipReturnDMAfterFuel ? "Disable" : "Receive")} Second DM For Ship Needs Fueling", $"SRDSecondDM:{user.DiscordId}", ButtonStyle.Primary),
                    new ButtonBuilder("Disable Ship DMs", $"SRDDisable:{user.DiscordId}", ButtonStyle.Danger));
            }
            page.WithReturn($"MCSAccounts:{user.DiscordId}", "← Contract Settings");
            return page.Build();
        }

        [ComponentInteraction("SRDEnable:*", ignoreGroupNames: true)]
        public async Task SRDEnable(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            dbuser.DMOnShipReturn = true;
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = MainMenu(dbuser); });
        }

        [ComponentInteraction("SRDDisable:*", ignoreGroupNames: true)]
        public async Task SRDDisable(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            dbuser.DMOnShipReturn = false;
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = MainMenu(dbuser); });
        }

        [ComponentInteraction("SRDSecondDM:*", ignoreGroupNames: true)]
        public async Task SRDSecondDM(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            dbuser.ShipReturnDMAfterFuel = !dbuser.ShipReturnDMAfterFuel;
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = MainMenu(dbuser); });
        }

        [ComponentInteraction("SRDSetFueledTime:*", ignoreGroupNames: true)]
        public async Task SRDSetFueledTime(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var modal = GetModal("Set Time For Full Ship", $"SRDFueledTime:{data}", "Number of Minutes", (dbuser.ShipReturnMinutes > 0 ? dbuser.ShipReturnMinutes : 1).ToString(), "mins");
            await component.RespondWithModalAsync(modal);
        }

        [ModalInteraction("SRDFueledTime:*", ignoreGroupNames: true)]
        public async Task SRDFueledTime(string data, MinutesInputModal form) {
            var modal = (SocketModal)Context.Interaction;

            await modal.DeferAsync();
            var minsText = form.Mins;
            var isNum = int.TryParse(minsText, out var mins);

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));

            if(!isNum || mins < 0) {
                var components = ComponentsV2EmbedHelpers.ErrorWithRetry("Ship Return DMs", "⚠️ Input needs to be a positive integer", $"SRDSetFueledTime:{data}", $"SRDMenu:{data}");
                await modal.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            } else {
                dbuser.ShipReturnMinutes = mins;
                await Db.SaveChangesAsync();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = MainMenu(dbuser); });
            }
        }

        [ComponentInteraction("SRDSetNotFueledTime:*", ignoreGroupNames: true)]
        public async Task SRDSetNotFueledTime(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));

            var modal = GetModal("Set Time For Ship Needs Fueling", $"SRDNotFueledTime:{data}", "Number of Minutes", (dbuser.ShipReturnStillFuelingMinutes > 0 ? dbuser.ShipReturnStillFuelingMinutes : 10).ToString(), "mins");
            await component.RespondWithModalAsync(modal);
        }

        [ModalInteraction("SRDNotFueledTime:*", ignoreGroupNames: true)]
        public async Task SRDNotFueledTime(string data, MinutesInputModal form) {
            var modal = (SocketModal)Context.Interaction;

            await modal.DeferAsync();
            var minsText = form.Mins;
            var isNum = int.TryParse(minsText, out var mins);

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));

            if(!isNum || mins < 0) {
                var components = ComponentsV2EmbedHelpers.ErrorWithRetry("Ship Return DMs", "⚠️ Input needs to be a positive integer", $"SRDSetNotFueledTime:{data}", $"SRDMenu:{data}");
                await modal.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            } else if(mins < dbuser.ShipReturnMinutes) {
                var components = ComponentsV2EmbedHelpers.ErrorWithRetry("Ship Return DMs", $"⚠️ Input needs to be greater or equal to Ship Fueled Time of {dbuser.ShipReturnMinutes} mins", $"SRDSetNotFueledTime:{data}", $"SRDMenu:{data}");
                await modal.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            } else {
                dbuser.ShipReturnStillFuelingMinutes = mins;
                await Db.SaveChangesAsync();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = MainMenu(dbuser); });
            }
        }

        private static Modal GetModal(string title, string modalid, string inputDescrption, string inputValue, string inputName) {
            return new ModalBuilder().WithTitleSafe(title).WithCustomId(modalid).AddTextInputSafe(label: inputDescrption, value: inputValue, customId: inputName, required: true).Build();
        }
    }

    public class MinutesInputModal : IModal {
        public string Title => "Enter Number of Minutes";

        [InputLabel("Number of Minutes")]
        [ModalTextInput("mins")]
        [RequiredInput(true)]
        public string Mins { get; set; }
    }
}
