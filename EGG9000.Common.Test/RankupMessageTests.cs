using System.Collections.Generic;
using System.Linq;

using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    public class RankupMessageTests {
        private static RankupMessage Msg(int group, ulong guildId = 1, bool palaceOnly = false) =>
            new() { GroupBaseOom = group, GuildId = guildId, Text = $"g{group}", PalaceOnly = palaceOnly, Weight = 1 };

        [TestMethod]
        public void ShouldAnnounce_true_when_higher_enabled_not_filtered() {
            Assert.IsTrue(RankupMessageHelper.ShouldAnnounce(newOom: 5, highWater: 4, messagesEnabled: true, groupDisabled: false));
        }

        [TestMethod]
        public void ShouldAnnounce_blocks_eb_spike_recovery() {
            // Already announced oom 30, EB dipped to 28, recovered to 30: must not re-announce.
            Assert.IsFalse(RankupMessageHelper.ShouldAnnounce(newOom: 30, highWater: 30, messagesEnabled: true, groupDisabled: false));
            // Genuine new rank above the high-water still fires.
            Assert.IsTrue(RankupMessageHelper.ShouldAnnounce(newOom: 31, highWater: 30, messagesEnabled: true, groupDisabled: false));
        }

        [TestMethod]
        public void ShouldAnnounce_false_when_disabled_or_filtered() {
            Assert.IsFalse(RankupMessageHelper.ShouldAnnounce(5, 4, messagesEnabled: false, groupDisabled: false));
            Assert.IsFalse(RankupMessageHelper.ShouldAnnounce(5, 4, messagesEnabled: true, groupDisabled: true));
        }

        [TestMethod]
        public void Pool_mixes_group_and_global_when_not_exclusive() {
            var applicable = new List<RankupMessage> { Msg(3), Msg(3), Msg(RankupMessage.GlobalPool) };
            var pool = RankupMessageHelper.SelectPool(applicable, groupBaseOom: 3, exclusive: false);
            Assert.HasCount(3, pool);
        }

        [TestMethod]
        public void Exclusive_drops_global_when_group_present() {
            var applicable = new List<RankupMessage> { Msg(3), Msg(RankupMessage.GlobalPool) };
            var pool = RankupMessageHelper.SelectPool(applicable, groupBaseOom: 3, exclusive: true);
            Assert.HasCount(1, pool);
            Assert.AreEqual(3, pool[0].GroupBaseOom);
        }

        [TestMethod]
        public void Exclusive_falls_back_to_global_when_group_empty() {
            var applicable = new List<RankupMessage> { Msg(RankupMessage.GlobalPool), Msg(RankupMessage.GlobalPool) };
            var pool = RankupMessageHelper.SelectPool(applicable, groupBaseOom: 9, exclusive: true);
            Assert.HasCount(2, pool);
        }

        [TestMethod]
        public void WeightedPick_returns_pool_member_or_null() {
            Assert.IsNull(RankupMessageHelper.WeightedPick([]));
            var pool = new List<RankupMessage> { Msg(0), Msg(3) };
            for(int i = 0; i < 50; i++) Assert.IsTrue(pool.Contains(RankupMessageHelper.WeightedPick(pool)));
        }

        [TestMethod]
        public void AppliesToGuild_palace_applies_to_all() {
            const ulong palaceId = 99;
            var guild = new Guild { Id = 1, DiscordSeverId = 1 };
            var palaceMsg = Msg(0, guildId: palaceId);
            Assert.IsTrue(palaceMsg.AppliesToGuild(guild, palaceId));
        }

        [TestMethod]
        public void AppliesToGuild_own_guild_applies() {
            var guild = new Guild { Id = 7, DiscordSeverId = 7 };
            Assert.IsTrue(Msg(0, guildId: 7).AppliesToGuild(guild, palaceGuildId: 99));
        }

        [TestMethod]
        public void AppliesToGuild_palaceOnly_blocks_other_guild() {
            var guild = new Guild { Id = 7, DiscordSeverId = 7 };
            var other = Msg(0, guildId: 50, palaceOnly: true);
            Assert.IsFalse(other.AppliesToGuild(guild, palaceGuildId: 99));
        }
    }
}
