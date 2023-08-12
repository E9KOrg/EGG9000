using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Commands;

namespace EGG9000.Bot.Commands {
    public static class ContextUserCommands {
        [UserCommand(Name = "Userstatus", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task Userstatus(SocketUserCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            await RegisterCommandsSlash._userstatus(command, db, _client, apiLink, command.Data.Member, true, false);
        }

        [UserCommand(Name = "EGG9000.com", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task WebsiteLink(SocketUserCommand command) {
            await command.RespondAsync($"<https://egg9000.com/MyFarms/ViewUser?discordId={command.Data.Member.Id}>", ephemeral: true);
        }

        [UserCommand(Name = "Rockets Tracker", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RocketsTrackerLinks(SocketUserCommand command, ApplicationDbContext db)
        {
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.Data.Member.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            } else {
                var sb = new StringBuilder();
                foreach(var id in user.EggIncAccounts) {
                    var backup = user.EggIncAccounts.FirstOrDefault(x => x.Id == id.Id).Backup;
                    if(sb.ToString() != "") sb.Append("\n\n");
                    sb.Append(backup.UserName + ": " + $"<https://wasmegg-carpet.netlify.app/rockets-tracker/?playerId={id.Id}>");
                }
                await command.RespondAsync(sb.ToString(), ephemeral: true);
            }
        }
    }
}


