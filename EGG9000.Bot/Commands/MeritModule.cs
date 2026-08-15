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
    public class MeritModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient gateway) : Interactions.E9KModuleBase(dbFactory) {
        private async Task RespondWithMeritList(string mentionText, List<Merit> merits, bool ephemeral) {
            var i = 1;
            var meritDesc = string.Join("\n", merits.Select(x => $"{i++}: {x.Reason}"));
            var header = $"Merit info for {mentionText}\n";

            if((header.Length + meritDesc.Length) <= 1900) {
                await Context.Interaction.RespondAsyncGettingMessage(header + meritDesc, ephemeral: ephemeral);
            } else {
                await Context.Interaction.RespondWithFilesAsyncGettingMessage(
                    [new FileAttachment(new MemoryStream(Encoding.UTF8.GetBytes(meritDesc)), "Merits.txt")],
                    text: header + "_(List too large for Discord - see attached file)_",
                    ephemeral: ephemeral);
            }
        }

        [SlashCommand("addmerit", "Add merit to user(s)")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        [Interactions.StaffOnly(Interactions.StaffTier.ChickenTender)]
        public async Task AddMerit(
            [Summary("reason", "Merit Reason")] string reason,
            [ComplexParameter] Interactions.UserSlots userSlots) {
            await Context.Interaction.RespondAsyncGettingMessage("Adding Merits");
            var users = userSlots.Users;
            var admin = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);

            var dbGuild = await Db.Guilds.FirstOrDefaultAsync(x => x.Id == Context.Interaction.GuildId || x.OverflowServersJson.IndexOf(Context.Interaction.GuildId.ToString()) > -1);

            foreach(var mention in users) {
                await MeritCommands.CreateMerit(reason, Db, gateway, mention, admin.Id, guild: dbGuild);
                var dbMention = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == mention.Id);
                var count = await Db.Merit.AsQueryable().Where(x => x.UserId == dbMention.Id).CountAsync();
                await Context.Channel.SendMessageAsync($"Merit Added {mention.Mention}: {reason} (Merits: {count})");
            }
            await Context.Interaction.DeleteResponseFix();
        }

        [SlashCommand("removemerit", "Remove merit from user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        [Interactions.StaffOnly(Interactions.StaffTier.ChickenTender)]
        public async Task RemoveMerit([Summary("user", "user")] SocketGuildUser user) {
            try {
                var admin = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
                var dbuser = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);


                var merit = await Db.Merit.AsQueryable().Where(x => x.UserId == dbuser.Id).OrderByDescending(x => x.When).FirstOrDefaultAsync();
                if(merit == null) {
                    await Context.Interaction.RespondAsyncGettingMessage($"There are no recent merits for {user.Mention}");
                    return;
                }
                Db.Remove(merit);
                await Db.SaveChangesAsync();

                var count = await Db.Merit.AsQueryable().Where(x => x.UserId == dbuser.Id).CountAsync();

                await Context.Interaction.RespondAsyncGettingMessage($"Merit removed for {user.Mention}, they currently have {count} merits");
            } catch(Exception e) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("meritsforuser", "List merits for user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        [Interactions.StaffOnly(Interactions.StaffTier.ChickenTender)]
        public async Task MeritsForUser([Summary("targetuser", "targetUser")] SocketGuildUser targetUser) {
            try {
                var user = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);


                var merits = await Db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    await Context.Interaction.RespondAsyncGettingMessage($"There are no merits for {targetUser.Mention}");
                    return;
                }

                await RespondWithMeritList(targetUser.Mention, merits, ephemeral: false);
            } catch(Exception e) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("merits", "List your merits")]
        [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
        public async Task Merits() {
            try {
                var socketUser = Context.User;
                var user = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);

                var merits = await Db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    await Context.Interaction.RespondAsyncGettingMessage($"There are no merits for {socketUser.Mention}", ephemeral: true);
                    return;
                }

                await RespondWithMeritList(socketUser.Mention, merits, ephemeral: true);
            } catch(Exception e) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedExceptionFrame(e));
            }
        }
    }
}
