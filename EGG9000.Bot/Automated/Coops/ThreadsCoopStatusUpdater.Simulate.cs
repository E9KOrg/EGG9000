using Discord;
using EGG9000.Common.Database.Entities;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.FixedWidthTable;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        public static List<string> BuildSyntheticRosterMessages(int participantCount, int maxUsers, bool worstCase) {
            var table = new List<List<FixedWidthCell>> {new () {
                new($"{participantCount}/{maxUsers}"),
                new("Discord", CellAlignment.Center),
                new("EB", CellAlignment.Center),
                new("Total", CellAlignment.Center),
                new("Rate", CellAlignment.Center),
                new("📈", CellAlignment.Center),
                new("%", CellAlignment.Center),
                new("🟡", CellAlignment.Center, true),
                new("⏲️", CellAlignment.Center, true),
                new("Silo"),
                new(""),
            }};

            for(var i = 0; i < participantCount; i++) {
                table.Add(BuildSyntheticRosterRow(i, worstCase));
            }

            return ChunkTableAtLimit(table, V1MessageContentCharBudget);
        }

        private static List<FixedWidthCell> BuildSyntheticRosterRow(int index, bool worstCase) {
            if(worstCase) {
                return [
                    new(Truncate($"✅XXXXPlayer{index}", 11)),
                    new(Truncate($"XXXXDiscord{index}", 11)),
                    new("999.99q", CellAlignment.Right),
                    new("999.99q", CellAlignment.Right),
                    new("999.99q/h", CellAlignment.Right),
                    new("999.99q", CellAlignment.Right),
                    new("999%", CellAlignment.Right),
                    new("99"),
                    new("23.9h"),
                    new("7d 23h"),
                    new(Truncate($"💤 Empty Silos {index} hours worst-case-suffix ⏱️", 24)),
                ];
            }

            return [
                new(Truncate($"P{index}", 11)),
                new(Truncate($"D{index}", 11)),
                new($"{index}.5q", CellAlignment.Right),
                new($"{index}q", CellAlignment.Right),
                new($"{index}q/h", CellAlignment.Right),
                new($"{index}q", CellAlignment.Right),
                new($"{index % 100}%", CellAlignment.Right),
                new($"{index % 10}"),
                new($"{index % 24}h"),
                new($"{index % 24}h"),
                new(index % 3 == 0 ? "💤" : ""),
            ];
        }

        internal async Task<(int MessageCount, bool UsedComponentsV2)> SimulateStatusRender(Coop coop, IThreadChannel thread, int participantCount, bool worstCase, CancellationToken cancellationToken) {
            var maxUsers = coop.MaxUsers.GetValueOrDefault();
            var rosterMessages = BuildSyntheticRosterMessages(participantCount, maxUsers, worstCase);
            var fillerText = worstCase ? BuildWorstCaseFillerText(maxUsers) : $"Simulated extras text for {participantCount} participants.";
            var headerText = worstCase ? BuildWorstCaseHeaderText() : $"# Simulated Coop Status\nParticipants: {participantCount}/{maxUsers}";

            var msgs = new List<string>(rosterMessages) { fillerText };
            var existingMessages = await GetDiscordMessages(thread, coop, cancellationToken);

            if(coop.StatusMessagesUseComponentsV2) {
                await UpdateChannelV2(msgs, headerText, thread, coop, existingMessages);
            } else {
                var embed = new EmbedBuilder().WithDescription(headerText).Build();
                await UpdateChannel(msgs, embed, thread, coop, existingMessages);
            }

            var finalIds = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId ?? "[]");
            return (finalIds.Count, coop.StatusMessagesUseComponentsV2);
        }
    }
}
