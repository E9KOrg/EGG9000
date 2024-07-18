using Discord.WebSocket;
using Discord;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.FAQHelper;
using System.Web;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Bugsnag;

namespace EGG9000.Bot.Commands {
    public static class FAQCommandSlash {

        [SlashCommand(Description = "Nuke all FAQs from the server", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task NukeFAQ(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);

            if(userRunning is null) {
                await command.RespondAsync("Could not determine who you are ... (report this)", ephemeral: true);
            }

            var guildObj = db.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? db.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId);
            var socketGuild = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? client.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId);

            guildObj.FAQTopics = [];
            await db.SaveChangesAsync();
            db._cache.InvalidateFAQTopics(guildObj);

            await command.RespondAsync("Nuked and invalidated.");
        }

        [SlashCommand(Description = "Lookup brief explanations of key topics", AllowInDMs = true)]
        public static async Task FAQ(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam(Description = "Topic or keyword")] string query, [SlashParam(Description = "Show in channel", Required = false)] bool showInChannel = true) {
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);

            if(userRunning is null) {
                await command.RespondAsync("Could not determine who you are ... (report this)", ephemeral: true);
            }

            var guildObj = db.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? db.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId);
            var socketGuild = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? client.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId); ;

            if(guildObj is null || socketGuild is null) {
                await command.RespondAsync("Could not determine which server you are a part of ... (report this)", ephemeral: true);
            }

            var faqTopics = await db.QueryFAQTopicsAsync(guildObj, query);
            if(faqTopics is null || faqTopics.Count == 0) {
                await command.RespondAsync(content: $"Could not find any faq topics for the term `{query}`", ephemeral: true);
                return;
            }

            faqTopics = faqTopics.Where(f => !f.StaffOnly).ToList();

            if(faqTopics.Any()) {
                var builder = FAQEmbedBuilder(guildObj.Id.ToString(), faqTopics, faqTopics.First());
                await command.RespondAsync(components: builder.ComponentBuilder?.Build(), embed: builder.EmbedBuilder.Build(), ephemeral: !showInChannel);
            } else {
                await command.RespondAsync(content: $"Could not find any faq topics for the term `{query}`", ephemeral: true);
            }
        }

        [SlashCommand(Description = "Lookup brief explanations of key topics/templates", AllowInDMs = true, AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task FAQ(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, ILogger logger, [SlashParam(Description = "Topic or keyword")] string query, [SlashParam(Description = "Show in channel", Required = false)] bool showInChannel = true) {
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(userRunning is null) {
                await command.RespondAsync("Could not determine who you are ... (report this)", ephemeral: true);
            }

            var guildObj = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId) ?? db.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId);
            var socketGuild = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? client.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId); ;

            if(guildObj is null || socketGuild is null) {
                await command.RespondAsync("Could not determine which server you are a part of ... (report this)", ephemeral: true);
            }

            var faqTopics = await db.QueryFAQTopicsAsync(guildObj, query);

            if(faqTopics.Any()) {
                var builder = FAQEmbedBuilder(guildObj.Id.ToString(), faqTopics, faqTopics.First());
                await command.RespondAsync(content: $"faqTopics.Count: {faqTopics.Count}", components: builder.ComponentBuilder?.Build(), embed: builder.EmbedBuilder.Build(), ephemeral: faqTopics.Any(f => f.StaffOnly) || !showInChannel);
            } else {
                await command.RespondAsync(content: $"Could not find any faq topics for the term `{query}`", ephemeral: true);
            }
        }

        [ComponentCommand]
        public static async Task LoadFAQ(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var guildId = data.Split(",")[1];
            var guildObj = await db.Guilds.FirstOrDefaultAsync(g => g.Id.ToString() == guildId);
            var faqTopics = await db.GetFAQTopicsAsync(guildObj);
            var currentItem = faqTopics.FirstOrDefault(f => f.Name == data.Split(",")[0]);
            var items = data.Split(",")[2].Split("|").ToList().Select(item => faqTopics.FirstOrDefault(f => f.Name == item)).ToList();
            var builder = FAQEmbedBuilder(guildId, items, currentItem);
            await component.UpdateAsync(x => { x.Components = builder.ComponentBuilder?.Build(); x.Embed = builder.EmbedBuilder.Build(); });
        }

        public static FAQBuilder FAQEmbedBuilder(string guildId, List<FAQTopic> items, FAQTopic currentItem) {
            var builder = new FAQBuilder() {
                ComponentBuilder = null
            };

            var componentBuilder = new ComponentBuilder();
            var buttonCount = 0;

            var embedBuilder = new EmbedBuilder().WithAuthor(
                new EmbedAuthorBuilder()
                    .WithName($"{currentItem.Name} (More Information)")
                    .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.png"))
                .WithColor(currentItem.EmbedColor);
            embedBuilder.AddField("Explanation", currentItem.Explanation);

            var indexInList = items.IndexOf(currentItem);
            var itemsInList = items.Count;

            if(indexInList > 0 && itemsInList > 1 && items[indexInList - 1] is not null) {
                componentBuilder.WithButton($"← {items[indexInList - 1].Name}", $"LoadFAQ:{items[indexInList - 1].Name},{guildId},{string.Join("|", items.Select(i => i.Name))}"); buttonCount++;
            }
            if(indexInList < items.Count - 1 && items[indexInList + 1] is not null) {
                componentBuilder.WithButton($"{items[indexInList + 1].Name} →", $"LoadFAQ:{items[indexInList + 1].Name},{guildId},{string.Join("|", items.Select(i => i.Name))}"); buttonCount++;
            }
            if(buttonCount > 0) builder.ComponentBuilder = componentBuilder;

            builder.EmbedBuilder = embedBuilder;
            return builder;
        }
    }
}
