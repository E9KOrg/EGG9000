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

namespace EGG9000.Bot.Commands {
    public static class DemeritCommands {
        public static async Task AddDemerit(SocketMessage message, string[] args, ApplicationDbContext db) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == message.Author.Id);
                SocketUser socketUser = message.MentionedUsers.FirstOrDefault();
                if(socketUser == null) {
                    await message.Channel.SendMessageAsync($"ERROR: Bot error - Missing User Mention");
                }
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);

                Console.WriteLine(JsonConvert.SerializeObject(args, Formatting.Indented));

                var demerit = new Demerit {
                    When = DateTimeOffset.Now,
                    AdminUserId = admin.Id,
                    UserId = user.Id,
                    Id = Guid.NewGuid(),
                    Reason = String.Join(" ", args.Where(x => !x.StartsWith("<@")))
                };
                db.Demerit.Add(demerit);
                await db.SaveChangesAsync();

                var count = await db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();

                await message.Channel.SendMessageAsync($"Demerit added to {socketUser.Mention} for the reason: {demerit.Reason}\nThey currently have {count} demerits");
            } catch(Exception e) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        public static async Task RemoveDemerit(SocketMessage message, string[] args, ApplicationDbContext db) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == message.Author.Id);
                SocketUser socketUser = message.MentionedUsers.FirstOrDefault();
                if(socketUser == null) {
                    await message.Channel.SendMessageAsync($"ERROR: Bot error - Missing User Mention");
                }
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);


                var demerit = await db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).FirstOrDefaultAsync();
                if(demerit == null) {
                    await message.Channel.SendMessageAsync($"There are no recent demerits for {socketUser.Mention}");
                    return;
                }
                db.Remove(demerit);
                await db.SaveChangesAsync();

                var count = await db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).CountAsync();

                await message.Channel.SendMessageAsync($"Demerit removed for {socketUser.Mention}, they currently have {count} demerits");
            } catch(Exception e) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        public static async Task Demerits(SocketMessage message, string[] args, ApplicationDbContext db) {
            try {
                SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);


                var demerits = await db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.Now.AddMonths(-1)).ToListAsync();
                if(demerits.Count == 0) {
                    string msg;
                    if(socketUser == message.Author) {
                        var msgs = new List<string> {
                            "How does a demerit sound for asking me that which you should already know",
                            "I really should give you a demerit so you can know what it feels like",
                            "No demerits, maybe I'll give you one just for fun"
                        };
                        msg = msgs.Skip(new Random().Next(0, msgs.Count)).Take(1).First();
                    } else {
                        msg = $"There are no recent demerits for {socketUser.Mention}";
                    }
                    await message.Channel.SendMessageAsync(msg);
                    return;
                }

                var demeritDesc = String.Join("\n", demerits.Select(x => {
                    var monthAgo = DateTimeOffset.Now.AddMonths(-1);
                    var timeLeft = monthAgo - x.When;
                    return $"Expires in {timeLeft.Humanize(2)} for reason: {x.Reason}";
                }));

                await message.Channel.SendMessageAsync($"Demerit info for {socketUser.Mention}\n{demeritDesc}");
            } catch(Exception e) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }

        public static async Task NoDemerit(SocketMessage message, string[] args, ApplicationDbContext db) {
            var user = message.MentionedUsers.FirstOrDefault();
            UserCoopXref xref;
            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == message.Channel.Id);
            if(user == null) {
                xref = await db.UserCoopXrefs.Include(x => x.User).AsQueryable().Where(xref => xref.User.DiscordUsername.Contains(args[0]) && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            } else {
                xref = await db.UserCoopXrefs.AsQueryable().Where(xref => xref.User.DiscordId == user.Id && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();

            }

            if(xref == null) {
                await message.Channel.SendMessageAsync($"ERROR: Unabled to find user");
                return;
            }

            xref.NoDemerit = true;
            await db.SaveChangesAsync();
            await message.Channel.SendMessageAsync($"{user?.Mention ?? xref.User.DiscordUsername} will not receive automated demerits in this co-op.");
        }
    }
}

