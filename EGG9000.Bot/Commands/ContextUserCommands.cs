using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands
{
    public static class ContextUserCommands
    {
        [UserCommand(Name = "View User on EGG9000.com", AdminOnly = true)]
        public static async Task WebsiteLink(SocketUserCommand command) {
            await command.RespondAsync($"<https://egg9000.com/MyFarms/ViewUser?discordId={command.Data.Member.Id}>", ephemeral: true);
        }
    }
}


