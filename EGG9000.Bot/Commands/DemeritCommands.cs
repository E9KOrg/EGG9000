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
using EGG9000.Common.Services;
using EGG9000.Common.Commands;

namespace EGG9000.Bot.Commands {
    public static class DemeritCommands {
        [SlashCommand(Description = "Add demerit to user", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task AddDemerit(FauxCommand command, [SlashParam] SocketGuildUser user, [SlashParam] string reason, ApplicationDbContext db, DiscordHostedService discordClient) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
                var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

                var demerit = new Demerit {
                    When = DateTimeOffset.Now,
                    AdminUserId = admin.Id,
                    UserId = dbuser.Id,
                    Id = Guid.NewGuid(),
                    Reason = reason
                };
                db.Demerit.Add(demerit);
                await db.SaveChangesAsync();

                var count = await db.Demerit.AsQueryable().Where(x => x.UserId == dbuser.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();

                var message = $"Demerit added to {user.Mention} for the reason: {demerit.Reason}\nThey currently have {count} demerits";
                await command.RespondAsync(message);

                var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId);
                var demeritChannel = await discordClient.GetChannelAsync(GuildChannelType.DemeritLogChannel, dbguild);
                if(demeritChannel is not null) {
                    if(count >= 3) {
                        message = $"**{message}**";
                    }
                    await demeritChannel.SendMessageAsync(message);
                }
            } catch(Exception e) {
                await command.RespondAsync($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        [SlashCommand(Description = "Remove latest demerit from user", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task RemoveDemerit(FauxCommand command, [SlashParam] SocketGuildUser user, ApplicationDbContext db) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
                var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);


                var demerit = await db.Demerit.AsQueryable().Where(x => x.UserId == dbuser.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).OrderByDescending(x => x.When).FirstOrDefaultAsync();
                if(demerit == null) {
                    await command.RespondAsync($"There are no recent demerits for {user.Mention}");
                    return;
                }
                db.Remove(demerit);
                await db.SaveChangesAsync();

                var count = await db.Demerit.AsQueryable().Where(x => x.UserId == dbuser.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();

                await command.RespondAsync($"Demerit removed for {user.Mention}, they currently have {count} demerits");
            } catch(Exception e) {
                await command.RespondAsync($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        [SlashCommand(Description = "List your demerits", AllowInDMs = true)]
        public static async Task Demerits(FauxCommand command, ApplicationDbContext db) {
            try {
                IUser socketUser = command.User;
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);


                var demerits = await db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).ToListAsync();
                if(demerits.Count == 0) {
                    string msg;
                    var msgs = new List<string> {
                            "How does a demerit sound for asking me that which you should already know",
                            "I really should give you a demerit so you can know what it feels like",
                            "No demerits, maybe I'll give you one just for fun"
                        };
                    msg = msgs.Skip(new Random().Next(0, msgs.Count)).Take(1).First();
                    await command.RespondAsync(msg, ephemeral: true);
                    return;
                }

                var demeritDesc = String.Join("\n", demerits.Select(x => {
                    var monthAgo = DateTimeOffset.Now.AddMonths(-1);
                    var timeLeft = monthAgo - x.When;
                    return $"Expires in {timeLeft.Humanize(2)} for reason: {x.Reason}";
                }));

                await command.RespondAsync($"Demerit info for {socketUser.Mention}\n{demeritDesc}", ephemeral: true);
            } catch(Exception e) {
                await command.RespondAsync($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }
        [SlashCommand(Description = "List demerits for user", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task DemeritsForUser(FauxCommand command, [SlashParam] SocketGuildUser user, ApplicationDbContext db) {
            try {
                var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

                var demeritDesc = await GetDemerits(dbuser.Id, db);

                await command.RespondAsync($"Demerit info for {user.Mention}\n{demeritDesc}", ephemeral: true);
            } catch(Exception e) {
                await command.RespondAsync($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        public static async Task<string> GetDemerits(Guid dbuserid, ApplicationDbContext db) {
            var demerits = await db.Demerit.AsQueryable().Where(x => x.UserId == dbuserid && x.When > DateTimeOffset.Now.AddMonths(-1)).ToListAsync();
            if(demerits.Count == 0) {
                string msg;
                msg = $"There are no recent demerits";
                return msg;
            }

            var demeritDesc = String.Join("\n", demerits.Select(x => {
                var monthAgo = DateTimeOffset.Now.AddMonths(-1);
                var timeLeft = monthAgo - x.When;
                return $"Expires in {timeLeft.Humanize(2)} for reason: {x.Reason}";
            }));

            return demeritDesc;
        }

        [SlashCommand(Description = "Stops user from getting demerit in co-op", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task NoDemerit(FauxCommand command, [SlashParam] SocketGuildUser user, ApplicationDbContext db) {
            UserCoopXref xref;
            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            xref = await db.UserCoopXrefs.AsQueryable().Where(xref => xref.User.DiscordId == user.Id && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();

            if(xref == null) {
                await command.RespondAsync($"⚠️ERROR: Unabled to find user");
                return;
            }

            xref.NoDemerit = true;
            await db.SaveChangesAsync();
            await command.RespondAsync($"{user?.Mention ?? xref.User.DiscordUsername} will not receive automated demerits in this co-op.");
        }
    }
}

