using EGG9000.Bot.Interactions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ChannelContextResolutionTest {
        [TestMethod]
        public void CoopOnly_RejectsWhenCoopMissing() =>
            Assert.IsTrue(ChannelContextResolution.ShouldReject(true, false, false, false));

        [TestMethod]
        public void CoopOnly_PassesWhenCoopResolved() =>
            Assert.IsFalse(ChannelContextResolution.ShouldReject(true, false, true, false));

        [TestMethod]
        public void ContractOnly_RejectsWhenContractMissing() =>
            Assert.IsTrue(ChannelContextResolution.ShouldReject(false, true, false, false));

        [TestMethod]
        public void Either_PassesWhenOnlyCoopResolved() =>
            Assert.IsFalse(ChannelContextResolution.ShouldReject(true, true, true, false));

        [TestMethod]
        public void Either_PassesWhenOnlyContractResolved() =>
            Assert.IsFalse(ChannelContextResolution.ShouldReject(true, true, false, true));

        [TestMethod]
        public void Either_RejectsOnlyWhenBothMissing() =>
            Assert.IsTrue(ChannelContextResolution.ShouldReject(true, true, false, false));

        [TestMethod]
        public void RejectMessage_BothRequested_MentionsEither() =>
            StringAssert.Contains(ChannelContextResolution.RejectMessage(true, true), "co-op or contract");

        [TestMethod]
        public void RejectMessage_CoopOnly_MentionsCoop() =>
            StringAssert.Contains(ChannelContextResolution.RejectMessage(true, false), "co-op channel");

        [TestMethod]
        public void RejectMessage_ContractOnly_MentionsContract() =>
            StringAssert.Contains(ChannelContextResolution.RejectMessage(false, true), "contract channel");
    }
}
