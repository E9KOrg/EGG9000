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
        [SlashCommand(Description = "How many SE/PE needed for next rank up")]
        public static async Task NextRank(SocketSlashCommand command, ApplicationDbContext db) {
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("ERROR: Unable to find backups for this user");
                return;
            }
            var msg = "";
            foreach(var id in user.EggIncIds) {
                var backup = user.Backups.FirstOrDefault(x => x.EggIncId == id.Id);
                if(backup == null)
                    continue;
                var nextSubRank = SIPrefix.GetNextRankInfo(backup, true);
                if(user.EggIncIds.Count > 1) {
                    msg += $"**{backup.UserName}**\n";
                }
                msg += $"To Reach Rank: {nextSubRank.First().Rank}\n";
                msg += String.Join("", nextSubRank.Take(5).Select(x => $"PE: {x.EggsOfProphecy} SE: {x.SoulsEggs.ToEggString()}\n"));

                var nextRank = SIPrefix.GetNextRankInfo(backup, false);
                if(nextRank.First().SoulsEggs != nextSubRank.First().SoulsEggs) {
                    msg += $"To Reach Rank: {nextRank.First().Rank}\n";
                    msg += String.Join("", nextRank.Take(5).Select(x => $"PE: {x.EggsOfProphecy} SE: {x.SoulsEggs.ToEggString()}\n"));
                }
            }
            await command.RespondAsync(msg);
        }

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
                await command.RespondAsync($"ERROR: Command only works in co-op channels", ephemeral: true);
                return;
            }
            var user = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == command.User.Id);

            var xref = await db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.UserId == user.Id && x.Coop.DiscordChannelId == command.Channel.Id);

            xref.PingOnFull = !xref.PingOnFull;
            await db.SaveChangesAsync();
            if(xref.PingOnFull) {
                await command.RespondAsync($"Will receive DM ping when everyone has joined", ephemeral: true);
            } else {
                await command.RespondAsync($"Will no longer receive ping", ephemeral: true);
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
                await coopStatusUpdater.SendUpdate(targetCoop.Id, guild, users, dbguild, default);
                await command.ModifyOriginalResponseAsync(m => m.Content = "Co-op Updated");
                return;
            }

            var targetGuildContract = await db.GuildContracts.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetGuildContract != null) {
                await command.RespondAsync("Updating contract...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetGuildContract.GuildID);
                var dbusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                //var backups = await apiLink.GetUserBackups(dbusers, db);
                var backups = dbusers.Where(x => x.Backups is not null).SelectMany(x => x.Backups.Where(y => x.EggIncIds.Any(eid => eid.Id == y.EggIncId)).Select(y => new LeaderboardUser { User = x, Backup = y })).ToList();

                await contractUpdater.UpdateContractChannel(db, backups, targetGuildContract, guild, dbguild, dbusers, command);
                await command.ModifyOriginalResponseAsync(x => x.Content = "Content Updated");
                //await command.DeleteOriginalResponseAsync();
                return;
            }
            await command.RespondAsync($"ERROR: Command only works in contract or co-op channels");
        }

        [SlashCommand(Description = "Test delete response")]
        public static async Task TestDeleteResponse1(SocketSlashCommand command) {
            await command.RespondAsync("Test Response");
            await Task.Delay(1000);
            await command.ModifyOriginalResponseAsync(x => x.Content = "After Delay");
        }
        //[SlashCommand(Description = "Test delete response")]
        //public static async Task TestDeleteResponse1(SocketSlashCommand command) {
        //    await command.RespondAsync("Test Respoinse");
        //    await Task.Delay(1000);
        //    await command.DeleteOriginalResponseAsync();
        //}
        //[SlashCommand(Description = "Test delete response")]
        //public static async Task TestDeleteResponse2(SocketSlashCommand command) {
        //    await command.RespondAsync("Test Respoinse");
        //    await Task.Delay(1000);
        //    await (await command.GetOriginalResponseAsync()).DeleteAsync();
        //}
        //[SlashCommand(Description = "Test delete response")]
        //public static async Task TestDeleteResponse3(SocketSlashCommand command) {
        //    await command.RespondAsync("Test Respoinse", ephemeral: true);
        //    await Task.Delay(1000);
        //    await (await command.GetOriginalResponseAsync()).DeleteAsync();
        //}
        [SlashCommand(Description = "Adds a temporary role for users that last a specific amount of time", AdminOnly = true, AllowFarmHand = true)]
        public static async Task TempRole(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] SocketRole role, [SlashParam] string timespan, [SlashParam] string reason, [SlashParam] SocketGuildUser[] users) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }
            await command.DeferAsync();
            var userids = users.Select(x => x.Id);
            var existingTempRoles = await db.TemporaryRoles.Where(x => x.RoleId == role.Id && x.Expires > DateTimeOffset.Now && userids.Contains(x.UserId)).ToListAsync();
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            foreach(var user in users) {
                var tempRole = existingTempRoles.FirstOrDefault(x => x.RoleId == role.Id && user.Id == x.UserId);
                if(tempRole == null) {
                    tempRole = new TemporaryRole { RoleId = role.Id, Created = DateTimeOffset.Now, UserId = user.Id, GuildId = guild.Id };
                    db.Add(tempRole);
                    await user.AddRoleAsync(role);
                }
                tempRole.Reason = reason;
                tempRole.Expires = expireTime;
            }

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the role {role.Emoji} {role.Name} to the following {"user".ToQuantity(users.Count(), ShowQuantityAs.None)} {string.Join(", ", users.Select(x => x.Mention))} until <t:{expireTime.ToUnixTimeSeconds()}:f> for the reason: {reason}");
        }

        [SlashCommand(Description = "Adds a temporary name to be used for co-op naming", AdminOnly = true, ParentCommand = "a", CPOnly = true)]
        public static async Task TempCustomCoopName(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string customName, [SlashParam] string timespan, [SlashParam] SocketGuildUser user) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }
            await command.DeferAsync();
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == user.Id);
            dbuser.CustomCoopName = customName;
            dbuser.ExpireCustomCoopName = expireTime;

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the custom name {customName} to {user.Mention} until <t:{expireTime.ToUnixTimeSeconds()}:f>");
        }

        [SlashCommand(Description = "Get help from staff, please give details", CPOnly = true)]
        public static async Task CallStaff(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string details, [SlashParam(Description = "If private then only staff will see your message", Required = false)] bool keepPrivate = false) {
            var channel = client.Guilds.First(x => x.Id == 656455567858073601).TextChannels.First(x => x.Id == 940777970111488050);
            await channel.SendMessageAsync($"<@&904799345122091018>: {command.User.Mention} called for staff in <#{command.Channel.Id}> with the details: {details}");
            if(keepPrivate) {

                await command.RespondAsync("Staff has been called.", ephemeral: true);
            } else {
                await command.RespondAsync($"Staff has been called ({details})");
            }
        }
    }
}

