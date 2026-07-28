using EGG9000.Bot.Automated.Coops;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test.Coops {
    [TestClass]
    public class ThreadsCoopStatusUpdaterSimulateTests {
        [TestMethod]
        public void ChunkAtDiscordMessageLimit_UnderLimit_ReturnsSingleChunk() {
            var chunks = ThreadsCoopStatusUpdater.ChunkAtDiscordMessageLimit("```short```", limit: 2000);

            Assert.AreEqual(1, chunks.Count);
        }

        [TestMethod]
        public void ChunkAtDiscordMessageLimit_OverLimit_SplitsAndReclosesCodeBlock() {
            var longTable = "```" + string.Join("\n", Enumerable.Range(0, 500).Select(i => $"row {i} padding padding padding")) + "```";

            var chunks = ThreadsCoopStatusUpdater.ChunkAtDiscordMessageLimit(longTable, limit: 2000);

            Assert.IsTrue(chunks.Count > 1);
            Assert.IsTrue(chunks.All(c => c.Length <= 2000));
            Assert.IsTrue(chunks.All(c => c.StartsWith("```") && c.EndsWith("```")));
        }

        [TestMethod]
        public void BuildSyntheticRosterMessages_ZeroParticipants_StillProducesHeaderChunk() {
            var messages = ThreadsCoopStatusUpdater.BuildSyntheticRosterMessages(0, maxUsers: 40, worstCase: true);

            Assert.AreEqual(1, messages.Count);
        }

        [TestMethod]
        public void BuildSyntheticRosterMessages_WorstCaseIsAtLeastAsLargeAsTypical() {
            var worstCase = ThreadsCoopStatusUpdater.BuildSyntheticRosterMessages(40, maxUsers: 40, worstCase: true);
            var typical = ThreadsCoopStatusUpdater.BuildSyntheticRosterMessages(40, maxUsers: 40, worstCase: false);

            var worstCaseChars = worstCase.Sum(m => m.Length);
            var typicalChars = typical.Sum(m => m.Length);

            Assert.IsTrue(worstCaseChars >= typicalChars);
        }

        [TestMethod]
        public void BuildSyntheticRosterMessages_LargeParticipantCountStaysWithinChunkLimit() {
            var messages = ThreadsCoopStatusUpdater.BuildSyntheticRosterMessages(70, maxUsers: 70, worstCase: true);

            Assert.IsTrue(messages.All(m => m.Length <= 2000));
        }
    }
}
