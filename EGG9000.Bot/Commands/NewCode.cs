using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class NewCode {
        [SlashCommand(Description = "Generate a new co-op code, a channel will be created for the co-op", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task NewCoopCode(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            var words = new Words();
            var wordOne = words.GetRandomWord();
            var wordTwo = words.GetRandomSecondWord(wordOne);
            var code = wordOne + wordTwo + words.GetRandomNumber();

            var guild = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId);

            var coop = new Coop { Name = code, Created = DateTimeOffset.Now, GuildId = guild.Id };
            db.Coops.Add(coop);
            await db.SaveChangesAsync();

            await command.RespondAsync(code);
        }

        [SlashCommand(Description = "Delete co-op channel from discord and database ", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task DeleteCoop(FauxCommand command, ApplicationDbContext db) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to find co-op, is this posted in a co-op channel?"));
            } else {
                db.Remove(coop);
                await db.SaveChangesAsync();
                await ((SocketThreadChannel)command.Channel).ModifyAsync(c => {
                    c.Archived = true;
                    c.Locked = true;
                });
            }
        }

        [SlashCommand(Description = "Delete thread for debugging", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task DeleteThread(FauxCommand command, ApplicationDbContext db) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to find co-op, is this posted in a co-op channel?"));
            } else {
                coop.ThreadID = 0;
                coop.ThreadArchived = false;
                coop.Finished = false;
                coop.Status = CoopStatusEnum.AllAssignedJoined;
                await db.SaveChangesAsync();
                await ((SocketThreadChannel)command.Channel).DeleteAsync();
            }
        }
    }
}
