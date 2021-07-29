using Discord;
using Discord.WebSocket;
using DiscordCoopCodes.Database;
using DiscordCoopCodes.Database.Entities;
using DiscordCoopCodes.Helpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordCoopCodes.Commands {
    public class NewCode {
        public static async Task ExecuteAsync(SocketMessage message, ApplicationDbContext db, DiscordSocketClient client) {
            var words = new Words();
            var code = words.GetRandomWord() + words.GetRandomWord() + words.GetRandomNumber();

            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            var coopCategory = guild.GetCoopCategory();
            //var channel = await guild.CreateTextChannelAsync(code, x => x.CategoryId = coopCategory.Id);

            var coop = new Coop { Name = code, Created = DateTimeOffset.Now, GuildId = guild.Id };//, DiscordChannelId = channel.Id };
            db.Coops.Add(coop);
            await db.SaveChangesAsync();

            await message.Channel.SendMessageAsync(code);
        }
        public static async Task DeleteCoop(SocketMessage message, ApplicationDbContext db, DiscordSocketClient client) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == message.Channel.Id);
            if(coop == null) {
                await message.Channel.SendMessageAsync($"Error: Unable to find co-op, is this posted in a co-op channel?");
            } else {
                db.Remove(coop);
                await db.SaveChangesAsync();
                await ((SocketTextChannel)message.Channel).DeleteAsync();
            }
        }
    }
}
