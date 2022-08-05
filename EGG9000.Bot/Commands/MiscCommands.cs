using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;

using EGG9000.Common.Helpers;

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
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public static class MiscCommands {
        public static async Task TestEmoji(SocketMessage message, string[] args) {
            try {
                var channel = (SocketTextChannel)message.MentionedChannels.First();
                var targetMessage = await channel.GetMessageAsync(ulong.Parse(args[2]));

                var emoteId = ulong.Parse(new Regex(@":(\d+)").Match(args[3]).Groups[1].Value);

                var firstEmote = targetMessage.Reactions.First(x => ((Emote)x.Key).Id == emoteId);
                var users = await targetMessage.GetReactionUsersAsync(firstEmote.Key, 1000).FlattenAsync();

                var otherEmotes = targetMessage.Reactions.Where(x => x.Key is Emote emote && emote.Id != emoteId);
                var otherUsers = new List<IUser>();
                foreach(var emote in otherEmotes) {
                    otherUsers.AddRange(await targetMessage.GetReactionUsersAsync(firstEmote.Key, 1000).FlattenAsync());
                }

                var voterIDs = users.Select(x => x.Id).ToList();
                var otherIDs = otherUsers.Select(x => x.Id).ToList();
                var dualVoterIDs = voterIDs.Intersect(otherIDs);
                var dualVoters = users.Where(x => dualVoterIDs.Contains(x.Id));
                var singleVoters = users.Where(x => !dualVoterIDs.Contains(x.Id));
                await message.Channel.SendMessageAsync($"Users with two votes {firstEmote.Key.Name}: {String.Join(", ", dualVoters.Select(x => x.Mention))}");
                //foreach(IGuildUser user in users) {
                //    await user.AddRoleAsync(message.MentionedRoles.First());
                //}


                await message.Channel.SendMessageAsync($"Users with single vote and  <:{firstEmote.Key.Name}:{((Emote)firstEmote.Key).Id}> {String.Join(", ", singleVoters.Select(x => x.Mention))}");
            } catch(Exception e) {
                Console.WriteLine($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
                await message.Channel.SendMessageAsync($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
            }
        }


        public static async Task RenameCoop(SocketMessage message, string[] args, ApplicationDbContext db)
        {
            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == message.Channel.Id);
            if(targetCoop == null)
            {
                await message.Channel.SendMessageAsync($"⚠️ERROR: Command only works in co-op channels");
                return;
            }

            if(args.Length == 0 || args[0].Length < 2)
            {
                await message.Channel.SendMessageAsync($"⚠️ERROR: Missing new co-op name");
                return;

            }
            var newName = args[0];

            targetCoop.Name = newName;
            await db.SaveChangesAsync();
            await message.Channel.SendMessageAsync($"Co-op renamed to {newName}");
        }
        public static async Task PingOnFull(SocketMessage message, string[] args, ApplicationDbContext db)
        {
            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == message.Channel.Id);
            if(targetCoop == null)
            {
                await message.Channel.SendMessageAsync($"⚠️ERROR: Command only works in co-op channels");
                return;
            }
            var discordUserId = message.MentionedUsers.FirstOrDefault()?.Id ?? message.Author.Id;
            var user = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == discordUserId);

            var xref = await db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.User.DiscordId == message.Author.Id && x.Coop.DiscordChannelId == message.Channel.Id);

            xref.PingOnFull = !xref.PingOnFull;
            await db.SaveChangesAsync();
            if(xref.PingOnFull)
            {
                await message.Channel.SendMessageAsync($"Will receive DM ping when everyone has joined");
            } else
            {
                await message.Channel.SendMessageAsync($"Will no longer receive ping");
            }
        }

        //public static async Task StaffCoops(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient discord) {
        //    try {
        //        var guild = discord.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
        //        var admins = guild.Users.Where(x => x.Roles.Any(r => r.Id == 708378160143794177 || r.Id == 759887156029423636 || r.Id == 750797304797069323));

        //        var adminDiscordIds = admins.Select(x => x.Id);

        //        var adminUsers = await db.DBUsers.AsQueryable().Where(x => adminDiscordIds.Contains(x.DiscordId)).ToListAsync();

        //        var adminUserIds = adminUsers.Select(x => x.Id);
        //        var coops = await db.Coops.Include(x => x.UserCoopsXrefs).AsQueryable().Where(x => !x.DeletedChannel && x.UserCoopsXrefs.Any(y => adminUserIds.Contains(y.UserId))).ToListAsync();


        //        var adminsWithChannels = adminUsers.OrderBy(x => x.DiscordUsername).Select(u => new {
        //            Admin = u,
        //            Channels = coops.Where(c => c.UserCoopsXrefs.Any(xref => u.Id == xref.UserId)).Select(c => discord.Guilds.First(g => g.Id == c.OverflowGuildId).TextChannels.First(tc => tc.Id == c.DiscordChannelId).Mention)
        //        });


        //        await message.Channel.SendMessageAsync(string.Join("\n", adminsWithChannels.Select(x => $"{x.Admin.DiscordUsername}: {string.Join(", ", x.Channels)}")));
        //    } catch(Exception e) {
        //        Console.WriteLine($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
        //        await message.Channel.SendMessageAsync($"⚠️ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
        //    }
        //}
    }
}

