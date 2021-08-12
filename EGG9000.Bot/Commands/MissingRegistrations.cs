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
    public class MissingRegistrations {
        public static async Task Run(SocketMessage message, ApplicationDbContext db, DiscordSocketClient client) {
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            var dbUsers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync();
            var discordUsers = guild.Users;
            var missingUsers = new List<IUser>();
            var activeUsers = new List<IUser>();
            foreach (var discordUser in discordUsers) {
                if (!dbUsers.Any(x => x.DiscordId == discordUser.Id) && !discordUser.IsBot) {
                    missingUsers.Add(discordUser);
                } else {
                    activeUsers.Add(discordUser);
                }
            }

            var usersNotSeenInCoop = dbUsers.Where(x => !x.EggIncIds.Any(y => !string.IsNullOrEmpty(y.Id)));


            var msg = "";

            if (missingUsers.Count > 0)
                msg += $"The following users are not registered with the bot: \nPlease use the command \"!addname [eggincname]\"\n{string.Join("\n", missingUsers.OrderBy(x => x.Username).Select(x => x.Mention))}\n";
            if (usersNotSeenInCoop.Count() > 0) {
                var list = usersNotSeenInCoop.OrderBy(x => x.DiscordUsername).Select(x => {
                    var mention = client.GetUser(x.DiscordId)?.Mention;
                    if (mention == null)
                        return null;
                    return mention + $" EggIncName: **{String.Join(",", x.EggIncIds.Select(y => y.Name))}**";
                }).Where(x => x != null);
                msg += $"The following users are registered but have not been seen in a coop (possibly wrong EggInc name): \n{string.Join("\n", list)}";
            }
            //msg += $"Active Users: \n{string.Join("\n", activeUsers.OrderBy(x => x.Username).Select(x => x.Mention))}\n";
            await message.Channel.SendMessageAsync(msg);
        }
    }
}
