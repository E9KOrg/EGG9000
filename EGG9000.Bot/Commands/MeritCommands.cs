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
    public static class MeritCommands {
        public static async Task CreateMerit(string reason, ApplicationDbContext db, DiscordSocketClient _client, SocketUser target, Guid? adminid, SocketInteraction command = null, Guild guild = null) {

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == target.Id);

            var merit = new Merit {
                When = DateTimeOffset.UtcNow,
                AdminUserId = adminid,
                UserId = user.Id,
                //Id = Guid.NewGuid(),
                Reason = reason
            };
            db.Merit.Add(merit);
            var count = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).CountAsync();
            count++;

            await db.SaveChangesAsync();

            if(command is not null || guild is not null) {
                var guildFind = guild;
                guildFind ??= db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);
                if(guildFind is not null) {
                    var socketGuild = _client.Guilds.First(x => x.Id == guildFind.Id);
                    if(socketGuild is not null) {
                        var response = await ChannelHelper.DetermineAndSend(_client, guildFind, GuildChannelType.MeritLogChannel, new() { Text = $"{target.Mention}: {merit.Reason} (Merits: {count})" });
                    }
                }
            }

            if(command != null) {
                await command.Channel.SendMessageAsync($"Merit Added {target.Mention}: {merit.Reason} (Merits: {count})");
            }

        }
    }

    public partial class AdminModule {
        // Long merit histories can exceed Discord's 2000-char message limit; fall back to a file
        // attachment instead of letting RespondAsyncGettingMessage throw.
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
            [Summary("users", "Mention one or more users (e.g. @a @b) or paste IDs")] string usersInput) {
            await Context.Interaction.RespondAsyncGettingMessage("Adding Merits");
            var users = Interactions.UserParams.ParseGuildUsers(usersInput, Context.Guild as SocketGuild, out var missing);
            if(users.Length == 0) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("No valid users parsed from input. Mention users like `@user1 @user2` or paste their IDs."); });
                return;
            }
            if(missing.Count > 0) {
                await Context.Interaction.FollowupAsync(embed: EmbedWarning($"Could not resolve: {string.Join(", ", missing.Select(id => $"`{id}`"))}"), ephemeral: true);
            }
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
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        [Interactions.StaffOnly(Interactions.StaffTier.FarmHand)]
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
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        [Interactions.StaffOnly(Interactions.StaffTier.FarmHand)]
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
    }

    public class MeritModule(IDbContextFactory<ApplicationDbContext> dbFactory) : Interactions.E9KModuleBase(dbFactory) {
        // Long merit histories can exceed Discord's 2000-char message limit; fall back to a file
        // attachment instead of letting RespondAsyncGettingMessage throw.
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
