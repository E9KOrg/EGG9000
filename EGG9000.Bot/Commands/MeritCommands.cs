using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using EGG9000.Bot.Common.Helpers;
using System.Diagnostics;

namespace EGG9000.Bot.Commands {
    public static class MeritCommands {
        [SlashCommand(Description = "Add merit to user(s)", AdminOnly = StaffOnlyLevel.ChickenTender)]
        public static async Task AddMerit(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client,
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
        public static async Task CreateMerit(string reason, ApplicationDbContext db, DiscordSocketClient _client, SocketUser target, Guid adminid, IMessageChannel channel, FauxCommand command = null) {

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

            if(command is not null) {
                var guildFind = db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);
                if(guildFind is not null) {
                    var socketGuild = _client.Guilds.First(x => x.Id == guildFind.Id);
                    if(socketGuild is not null) {
                        var response = await ChannelHelper.DetermineAndSend(db, _client, guildFind, socketGuild, GuildChannelType.MeritLogChannel, new() { Text = $"{target.Mention}: {merit.Reason} (Merits: {count})" });
                    }
                }
            }

            if(command != null) {
                await command.Channel.SendMessageAsync($"Merit Added {target.Mention}: {merit.Reason} (Merits: {count})");
            }

            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Remove merit from user", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RemoveMerit(FauxCommand command, [SlashParam] SocketGuildUser user, ApplicationDbContext db) {
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
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
        }

        [SlashCommand(Description = "List merits for user", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task MeritsForUser(FauxCommand command, [SlashParam] SocketGuildUser targetUser, ApplicationDbContext db) {
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
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
        }

        [SlashCommand(Description = "List your merits", AllowInDMs = true)]
        public static async Task Merits(FauxCommand command, ApplicationDbContext db) {
            try {
                IUser socketUser = command.User;
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

                await command.RespondAsync($"Merit info for {socketUser.Mention}\n{meritDesc}", ephemeral: true);
            } catch(Exception e) {
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
        }

    }
}

