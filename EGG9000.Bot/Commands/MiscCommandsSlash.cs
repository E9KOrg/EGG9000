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
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Commands {
    public static class MiscCommandsSlash {
        //public static async Task TestEmoji(SocketMessage message, string[] args) {
        //    try {
        //        var channel = (SocketTextChannel)message.MentionedChannels.First();
        //        var targetMessage = await channel.GetMessageAsync(ulong.Parse(args[2]));

        //        var emoteId = ulong.Parse(new Regex(@":(\d+)").Match(args[3]).Groups[1].Value);

        //        var firstEmote = targetMessage.Reactions.First(x => ((Emote)x.Key).Id == emoteId);
        //        var users = await targetMessage.GetReactionUsersAsync(firstEmote.Key, 1000).FlattenAsync();

        //        var otherEmotes = targetMessage.Reactions.Where(x => x.Key is Emote emote && emote.Id != emoteId);
        //        var otherUsers = new List<IUser>();
        //        foreach(var emote in otherEmotes) {
        //            otherUsers.AddRange(await targetMessage.GetReactionUsersAsync(firstEmote.Key, 1000).FlattenAsync());
        //        }

        //        var voterIDs = users.Select(x => x.Id).ToList();
        //        var otherIDs = otherUsers.Select(x => x.Id).ToList();
        //        var dualVoterIDs = voterIDs.Intersect(otherIDs);
        //        var dualVoters = users.Where(x => dualVoterIDs.Contains(x.Id));
        //        var singleVoters = users.Where(x => !dualVoterIDs.Contains(x.Id));
        //        await message.Channel.SendMessageAsync($"Users with two votes {firstEmote.Key.Name}: {String.Join(", ", dualVoters.Select(x => x.Mention))}");
        //        //foreach(IGuildUser user in users) {
        //        //    await user.AddRoleAsync(message.MentionedRoles.First());
        //        //}


        //        await message.Channel.SendMessageAsync($"Users with single vote and  <:{firstEmote.Key.Name}:{((Emote)firstEmote.Key).Id}> {String.Join(", ", singleVoters.Select(x => x.Mention))}");
        //    } catch(Exception e) {
        //        Console.WriteLine($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
        //        await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message} : {e.StackTrace} : {e.Data}");
        //    }
        //}

        [SlashCommand(Description = "Rename a co-op channel to mistype", AdminOnly = true)]
        public static async Task RenameCoop(SocketSlashCommand command, ApplicationDbContext db, [SlashParam] string correctcoopname) {
            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.RespondAsync($"ERROR: Command only works in co-op channels");
                return;
            }


            targetCoop.Name = correctcoopname;
            await db.SaveChangesAsync();
            await command.RespondAsync($"Co-op renamed to {correctcoopname}");
        }

        [SlashCommand(Description = "Get a ping from the bot via DM and all assigned members have joined")]
        public static async Task PingOnFull(SocketSlashCommand command, ApplicationDbContext db) {
            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.RespondAsync($"ERROR: Command only works in co-op channels");
                return;
            }
            var user = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == command.User.Id);

            var xref = await db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.UserId == user.Id && x.Coop.DiscordChannelId == command.Channel.Id);

            xref.PingOnFull = !xref.PingOnFull;
            await db.SaveChangesAsync();
            if(xref.PingOnFull) {
                await command.RespondAsync($"Will receive DM ping when everyone has joined");
            } else {
                await command.RespondAsync($"Will no longer receive ping");
            }
        }

        [SlashCommand(Description = "Trigger an update for a co-op or contract channel", AdminOnly = true)]
        public static async Task UpdateChannel(SocketSlashCommand command, ApplicationDbContext db, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient discord, ContractUpdater contractUpdater, APILink apiLink) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop != null) {
                await command.RespondAsync("Updating coop...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetCoop.GuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                await coopStatusUpdater.SendUpdate(targetCoop.Id, guild, users, dbguild);
                await command.ModifyOriginalResponseAsync(m => m.Content = "Co-op Update");
                return;
            }

            var targetGuildContract = await db.GuildContracts.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetGuildContract != null) {
                await command.RespondAsync("Updating contract...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetGuildContract.GuildID);
                var dbusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                var backups = await apiLink.GetUserBackups(dbusers, db);

                await contractUpdater.UpdateContractChannel(db, backups, targetGuildContract, guild, dbguild, dbusers);
                await command.DeleteOriginalResponseAsync();
                return;
            } 
            await command.RespondAsync($"ERROR: Command only works in contract or co-op channels");
        }
    }
}

