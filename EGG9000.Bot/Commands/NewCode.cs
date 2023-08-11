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
using EGG9000.Common.Services;
using EGG9000.Common.Commands;

namespace EGG9000.Bot.Commands {
    public class NewCode {
        [SlashCommand(Description = "Generate a new co-op code, a channel will be created for the co-op", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task NewCoopCode(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            var words = new Words();
            var wordOne = words.GetRandomWord();
            var wordTwo = words.GetRandomSecondWord(wordOne);
            var code = wordOne + wordTwo + words.GetRandomNumber();

            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));

            var coop = new Coop { Name = code, Created = DateTimeOffset.Now, GuildId = guild.Id };
            db.Coops.Add(coop);
            await db.SaveChangesAsync();

            await command.RespondAsync(code);
        }

        [SlashCommand(Description = "Delete co-op channel from discord and database ", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task DeleteCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find co-op, is this posted in a co-op channel?");
            } else {
                db.Remove(coop);
                await db.SaveChangesAsync();
                await ((SocketTextChannel)command.Channel).DeleteAsync();
            }
        }
    }
}
