using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class DemeritCommands {
        public static async Task<string> GetDemerits(Guid dbuserid, ApplicationDbContext db) {
            var demerits = await db.Demerit.AsQueryable().Where(x => x.UserId == dbuserid && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).ToListAsync();
            if(demerits.Count == 0) {
                string msg;
                msg = $"There are no recent demerits";
                return msg;
            }

            var demeritDesc = string.Join("\n", demerits.Select(x => {
                var monthAgo = DateTimeOffset.UtcNow.AddMonths(-1);
                var timeLeft = monthAgo - x.When;
                return $"Expires in {timeLeft.Humanize(2)} for reason: {x.Reason}";
            }));

            return demeritDesc;
        }
    }

    public class DemeritModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient client) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordSocketClient _client = client;

        [SlashCommand("adddemerit", "Add demerit to user")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [EGG9000.Bot.Interactions.StaffOnly(EGG9000.Bot.Interactions.StaffTier.Admin)]
        public async Task AddDemerit([Summary("user")] SocketGuildUser user, [Summary("reason")] string reason, [Summary("hidden")] bool hidden = false) {
            await Context.Interaction.DeferAsync(ephemeral: hidden);
            try {
                var admin = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
                var dbuser = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

                var demerit = new Demerit {
                    When = DateTimeOffset.UtcNow,
                    AdminUserId = admin.Id,
                    UserId = dbuser.Id,
                    Id = Guid.NewGuid(),
                    Reason = reason
                };
                Db.Demerit.Add(demerit);
                await Db.SaveChangesAsync();

                var count = await Db.Demerit.AsQueryable().Where(x => x.UserId == dbuser.Id && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).CountAsync();

                var message = $"Demerit added to {user.Mention} for the reason: {demerit.Reason}\nThey currently have {count} demerits";
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = message; });
                if(hidden) {
                    await Context.Channel.SendMessageAsync(message);
                }

                var dbguild = await Db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId);
                var response = await ChannelHelper.DetermineAndSend(_client, dbguild, GuildChannelType.DemeritLogChannel, new() { Text = count >= 3 ? $"**{message}**" : message });
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("removedemerit", "Remove latest demerit from user")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [EGG9000.Bot.Interactions.StaffOnly(EGG9000.Bot.Interactions.StaffTier.Admin)]
        public async Task RemoveDemerit([Summary("user")] SocketGuildUser user) {
            await Context.Interaction.DeferAsync();
            try {
                var admin = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
                var dbuser = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);


                var demerit = await Db.Demerit.AsQueryable().Where(x => x.UserId == dbuser.Id && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).OrderByDescending(x => x.When).FirstOrDefaultAsync();
                if(demerit == null) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"There are no recent demerits for {user.Mention}");
                    return;
                }
                Db.Remove(demerit);
                await Db.SaveChangesAsync();

                var count = await Db.Demerit.AsQueryable().Where(x => x.UserId == dbuser.Id && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).CountAsync();

                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"Demerit removed for {user.Mention}, they currently have {count} demerits");
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("demerits", "List your demerits")]
        [EnabledInDm(true)]
        public async Task Demerits() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            try {
                var socketUser = Context.User;
                var user = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);

                var demerits = await Db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).ToListAsync();
                if(demerits.Count == 0) {
                    string msg;
                    var msgs = new List<string> {
                            "How does a demerit sound for asking me that which you should already know",
                            "I really should give you a demerit so you can know what it feels like",
                            "No demerits, maybe I'll give you one just for fun"
                        };
                    msg = msgs.Skip(new Random().Next(0, msgs.Count)).Take(1).First();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = msg);
                    return;
                }

                var demeritDesc = string.Join("\n", demerits.Select(x => {
                    var monthAgo = DateTimeOffset.UtcNow.AddMonths(-1);
                    var timeLeft = monthAgo - x.When;
                    return $"Expires in {timeLeft.Humanize(2)} for reason: {x.Reason}";
                }));

                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"Demerit info for {socketUser.Mention}\n{demeritDesc}");
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("demeritsforuser", "List demerits for user")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [EGG9000.Bot.Interactions.StaffOnly(EGG9000.Bot.Interactions.StaffTier.Admin)]
        public async Task DemeritsForUser([Summary("user")] SocketGuildUser user, [Summary("hidden")] bool hidden = false) {
            await Context.Interaction.DeferAsync(ephemeral: hidden);
            try {
                var dbuser = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

                var demeritDesc = await DemeritCommands.GetDemerits(dbuser.Id, Db);

                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"Demerit info for {user.Mention}\n{demeritDesc}");
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("nodemerit", "Stops user from getting demerit in co-op")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        [EGG9000.Bot.Interactions.StaffOnly(EGG9000.Bot.Interactions.StaffTier.Admin)]
        public async Task NoDemerit([Summary("user")] SocketGuildUser user) {
            await Context.Interaction.DeferAsync();
            List<UserCoopXref> xref;
            var targetCoop = await Db.Coops.AsQueryable().FirstAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);

            if(targetCoop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedError("This command can only be used in a co-op channel"));
                return;
            }

            xref = await Db.UserCoopXrefs.AsQueryable().Where(xref => xref.User.DiscordId == user.Id && xref.CoopId == targetCoop.Id).ToListAsync();

            if(xref.Count == 0) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedError("Unable to find user reference in co-op"));
                return;
            }

            xref.ForEach(x => x.NoDemerit = true);
            await Db.SaveChangesAsyncRetry(2);
            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"{user.Mention} will not receive automated demerits in this co-op.");
        }
    }
}
