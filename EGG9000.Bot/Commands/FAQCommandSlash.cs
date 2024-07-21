using Discord.WebSocket;
using Discord;
using EGG9000.Common.Commands;
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
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace EGG9000.Bot.Commands {
    public static class FAQCommandSlash {

        [SlashCommand(Description = "Lookup brief explanations of key topics", AllowInDMs = true)]
        public static Task FAQ(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam(Description = "Topic or keyword", StringMaxLength = MAX_KEYWORD_LENGTH)] string query) {
            return _faq(command, db, _client, query, false, false);
        }

        [SlashCommand(Description = "Lookup brief explanations of key topics/templates", AllowInDMs = true, AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task FAQ(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam(Description = "Topic or keyword", StringMaxLength = MAX_KEYWORD_LENGTH)] string query, [SlashParam(Description = "Show in channel", Required = false)] bool showInChannel = false) {
            return _faq(command, db, _client, query, showInChannel, true);
        }

        public static async Task _faq(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, string query, bool showInChannel, bool withStaffPerms) {
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(userRunning is null) {
                await command.RespondAsync(embed: EmbedError("Could not determine who you are."), ephemeral: true);
                return;
            }

            // Because this can be run in DMs, need a fallback
            var inDms = command.GuildId == null;
            var guildId = inDms ? userRunning.GuildId : command.GuildId;
            var guildObj = db.Guilds.FirstOrDefault(g => g.Id == guildId || g.OverflowServersJson.Contains(guildId.ToString()));
            var socketGuild = _client.Guilds.FirstOrDefault(cg => cg.Id == guildObj.Id);

            var runningUserDiscord = socketGuild.GetUser(userRunning.DiscordId);
            var hasStaffPerms = runningUserDiscord.GuildPermissions.Has(GuildPermission.ModerateMembers);

            if(guildObj is null || socketGuild is null) {
                await command.RespondAsync(embed: EmbedError("Could not determine which server you are a part of."), ephemeral: true);
            }

            var faqTopics = await db.QueryFAQTopicsAsync(guildObj, hasStaffPerms && withStaffPerms, query);
            if(faqTopics.Any()) {
                var builder = FAQEmbedBuilder(guildObj.Id, userRunning.DiscordId, withStaffPerms, query, faqTopics, faqTopics.First());
                await command.RespondAsync(embed: builder.EmbedBuilder.Build(), components: builder.ComponentBuilder?.Build() ?? null, ephemeral: !inDms && !showInChannel);
            } else {
                await command.RespondAsync(embed: EmbedCustom(EmbedHelpers.EmbedType.Alert, "No Results", $"Could not find any FAQ topics for the term `{query}`"), ephemeral: true);
            }
        }

        [ComponentCommand]
        public static async Task LoadFAQ(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordHostedService _client) {
            var splits = data.Split(",");

            var guildId = ulong.Parse(splits[0]);
            var userDiscordId = ulong.Parse(splits[1]);
            var withStaffPerms = bool.Parse(splits[2]);
            var query = splits[3];
            var targetIndex = int.Parse(splits[4]);

            var guildObj = await db.Guilds.FirstOrDefaultAsync(g => g.Id == guildId);
            var socketGuild = _client.Guilds.FirstOrDefault(cg => cg.Id == guildObj.Id);
            var runningUserDiscord = socketGuild.GetUser(userDiscordId);
            var hasStaffPerms = runningUserDiscord.GuildPermissions.Has(GuildPermission.ModerateMembers);

            var faqTopics = await db.QueryFAQTopicsAsync(guildObj, hasStaffPerms && withStaffPerms, query);
            if(faqTopics.Count > 0 && faqTopics[targetIndex] != null) {
                var targetItem = faqTopics[targetIndex];
                var builder = FAQEmbedBuilder(guildId, userDiscordId, withStaffPerms, query, faqTopics, targetItem);
                await component.UpdateAsync(x => { x.Components = builder.ComponentBuilder?.Build(); x.Embed = builder.EmbedBuilder.Build(); });
            } else {
                var slashCommands = (await socketGuild.GetApplicationCommandsAsync()).ToList().Where(c => c.Type == ApplicationCommandType.Slash).ToList();
                var faqCommand = $"</faq:{slashCommands.FirstOrDefault(c => c.Name.ToLower() == "faq")?.Id ?? 0}>";
                // TODO: Before go live make this ephemeral
                await component.RespondAsync(embed: EmbedError($"Could not find an FAQ topic at this index. Try running {faqCommand} again."), ephemeral: false);
            }
        }

        public static FAQBuilder FAQEmbedBuilder(ulong guildId, ulong discordUserId, bool withStaffPerms, string query, List<FAQTopic> faqTopics, FAQTopic currentItem) {
            var builder = new FAQBuilder() {
                ComponentBuilder = null
            };

            var embedBuilder = new EmbedBuilder().WithAuthor(
                new EmbedAuthorBuilder()
                    .WithName($"{currentItem.Name}")
                    .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.png"))
                .WithColor(currentItem.EmbedColor);
            embedBuilder.AddField("Information", currentItem.Explanation);

            var indexInList = faqTopics.IndexOf(currentItem);

            var hasButtons = false;
            var componentBuilder = new ComponentBuilder();
            if(indexInList > 0 && faqTopics.Count > 1 && faqTopics[indexInList - 1] is not null) {
                componentBuilder.WithFAQButton(FAQButtonType.Previous, guildId, discordUserId, withStaffPerms, query, faqTopics[indexInList - 1], indexInList - 1);
                hasButtons = true;
            }
            if(indexInList < faqTopics.Count - 1 && faqTopics[indexInList + 1] is not null) {
                componentBuilder.WithFAQButton(FAQButtonType.Next, guildId, discordUserId, withStaffPerms, query, faqTopics[indexInList + 1], indexInList + 1);
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

        private static ComponentBuilder WithFAQButton(this ComponentBuilder builder, FAQButtonType type, ulong guildId, ulong discordUserId, bool withStaffPerms, string query, FAQTopic faqTopic, int targetIndex) {
            if(type != FAQButtonType.Post) {
                return builder.WithButton(
                    $"{(type == FAQButtonType.Previous ? "← " : "")}{faqTopic.Name}{(type == FAQButtonType.Next ? " →" : "")}",
                    $"LoadFAQ:{guildId},{discordUserId},{withStaffPerms},{query},{targetIndex}"
                );
            } else {
                return null;
            }
        }
    }
}
