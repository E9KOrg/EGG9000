using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public static class ProfileCommand {
        [SlashCommand(Description = "Configure your profile with the bot (work in progress")]
        public static async Task Profile(SocketSlashCommand command,  ApplicationDbContext db) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"ERROR: Bot error - User not registered");
                return;
            }

            var cBuilder = new ComponentBuilder().WithButton();

            var row1 = new ActionRowBuilder();
            


        }
    }
}
