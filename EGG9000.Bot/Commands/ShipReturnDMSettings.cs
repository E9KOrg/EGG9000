using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class ShipReturnDmModule(IDbContextFactory<ApplicationDbContext> dbFactory) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {

        [ComponentInteraction("SRDMenu:*", ignoreGroupNames: true)]
        public async Task SRDMenu(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
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
                builder.WithButton("Enable Ship DMs", $"SRDEnable:{user.DiscordId}");
            } else {
                eBuilder.Description += "\n\nYou will receive a DM a set number of minutes before a ship is set to return depending on whether the next ship is fully fueled or not. You have the option for a second DM for ships that need fueling, one sent at the 'Needs Fueling' time and a second sent at the 'Full Ship' time.";
                eBuilder.AddField("If Ship Is Fully Fueled", $"DM sent {user.ShipReturnMinutes} mins before ship is set to return");

                var needsFueling = $"DM sent {(user.ShipReturnStillFuelingMinutes > 0 ? user.ShipReturnStillFuelingMinutes : user.ShipReturnMinutes)} mins before ship is set to return.";
                if(user.ShipReturnDMAfterFuel) {
                    needsFueling += $"\nYou will receive a second DM at {user.ShipReturnMinutes} mins before ship is set to return";
                }
                eBuilder.AddField("Or If Ship Needs Fueling", needsFueling);

                builder.WithButton("Disable Ship DMs", $"SRDDisable:{user.DiscordId}");
                builder.WithButton("Set Time For Full Ship", $"SRDSetFueledTime:{user.DiscordId}");
                builder.WithButton("Set Time For Ship Needs Fueling", $"SRDSetNotFueledTime:{user.DiscordId}");
                builder.WithButton($"{(user.ShipReturnDMAfterFuel ? "Disable" : "Receive")} Second DM For Ship Needs Fueling", $"SRDSecondDM:{user.DiscordId}");
            }
            builder.WithButton("↵ Contract Settings", $"MCSAccounts:{user.DiscordId}", ButtonStyle.Secondary);

            props.Components = builder.Build();
            props.Embed = eBuilder.Build();

            return props;
        }

        [ComponentInteraction("SRDEnable:*", ignoreGroupNames: true)]
        public async Task SRDEnable(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            dbuser.DMOnShipReturn = true;
            await Db.SaveChangesAsync();
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentInteraction("SRDDisable:*", ignoreGroupNames: true)]
        public async Task SRDDisable(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            dbuser.DMOnShipReturn = false;
            await Db.SaveChangesAsync();
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentInteraction("SRDSecondDM:*", ignoreGroupNames: true)]
        public async Task SRDSecondDM(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            dbuser.ShipReturnDMAfterFuel = !dbuser.ShipReturnDMAfterFuel;
            await Db.SaveChangesAsync();
            var props = MainMenu(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
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
        public async Task SRDFueledTime(string data) {
            var modal = (SocketModal)Context.Interaction;

            await modal.DeferAsync();
            var minsText = modal.Data.Components.First(x => x.CustomId == "mins").Value;
            var isNum = int.TryParse(minsText, out var mins);

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));

            if(!isNum || mins < 0) {
                var embedBuilder = new EmbedBuilder().WithTitle("⚠️Input needs to be a positive integer").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", $"SRDSetFueledTime:{data}").WithButton("Cancel", $"SRDMenu:{data}").Build();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else {
                dbuser.ShipReturnMinutes = mins;
                await Db.SaveChangesAsync();
                var props = MainMenu(dbuser);
                await modal.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
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
        public async Task SRDNotFueledTime(string data) {
            var modal = (SocketModal)Context.Interaction;

            await modal.DeferAsync();
            var minsText = modal.Data.Components.First(x => x.CustomId == "mins").Value;
            var isNum = int.TryParse(minsText, out var mins);

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));

            if(!isNum || mins < 0) {
                var embedBuilder = new EmbedBuilder().WithTitle("⚠️Input needs to be a positive integer").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", $"SRDSetNotFueledTime:{data}").WithButton("Cancel", $"SRDMenu:{data}").Build();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else if(mins < dbuser.ShipReturnMinutes) {
                var embedBuilder = new EmbedBuilder().WithTitle($"⚠️Input needs to be greater or equal to Ship Fueled Time of {dbuser.ShipReturnMinutes} mins").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", $"SRDSetNotFueledTime:{data}").WithButton("Cancel", $"SRDMenu:{data}").Build();
                await modal.ModifyOriginalResponseAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else {
                dbuser.ShipReturnStillFuelingMinutes = mins;
                await Db.SaveChangesAsync();
                var props = MainMenu(dbuser);
                await modal.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
            }
        }

        private static Modal GetModal(string title, string modalid, string inputDescrption, string inputValue, string inputName) {
            return new ModalBuilder().WithTitleSafe(title).WithCustomId(modalid).AddTextInputSafe(label: inputDescrption, value: inputValue, customId: inputName, required: true).Build();
        }
    }
}
