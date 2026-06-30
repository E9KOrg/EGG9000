using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class MeritCommands {
        [SlashCommand(Description = "Add merit to user(s)", AdminOnly = StaffOnlyLevel.ChickenTender, ParentCommand = "a")]
        public static async Task AddMerit(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client,
            [SlashParam(Description = "Merit Reason")] string reason,
            [SlashParam] SocketGuildUser[] users
            ) {
            await command.RespondAsync("Adding Merits");
            var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);


            foreach(var mention in users) {
                await CreateMerit(reason, db, _client, mention, admin.Id, command);
            }
            await command.DeleteResponseFix();
        }
        public static async Task CreateMerit(string reason, ApplicationDbContext db, DiscordSocketClient _client, SocketUser target, Guid? adminid, FauxCommand command = null, Guild guild = null) {

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

        [SlashCommand(Description = "Remove merit from user", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task RemoveMerit(FauxCommand command, [SlashParam] SocketGuildUser user, ApplicationDbContext db) {
            try {
                var admin = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
                var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);


                var merit = await db.Merit.AsQueryable().Where(x => x.UserId == dbuser.Id).OrderByDescending(x => x.When).FirstOrDefaultAsync();
                if(merit == null) {
                    await command.RespondAsync($"There are no recent merits for {user.Mention}");
                    return;
                }
                db.Remove(merit);
                await db.SaveChangesAsync();

                var count = await db.Merit.AsQueryable().Where(x => x.UserId == dbuser.Id).CountAsync();

                await command.RespondAsync($"Merit removed for {user.Mention}, they currently have {count} merits");
            } catch(Exception e) {
                await command.RespondAsync(content: "", embed: EmbedExceptionFrame(e));
            }
        }

        [SlashCommand(Description = "List merits for user", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task MeritsForUser(FauxCommand command, [SlashParam] SocketGuildUser targetUser, ApplicationDbContext db) {
            try {
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);

                var merits = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    await command.RespondAsync($"There are no merits for {targetUser.Mention}");
                    return;
                }

                var (embed, components) = BuildMeritPage(merits, 0, command.User.Id, targetUser.Id, targetUser.Mention);
                await command.RespondAsync(embed: embed, components: components);
            } catch(Exception e) {
                await command.RespondAsync(content: "", embed: EmbedExceptionFrame(e));
            }
        }

        [SlashCommand(Description = "List your merits", AllowInDMs = true)]
        public static async Task Merits(FauxCommand command, ApplicationDbContext db) {
            try {
                var socketUser = command.User;
                var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);

                var merits = await db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
                if(merits.Count == 0) {
                    await command.RespondAsync($"There are no merits for {socketUser.Mention}");
                    return;
                }

                var (embed, components) = BuildMeritPage(merits, 0, socketUser.Id, socketUser.Id, socketUser.Mention);
                await command.RespondAsync(embed: embed, components: components, ephemeral: true);
            } catch(Exception e) {
                await command.RespondAsync(content: "", embed: EmbedExceptionFrame(e));
            }
        }

        [ComponentCommand]
        public static async Task LoadMerits(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var parts = data.Split(",");
            if(parts.Length < 3) return;
            var invokerDiscordId = ulong.Parse(parts[0]);
            var targetDiscordId = ulong.Parse(parts[1]);
            var page = int.Parse(parts[2]);

            if(component.User.Id != invokerDiscordId) {
                await component.RespondAsync("These merits aren't yours to page through.", ephemeral: true);
                return;
            }

            var targetUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetDiscordId);
            if(targetUser is null) {
                await component.RespondAsync("Could not find user.", ephemeral: true);
                return;
            }

            var merits = await db.Merit.AsQueryable().Where(x => x.UserId == targetUser.Id).OrderBy(x => x.When).ToListAsync();
            if(merits.Count == 0) {
                await component.RespondAsync("There are no merits to display.", ephemeral: true);
                return;
            }
            var targetMention = $"<@{targetDiscordId}>";
            var (embed, messageComponents) = BuildMeritPage(merits, page, invokerDiscordId, targetDiscordId, targetMention);

            await component.UpdateAsync(x => {
                x.Content = "";
                x.Embed = embed;
                x.Components = messageComponents;
            });
        }

#nullable enable
        private static (Embed embed, MessageComponent? components) BuildMeritPage(
            List<Merit> merits, int page, ulong invokerDiscordId, ulong targetDiscordId, string targetMention) {

            var pages = new List<(int start, int end)>();
            var currentStart = 0;
            while(currentStart < merits.Count) {
                var charCount = 0;
                var count = 0;
                var end = currentStart;
                while(end < merits.Count) {
                    var line = $"{end + 1}. {merits[end].Reason}\n";
                    if(count > 0 && (charCount + line.Length > 1000 || count >= 10)) break;
                    charCount += line.Length;
                    count++;
                    end++;
                }
                pages.Add((currentStart, end));
                currentStart = end;
            }

            page = Math.Clamp(page, 0, pages.Count - 1);

            var (start, pageEnd) = pages[page];
            var desc = string.Join("\n", merits[start..pageEnd].Select((m, i) => $"{start + i + 1}. {m.Reason}"));

            var embedBuilder = new EmbedBuilder()
                .WithTitle("Merit History")
                .WithDescription($"{targetMention}\n\n{desc}")
                .WithColor(Color.Gold);
            if(pages.Count > 1)
                embedBuilder.WithFooter($"Page {page + 1} of {pages.Count}");

            MessageComponent? messageComponents = null;
            if(pages.Count > 1) {
                var cb = new ComponentBuilder()
                    .WithButton("< Prev", $"LoadMerits:{invokerDiscordId},{targetDiscordId},{Math.Max(0, page - 1)}", ButtonStyle.Secondary, disabled: page == 0)
                    .WithButton("Next >", $"LoadMerits:{invokerDiscordId},{targetDiscordId},{Math.Min(pages.Count - 1, page + 1)}", ButtonStyle.Secondary, disabled: page == pages.Count - 1);
                messageComponents = cb.Build();
            }

            return (embedBuilder.Build(), messageComponents);
        }
#nullable restore
    }
}

