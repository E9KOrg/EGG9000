using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class NewCode {
        [SlashCommand(Description = "Generate a new co-op code, a channel will be created for the co-op", AdminOnly =true, AllowFarmHand = true)]
        public static async Task NewCoopCode(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            var words = new Words();
            var code = words.GetRandomWord() + words.GetRandomWord() + words.GetRandomNumber();

            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var coopCategory = guild.GetCoopCategory();
            //var channel = await guild.CreateTextChannelAsync(code, x => x.CategoryId = coopCategory.Id);

            var coop = new Coop { Name = code, Created = DateTimeOffset.Now, GuildId = guild.Id };//, DiscordChannelId = channel.Id };
            db.Coops.Add(coop);
            await db.SaveChangesAsync();

            await command.RespondAsync(code);
        }

        [SlashCommand(Description = "Delete co-op channel from discord and database ", AdminOnly = true)]
        public static async Task DeleteCoop(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"Error: Unable to find co-op, is this posted in a co-op channel?");
            } else {
                db.Remove(coop);
                await db.SaveChangesAsync();
                await ((SocketTextChannel)command.Channel).DeleteAsync();
            }
        }
    }
}
