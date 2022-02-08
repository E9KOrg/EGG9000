using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;

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

using static EGG9000.Bot.Helpers.FixedWidthTable;
using EGG9000.Common.Helpers;

namespace EGG9000.Bot.Commands {
    public static class MeritCommands {
        [SlashCommand(Description = "Add merit to user(s)", AdminOnly = true, AllowFarmHand = true)]
        public static async Task AddMerit(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client,
            [SlashParam(Description = "Merit Reason")] string reason,
            [SlashParam] SocketGuildUser[] users
            ) {
            await command.RespondAsync("Adding Merits");
            var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);


            foreach(var mention in users) {
                await CreateMerit(reason, db, _client, mention, admin.Id, command.Channel, command);
            }
            await command.DeleteResponseFix();
        }
        public static async Task CreateMerit(string reason, ApplicationDbContext db, DiscordSocketClient _client, SocketUser target, Guid adminid, ISocketMessageChannel messageChannel, SocketSlashCommand command = null) {

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

            //if(message.Reference?.MessageId != null) {
            //    merit.Reason += $" {(await _client.GetGuild(message.Reference.GuildId.Value).GetTextChannel(message.Reference.ChannelId).GetMessageAsync(message.Reference.MessageId.Value)).Content}";
            //}

            if(user.GuildId == 656455567858073601) {
                var meritChannel = _client.Guilds.First(x => x.Id == 656455567858073601).TextChannels.First(x => x.Id == 833016587891376128);
                await meritChannel.SendMessageAsync($"{target.Mention}: {merit.Reason} (Merits: {count})");
            }

            if(command != null) {
                await command.Channel.SendMessageAsync($"Merit Added {target.Mention}: {merit.Reason} (Merits: {count})");
            }
            
            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Remove merit from user", AdminOnly = true, AllowFarmHand = true)]
        public static async Task RemoveMerit(SocketSlashCommand command, [SlashParam] SocketGuildUser user, ApplicationDbContext db) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
                var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);


                var merit = await db.Merit.AsQueryable().Where(x => x.UserId == dbuser.Id).OrderByDescending(x => x.When).FirstOrDefaultAsync();
                if(merit == null) {
                    await command.RespondAsync($"There are no recent merits for {user.Mention}");
                    return;
                }
                db.Remove(merit);
                await db.SaveChangesAsync();

                var count = await db.Merit.AsQueryable().Where(x => x.UserId == dbuser.Id).CountAsync();

                await command.RespondAsync($"Merit removed for {user.Mention}, they currently have {count} merits");
            } catch(Exception e) {
                await command.RespondAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        [SlashCommand(Description = "List merits for user", AdminOnly = true, AllowFarmHand = true)]
        public static async Task MeritsForUser(SocketSlashCommand command, [SlashParam] SocketGuildUser targetUser, ApplicationDbContext db) {
            try {
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);


                var merits = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    string msg;
                    msg = $"There are no merits for {targetUser.Mention}";
                    await command.RespondAsync(msg);
                    return;
                }

                var i = 1;
                var meritDesc = String.Join("\n", merits.Select(x => {
                    return $"{i++}: {x.Reason}";
                }));

                await command.RespondAsync($"Merit info for {targetUser.Mention}\n{meritDesc}");
            } catch(Exception e) {
                await command.RespondAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        [SlashCommand(Description = "List your merits")]
        public static async Task Merits(SocketSlashCommand command, ApplicationDbContext db) {
            try {
                SocketUser socketUser = command.User;
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);


                var merits = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    string msg;
                    msg = $"There are no merits for {socketUser.Mention}";
                    await command.RespondAsync(msg);
                    return;
                }

                var i = 1;
                var meritDesc = String.Join("\n", merits.Select(x => {
                    return $"{i++}: {x.Reason}";
                }));

                await command.RespondAsync($"Merit info for {socketUser.Mention}\n{meritDesc}");
            } catch(Exception e) {
                await command.RespondAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

    }
}

