using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Helpers.Discord.Paging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class MeritListPager(IReadOnlyList<string> lines, int page, string mentionText, ulong invokerId, ulong targetDiscordId) : TextListPager(lines, page) {
        protected override string Title => "Merits";
        protected override string Preamble => $"Merits for {mentionText}";
        protected override string CustomIdPrefix => "MeritsPage";
        protected override string KeySuffix => $"{invokerId},{targetDiscordId}";

        public static (ulong InvokerId, ulong TargetDiscordId, int Page) ParseCustomId(string data) {
            var parts = data.Split(",");
            return (ulong.Parse(parts[0]), ulong.Parse(parts[1]), int.Parse(parts[2]));
        }
    }

    public class MeritModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient gateway) : Interactions.E9KModuleBase(dbFactory) {
        private static List<string> BuildMeritLines(List<Merit> merits) =>
            [.. merits.Select((m, i) => $"{i + 1}: {m.Reason}")];

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

                var pager = new MeritListPager(BuildMeritLines(merits), 0, targetUser.Mention, Context.User.Id, targetUser.Id);
                await pager.SendAsync(Context.Interaction);
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

                var pager = new MeritListPager(BuildMeritLines(merits), 0, socketUser.Mention, socketUser.Id, socketUser.Id);
                await pager.SendAsync(Context.Interaction, ephemeral: true);
            } catch(Exception e) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedExceptionFrame(e));
            }
        }

        [ComponentInteraction("MeritsPage:*", ignoreGroupNames: true)]
        public async Task MeritsPage(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var (invokerId, targetDiscordId, page) = MeritListPager.ParseCustomId(data);
            if(component.User.Id != invokerId) { await Pager.RejectNonInvokerAsync(component); return; }

            var user = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetDiscordId);
            if(user is null) return;
            var merits = await Db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            var pager = new MeritListPager(BuildMeritLines(merits), page, $"<@{targetDiscordId}>", invokerId, targetDiscordId);
            await pager.UpdateComponentAsync(component);
        }
    }
}
