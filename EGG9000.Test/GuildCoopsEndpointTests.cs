using EGG9000.Site.Controllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {

    // Guards on the API-key co-ops endpoint. The cache key matters as much as the validation: it is
    // keyed per guild tag, so one server's keys cannot serve each other's guild rosters out of cache.
    [TestClass]
    [TestCategory("Unit")]
    public class GuildCoopsEndpointTests {

        [TestMethod]
        public void MissingContractId_IsRejected() {
            Assert.IsNotNull(APIController.ValidateGuildCoopsRequest(contractId: "", guildTag: "Tachyon"));
        }

        [TestMethod]
        public void WhitespaceContractId_IsRejected() {
            Assert.IsNotNull(APIController.ValidateGuildCoopsRequest(contractId: "   ", guildTag: "Tachyon"));
        }

        [TestMethod]
        public void KeyWithNoGuildScope_IsRejected() {
            // Most existing API keys have MembersOfGuildOnly unset. A guild-scoped endpoint must say so
            // loudly rather than quietly hand back an empty list that looks like "no co-ops".
            Assert.IsNotNull(APIController.ValidateGuildCoopsRequest(contractId: "winter-jubilee-2026", guildTag: null));
        }

        [TestMethod]
        public void ContractIdAndGuildScopePresent_IsAccepted() {
            Assert.IsNull(APIController.ValidateGuildCoopsRequest(contractId: "winter-jubilee-2026", guildTag: "Tachyon"));
        }

        [TestMethod]
        public void CacheKeySeparatesGuildTagsOnTheSameServerAndContract() {
            var tachyon = APIController.BuildGuildCoopsCacheKey(123, "winter-jubilee-2026", "Tachyon");
            var other = APIController.BuildGuildCoopsCacheKey(123, "winter-jubilee-2026", "SomeOtherGuild");

            Assert.AreNotEqual(tachyon, other, "Two guilds on one server must never share a cache entry.");
        }

        [TestMethod]
        public void CacheKeySeparatesServers() {
            Assert.AreNotEqual(
                APIController.BuildGuildCoopsCacheKey(123, "winter-jubilee-2026", "Tachyon"),
                APIController.BuildGuildCoopsCacheKey(456, "winter-jubilee-2026", "Tachyon"));
        }

        [TestMethod]
        public void CacheKeySeparatesContracts() {
            Assert.AreNotEqual(
                APIController.BuildGuildCoopsCacheKey(123, "winter-jubilee-2026", "Tachyon"),
                APIController.BuildGuildCoopsCacheKey(123, "spring-festival-2026", "Tachyon"));
        }

        [TestMethod]
        public void CacheKeyIgnoresGuildTagCasingAndPadding() {
            // The query matches tags case-insensitively, so the cache must not split one guild's entry
            // across every way an admin happened to type the tag.
            Assert.AreEqual(
                APIController.BuildGuildCoopsCacheKey(123, "winter-jubilee-2026", "Tachyon"),
                APIController.BuildGuildCoopsCacheKey(123, "winter-jubilee-2026", "  tachyon "));
        }
    }
}
