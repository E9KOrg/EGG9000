using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class MeritCommands {
        public static async Task CreateMerit(string reason, ApplicationDbContext db, DiscordSocketClient _client, SocketUser target, Guid? adminid, SocketInteraction command = null, Guild guild = null) {

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == target.Id);

            var merit = new Merit {
                When = DateTimeOffset.UtcNow,
                AdminUserId = adminid,
                UserId = user.Id,
                //Id = Guid.NewGuid(),
                Reason = reason
            };
            db.Merit.Add(merit);
            var count = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).CountAsync();
            count++;

            await db.SaveChangesAsync();

            if(command is not null || guild is not null) {
                var guildFind = guild;
                guildFind ??= db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);
                if(guildFind is not null) {
                    var socketGuild = _client.Guilds.First(x => x.Id == guildFind.Id);
                    if(socketGuild is not null) {
                        var response = await ChannelHelper.DetermineAndSend(_client, guildFind, GuildChannelType.MeritLogChannel, new() { Text = $"{target.Mention}: {merit.Reason} (Merits: {count})" });
                    }
                }
            }

            if(command != null) {
                await command.Channel.SendMessageAsync($"Merit Added {target.Mention}: {merit.Reason} (Merits: {count})");
            }

        }
    }
}
