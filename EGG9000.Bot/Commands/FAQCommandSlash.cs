using Discord.WebSocket;
using Discord;
using Discord.Interactions;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.FAQHelper;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Helpers;
using System;
using EGG9000.Bot.Helpers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Commands {
    public static class FAQCommandSlash {

        public static async Task _faq(SocketInteraction command, ApplicationDbContext db, DiscordHostedService _client, string query, bool withStaffPerms, string respondTo, ILogger _logger) {
            _logger.LogInformation($"Running FAQ for {query}");
            // Because this can be run in DMs, need a fallback
            var inDms = command.GuildId == null;
            var isEphemeral = !inDms;

            await command.DeferAsync(ephemeral: isEphemeral);

            var respondToMessage = ulong.MaxValue;
            if(!string.IsNullOrEmpty(respondTo)) {
                if(!ulong.TryParse(respondTo, out var messageId)) {
                    await command.ModifyOriginalResponseAsync(x => { x.Embed = EmbedError($"Could not parse the ID `{respondTo}`."); });
                    return;
                }
                var message = await command.Channel.GetMessageAsync(messageId);
                if(message == null) {
                    await command.ModifyOriginalResponseAsync(x => { x.Embed = EmbedError($"Could not find a message with the ID `{respondTo}`."); });
                    return;
                }
                respondToMessage = message.Id;
            }
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(userRunning is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Embed = EmbedError("Could not determine who you are."); });
                return;
            }
            var guildId = inDms ? userRunning.GuildId : command.GuildId;
            var guildObj = db.Guilds.FirstOrDefault(g => g.Id == guildId || g.OverflowServersJson.Contains(guildId.ToString()));
            var socketGuild = _client.Guilds.FirstOrDefault(cg => cg.Id == guildObj.Id);

            if(guildObj is null || socketGuild is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Embed = EmbedError("Could not determine which server you are a part of."); });
                return;
            }

            if(!guildObj.FAQTopicsEnabled) {
                await command.ModifyOriginalResponseAsync(x => { x.Embed = EmbedError("Your server does not have FAQ Topics enabled."); });
                return;
            }
            var runningUserDiscord = socketGuild.GetUser(userRunning.DiscordId);
            var hasStaffPerms = runningUserDiscord.GuildPermissions.Has(GuildPermission.ModerateMembers);

            var faqTopics = await db.QueryFAQTopicsAsync(guildObj, hasStaffPerms && withStaffPerms, query);
            if(faqTopics.Any()) {
                _logger.LogInformation($"Found FAQ Topic for {query}");
                await command.ModifyOriginalResponseAsync(x => x.Content = "Found, please wait...");
                var builder = await FAQEmbedBuilder(_client, guildObj.Id, withStaffPerms, query, isEphemeral, respondToMessage, faqTopics, faqTopics.First());
                await command.ModifyOriginalResponseAsync(x => { x.Embed = builder.EmbedBuilder.Build(); x.Components = builder.ComponentBuilder?.Build() ?? null; x.Content = null; });
            } else {
                _logger.LogInformation($"No FAQ Topic for {query}");
                await command.ModifyOriginalResponseAsync(x => { x.Embed = MakeCustomEmbed(EmbedHelpers.EmbedType.Alert, "No Results", $"Could not find any FAQ topics for the term `{query}`"); });
            }
            _logger.LogInformation($"FAQ for {query} complete");
        }

        public static async Task<FAQBuilder> FAQEmbedBuilder(DiscordHostedService _client, ulong guildId, bool withStaffPerms, string query, bool isEphemeral, ulong? respondTo, List<FAQTopic> faqTopics, FAQTopic currentItem, IGuildUser poster = null) {
            var builder = new FAQBuilder() {
                ComponentBuilder = null
            };

            var embedBuilder = new EmbedBuilder().WithAuthor(
                new EmbedAuthorBuilder()
                    .WithName($"{currentItem.Name}")
                    .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.png"))
                .WithColor(currentItem.EmbedColor);

            if(!string.IsNullOrEmpty(currentItem.ImageUrl)) {
                embedBuilder.WithImageUrl(currentItem.ImageUrl);
            }

            var informationText = await MessageFormatter.FormatAsync(currentItem.Explanation, _client, guildId);

            if (informationText.Length >= 1024) {
                informationText = string.Concat(informationText.AsSpan(0, 950), "...\n\n**_(Topic was cut-off due to Discord's `1024` character limit)_**");
            }
            embedBuilder.AddField("Information", informationText);

            if(poster != null) {

                var userIconUrl = poster.GetGuildAvatarUrl();
                if(string.IsNullOrEmpty(userIconUrl)) {
                    userIconUrl = poster.GetAvatarUrl();
                }
                if(string.IsNullOrEmpty(userIconUrl)) {
                    userIconUrl = poster.GetDefaultAvatarUrl();
                }

                embedBuilder.WithFooter(
                    new EmbedFooterBuilder()
                        .WithText($"Posted by {poster.Nickname}")
                        .WithIconUrl(userIconUrl)
                );
            }


            var indexInList = faqTopics.IndexOf(currentItem);

            var hasButtons = false;
            var componentBuilder = new ComponentBuilder();
            if(indexInList > 0 && faqTopics.Count > 1 && faqTopics[indexInList - 1] is not null) {
                componentBuilder.WithFAQButton(FAQButtonType.Previous, guildId, withStaffPerms, query, isEphemeral, faqTopics[indexInList - 1], indexInList - 1, respondTo);
                hasButtons = true;
            }
            if(indexInList < faqTopics.Count - 1 && faqTopics[indexInList + 1] is not null) {
                componentBuilder.WithFAQButton(FAQButtonType.Next, guildId, withStaffPerms, query, isEphemeral, faqTopics[indexInList + 1], indexInList + 1, respondTo);
                hasButtons = true;
            }
            if(isEphemeral) {
                componentBuilder.WithFAQButton(FAQButtonType.Post, guildId, withStaffPerms, query, isEphemeral, faqTopics[indexInList], indexInList, respondTo);
                hasButtons = true;
            }

            if(hasButtons) builder.ComponentBuilder = componentBuilder;
            builder.EmbedBuilder = embedBuilder;
            return builder;
        }

        private enum FAQButtonType {
            Previous,
            Next,
            Post,
        }

        private static ComponentBuilder WithFAQButton(this ComponentBuilder builder, FAQButtonType type, ulong guildId, bool withStaffPerms, string query, bool isEphemeral, FAQTopic faqTopic, int targetIndex, ulong? respondTo) {
            if(type == FAQButtonType.Post) {
                return builder.WithButton(
                    $"Post to Channel",
                    $"PostFAQ:{guildId},{withStaffPerms},{isEphemeral},{query},{targetIndex},{respondTo ?? ulong.MaxValue}",
                    ButtonStyle.Success,
                    row: 1
                );
            } else {
                return builder.WithButton(
                    $"{(type == FAQButtonType.Previous ? "← " : "")}{faqTopic.Name}{(type == FAQButtonType.Next ? " →" : "")}",
                    $"LoadFAQ:{guildId},{withStaffPerms},{isEphemeral},{query},{targetIndex},{respondTo ?? ulong.MaxValue}"
                );
            }
        }
    }

    public class FaqModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client, ILogger<FaqModule> logger) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;
        private readonly ILogger<FaqModule> _logger = logger;

        [SlashCommand("faq", "Lookup brief explanations of key topics")]
        [EnabledInDm(true)]
        public Task FAQ([Summary("query", "Topic or keyword")][MaxLength(MAX_KEYWORD_LENGTH)] string query) {
            return FAQCommandSlash._faq(Context.Interaction, Db, _client, query, false, "", _logger);
        }

        [ComponentInteraction("LoadFAQ:*", ignoreGroupNames: true)]
        public async Task LoadFAQ(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var splits = data.Split(",");

            var guildId = ulong.Parse(splits[0]);
            var withStaffPerms = bool.Parse(splits[1]);
            var isEphemeral = bool.Parse(splits[2]);
            var query = splits[3];
            var targetIndex = int.Parse(splits[4]);
            var respondTo = ulong.Parse(splits[5]);

            var guildObj = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == guildId);
            var socketGuild = _client.Guilds.FirstOrDefault(cg => cg.Id == guildObj.Id);
            var runningUserDiscord = socketGuild.GetUser(component.User.Id);
            var hasStaffPerms = runningUserDiscord.GuildPermissions.Has(GuildPermission.ModerateMembers);

            var faqTopics = await Db.QueryFAQTopicsAsync(guildObj, hasStaffPerms && withStaffPerms, query);
            if(faqTopics.Count > 0 && faqTopics[targetIndex] != null) {
                var targetItem = faqTopics[targetIndex];
                var builder = await FAQCommandSlash.FAQEmbedBuilder(_client, guildId, withStaffPerms, query, isEphemeral, respondTo, faqTopics, targetItem);
                await component.UpdateAsync(x => { x.Components = builder.ComponentBuilder?.Build(); x.Embed = builder.EmbedBuilder.Build(); });
            } else {
                var faqCommand = await _client.GetSlashCommandStringAsync(socketGuild, "FAQ");
                await component.RespondAsync(embed: EmbedError($"Could not find an FAQ topic at this index. Try running {faqCommand} again."), ephemeral: true);
            }
        }

        [ComponentInteraction("PostFAQ:*", ignoreGroupNames: true)]
        public async Task PostFAQ(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            if(!component.HasResponded) await component.DeferAsync();
            var splits = data.Split(",");

            var guildId = ulong.Parse(splits[0]);
            var withStaffPerms = bool.Parse(splits[1]);
            var isEphemeral = bool.Parse(splits[2]);
            var query = splits[3];
            var targetIndex = int.Parse(splits[4]);
            var respondTo = ulong.Parse(splits[5]);

            if(!isEphemeral) {
                await component.ModifyOriginalResponseAsync(x => x.Embed = EmbedError("Cannot re-post from a non-ephemeral message.\n\nHow did you do this?"));
                return;
            }

            var guildObj = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == guildId);
            var socketGuild = _client.Guilds.FirstOrDefault(cg => cg.Id == guildObj.Id);
            var userRunning = Db.DBUsers.FirstOrDefault(x => x.DiscordId == component.User.Id);
            var runningUserDiscord = socketGuild.GetUser(component.User.Id);
            var hasStaffPerms = runningUserDiscord.GuildPermissions.Has(GuildPermission.ModerateMembers);
            var lastPostTime = userRunning.LastFAQPosted ?? DateTimeOffset.MinValue;
            var isOnCooldown = DateTimeOffset.UtcNow - (userRunning.LastFAQPosted ?? DateTimeOffset.MinValue) < TimeSpan.FromMinutes(guildObj.FAQTopicCooldownMinutes);

            if(isOnCooldown && !hasStaffPerms) {
                await component.ModifyOriginalResponseAsync(x => x.Embed = MakeCustomEmbed(
                        EmbedHelpers.EmbedType.Alert,
                        "Post Cooldown",
                        $"You are on cooldown, and will be able to post again {DiscordHelpers.TimeStamper(lastPostTime.AddMinutes(guildObj.FAQTopicCooldownMinutes), DiscordHelpers.DiscordTimestampFormat.Relative)}."
                    ));
                return;
            }

            var faqTopics = await Db.QueryFAQTopicsAsync(guildObj, hasStaffPerms && withStaffPerms, query);
            if(faqTopics.Count == 0 || faqTopics[targetIndex] == null) {
                await component.ModifyOriginalResponseAsync(x => x.Embed = EmbedError("Could not find an FAQ topic at this index. Try running {faqCommand} again."));
                return;
            }

            var embed = (await FAQCommandSlash.FAQEmbedBuilder(_client, guildId, withStaffPerms, query, isEphemeral, respondTo, faqTopics, faqTopics[targetIndex], runningUserDiscord)).EmbedBuilder.Build();
            var messageRef = respondTo != ulong.MaxValue ? new MessageReference(respondTo, failIfNotExists: false) : null;
            await component.Channel.SendMessageAsync(embed: embed, messageReference: messageRef);
            await component.ModifyOriginalResponseAsync(x => { x.Embed = EmbedSuccess("Posted."); x.Components = null; });
        }
    }

    public partial class AdminModule {
        [Discord.Interactions.SlashCommand("faq", "Lookup brief explanations of key topics/templates")]
        public Task FAQ([Discord.Interactions.Summary("query", "Topic or keyword")][Discord.Interactions.MaxLength(MAX_KEYWORD_LENGTH)] string query, [Discord.Interactions.Summary("respondto", "Which message to respond to")] string respondto = "") {
            return FAQCommandSlash._faq(Context.Interaction, Db, client, query, true, respondto, _logger);
        }
    }
}
