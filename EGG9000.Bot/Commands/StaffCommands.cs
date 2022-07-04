using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;

using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Commands {
    public static class StaffCommands {

        [SlashCommand(Description = "Log a Message", AdminOnly =true, AllowFarmHand = true)]
        public static async Task AS(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string message, [SlashParam(Required = false)] SocketChannel channel = null) {
            if(channel == null) {
                await command.Channel.SendMessageAsync(message);
            } else {
                await ((SocketTextChannel)channel).SendMessageAsync(message);
            }
            await command.RespondAsync("Sent", ephemeral: true);
        }
    }
}

