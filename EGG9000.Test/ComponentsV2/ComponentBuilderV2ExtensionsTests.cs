using Discord;
using EGG9000.Common.Helpers.Discord.ComponentsV2;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    public class ComponentBuilderV2ExtensionsTests {
        [TestMethod]
        public void WithHeader_NoAccountLine_JustTitle() {
            var built = new ContainerBuilder().WithHeader("Test Menu").Build();
            var text = (TextDisplayComponent)built.Components.First();
            Assert.AreEqual("# Test Menu", text.Content);
        }

        [TestMethod]
        public void WithHeader_WithAccountLine_AppendsSecondLine() {
            var built = new ContainerBuilder().WithHeader("Test Menu", "For Account Foo 1.000q").Build();
            var text = (TextDisplayComponent)built.Components.First();
            Assert.AreEqual("# Test Menu\nFor Account Foo 1.000q", text.Content);
        }
    }
}
