using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Helpers.Discord.Paging;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class DemeritCommands {
        public static async Task<List<string>> BuildDemeritLines(Guid dbuserid, ApplicationDbContext db) {
            var demerits = await db.Demerit.AsQueryable().Where(x => x.UserId == dbuserid && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).ToListAsync();
            var monthAgo = DateTimeOffset.UtcNow.AddMonths(-1);
            return [.. demerits.Select(x => $"Expires in {(monthAgo - x.When).Humanize(2)} for reason: {x.Reason}")];
        }

        public static async Task<string> GetDemerits(Guid dbuserid, ApplicationDbContext db) {
            var lines = await BuildDemeritLines(dbuserid, db);
            return lines.Count == 0 ? "There are no recent demerits" : string.Join("\n", lines);
        }
    }

    public class DemeritListPager(IReadOnlyList<string> lines, int page, string mentionText, ulong invokerId, ulong targetDiscordId) : TextListPager(lines, page) {
        protected override string Title => "Demerits";
        protected override string Preamble => $"Demerits for {mentionText}";
        protected override string CustomIdPrefix => "DemeritsPage";
        protected override string KeySuffix => $"{invokerId},{targetDiscordId}";

        public static (ulong InvokerId, ulong TargetDiscordId, int Page) ParseCustomId(string data) {
            var parts = data.Split(",");
            return (ulong.Parse(parts[0]), ulong.Parse(parts[1]), int.Parse(parts[2]));
        }
    }

    public class DemeritModule(IDbContextFactory<ApplicationDbContext> dbFactory) : Interactions.E9KModuleBase(dbFactory) {
        [SlashCommand("demerits", "List your demerits")]
        [CommandContextType(Discord.InteractionContextType.Guild, Discord.InteractionContextType.BotDm)]
        public async Task Demerits() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            try {
                var socketUser = Context.User;
                var user = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);

                var lines = await DemeritCommands.BuildDemeritLines(user.Id, Db);
                if(lines.Count == 0) {
                    var msgs = new List<string> {
                            "How does a demerit sound for asking me that which you should already know",
                            "I really should give you a demerit so you can know what it feels like",
                            "No demerits, maybe I'll give you one just for fun"
                        };
                    var msg = msgs.Skip(new Random().Next(0, msgs.Count)).Take(1).First();
                    await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = msg);
                    return;
                }

                var pager = new DemeritListPager(lines, 0, socketUser.Mention, socketUser.Id, socketUser.Id);
                await pager.SendAsync(Context.Interaction, ephemeral: true);
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [ComponentInteraction("DemeritsPage:*", ignoreGroupNames: true)]
        public async Task DemeritsPage(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var (invokerId, targetDiscordId, page) = DemeritListPager.ParseCustomId(data);
            if(component.User.Id != invokerId) { await Pager.RejectNonInvokerAsync(component); return; }

            var targetUser = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetDiscordId);
            if(targetUser is null) return;
            var lines = await DemeritCommands.BuildDemeritLines(targetUser.Id, Db);
            var pager = new DemeritListPager(lines, page, $"<@{targetDiscordId}>", invokerId, targetDiscordId);
            await pager.UpdateComponentAsync(component);
        }
    }

    public partial class AdminGroupModule {
        [SlashCommand("adddemerit", "Add demerit to user")]
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
                var response = await ChannelHelper.DetermineAndSend(gateway, dbguild, GuildChannelType.DemeritLogChannel, new() { Text = count >= 3 ? $"**{message}**" : message });
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("removedemerit", "Remove latest demerit from user")]
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

        [SlashCommand("demeritsforuser", "List demerits for user")]
        public async Task DemeritsForUser([Summary("user")] SocketGuildUser user, [Summary("hidden")] bool hidden = false) {
            await Context.Interaction.DeferAsync(ephemeral: hidden);
            try {
                var dbuser = await Db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

                var lines = await DemeritCommands.BuildDemeritLines(dbuser.Id, Db);
                if(lines.Count == 0) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"There are no recent demerits for {user.Mention}");
                    return;
                }

                var pager = new DemeritListPager(lines, 0, user.Mention, Context.User.Id, user.Id);
                await pager.SendAsync(Context.Interaction, ephemeral: hidden);
            } catch(Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedExceptionFrame(e));
            }
        }

        [SlashCommand("nodemerit", "Stops user from getting demerit in co-op")]
        [ChannelContext(Coop = true)]
        public async Task NoDemerit([Summary("user")] SocketGuildUser user) {
            await Context.Interaction.DeferAsync();
            List<UserCoopXref> xref;

            xref = await Db.UserCoopXrefs.AsQueryable().Where(xref => xref.User.DiscordId == user.Id && xref.CoopId == CoopChannel.Id).ToListAsync();

            if(xref.Count == 0) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedError("Unable to find user reference in co-op"));
                return;
            }

            xref.ForEach(x => x.NoDemerit = true);
            var (saved, _) = await Db.SaveChangesAsyncRetry(2, logger: _logger);
            if(!saved) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedError($"Failed to save the no-demerit flag for {user.Mention}. Please try again."));
                return;
            }
            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"{user.Mention} will not receive automated demerits in this co-op.");
        }
    }
}
