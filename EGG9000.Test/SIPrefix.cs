using EGG9000.Bot.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class SIPrefixTest {
        [TestMethod]
        public void Parse1000() {
            var prefix = SIPrefix.GetPrefix(1000);
            Assert.AreEqual("kilo", prefix.Name);
        }
        [TestMethod]
        public void Parse999999() {
            var prefix = SIPrefix.GetPrefix(999999);
            Assert.AreEqual("kilo", prefix.Name);
        }
        [TestMethod]
        public void Parse1000000() {
            var prefix = SIPrefix.GetPrefix(1000000);
            Assert.AreEqual("mega", prefix.Name);
        }
    }
}