using Discord;
using Discord.Webhook;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Google.Protobuf;

using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Bot.Commands {
    public class PingCommands {
        [SlashCommand(Description = "Test Button Interaction")]
        public static async Task TestButtons(FauxCommand command) {

            var selectMenu = new SelectMenuBuilder().WithCustomId("TestSelect").AddOption("Group 1", "BG1").AddOption("Group 2", "BG2").AddOption("Group 3", "BG3").WithMinValues(0).WithMaxValues(3);
            var builder = new ComponentBuilder().WithButton("Test Button1", "TestID:1").WithButton("Test Button2", "TestID:2").WithSelectMenu(selectMenu);
            await command.RespondAsync("Test:", ephemeral: false, components: builder.Build());
        }

        [ComponentCommand]
        public static async Task TestID(SocketMessageComponent component, [ComponentData] string data) {
            await component.UpdateAsync(x => x.Content = $"You clicked button {data[0]}");
            //await component.RespondAsync($"You clicked button {data[0]}");

        }
    }
}
