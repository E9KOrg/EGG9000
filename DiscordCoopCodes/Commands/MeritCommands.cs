using Discord;
using Discord.WebSocket;

using DiscordCoopCodes.Database;
using DiscordCoopCodes.Database.Entities;
using DiscordCoopCodes.EggIncAPI;
using DiscordCoopCodes.Helpers;

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

using static DiscordCoopCodes.Helpers.FixedWidthTable;

namespace DiscordCoopCodes.Commands {
    public static class MeritCommands {
        public static async Task AddMerit(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == message.Author.Id);
            if(message.MentionedUsers.Count == 0) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - Missing User Mention");
            }


            foreach(var mention in message.MentionedUsers) {
                await CreateMerit(message, String.Join(" ", args.Where(x => !x.StartsWith("<@"))), db, _client, mention, admin.Id, message.Channel);
            }
        }
        public static async Task CreateMerit(SocketMessage message, string reason, ApplicationDbContext db, DiscordSocketClient _client, SocketUser target, Guid adminid, ISocketMessageChannel messageChannel) {

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == target.Id);

            var merit = new Merit {
                When = DateTimeOffset.Now,
                AdminUserId = adminid,
                UserId = user.Id,
                Id = Guid.NewGuid(),
                Reason = reason
            };
            db.Merit.Add(merit);
            var count = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).CountAsync();
            count++;

            if(message.Reference?.MessageId != null) {
                merit.Reason += $" {(await _client.GetGuild(message.Reference.GuildId.Value).GetTextChannel(message.Reference.ChannelId).GetMessageAsync(message.Reference.MessageId.Value)).Content}";
            }

            if(user.GuildId == 656455567858073601) {
                var meritChannel = _client.Guilds.First(x => x.Id == 656455567858073601).TextChannels.First(x => x.Id == 833016587891376128);
                await meritChannel.SendMessageAsync($"{target.Mention}: {merit.Reason} (Merits: {count})");
            }

            await messageChannel.SendMessageAsync($"Merit Added {target.Mention}: {merit.Reason} (Merits: {count})");
            await db.SaveChangesAsync();
        }

        public static async Task RemoveMerit(SocketMessage message, string[] args, ApplicationDbContext db) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == message.Author.Id);
                SocketUser socketUser = message.MentionedUsers.FirstOrDefault();
                if(socketUser == null) {
                    await message.Channel.SendMessageAsync($"ERROR: Bot error - Missing User Mention");
                }
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);


                var merit = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).FirstOrDefaultAsync();
                if(merit == null) {
                    await message.Channel.SendMessageAsync($"There are no recent merits for {socketUser.Mention}");
                    return;
                }
                db.Remove(merit);
                await db.SaveChangesAsync();

                var count = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).CountAsync();

                await message.Channel.SendMessageAsync($"Merit removed for {socketUser.Mention}, they currently have {count} merits");
            } catch(Exception e) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        public static async Task Merits(SocketMessage message, string[] args, ApplicationDbContext db) {
            try {
                SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);


                var merits = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    string msg;
                    msg = $"There are no merits for {socketUser.Mention}";
                    await message.Channel.SendMessageAsync(msg);
                    return;
                }

                var i = 1;
                var meritDesc = String.Join("\n", merits.Select(x => {
                    return $"{i++}: {x.Reason}";
                }));

                await message.Channel.SendMessageAsync($"Merit info for {socketUser.Mention}\n{meritDesc}");
            } catch(Exception e) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

    }
}

