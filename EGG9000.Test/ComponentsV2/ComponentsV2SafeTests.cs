using Discord;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    public class ComponentsV2SafeTests {
        [TestMethod]
        public void WithTextDisplaySafe_UnderLimit_PassesThroughUnchanged() {
            var built = new ContainerBuilder().WithTextDisplaySafe("short text").Build();
            var text = (TextDisplayComponent)built.Components.First();
            Assert.AreEqual("short text", text.Content);
        }

        [TestMethod]
        public void WithTextDisplaySafe_OverLimit_Truncates() {
            var tooLong = new string('a', ComponentsV2Safe.TextDisplayMax + 100);
            var built = new ContainerBuilder().WithTextDisplaySafe(tooLong).Build();
            var text = (TextDisplayComponent)built.Components.First();
            Assert.AreEqual(ComponentsV2Safe.TextDisplayMax, text.Content.Length);
        }

        [TestMethod]
        public void WithHeaderSafe_OverLimit_TruncatesTitleAndAccountLine() {
            var tooLong = new string('a', ComponentsV2Safe.TextDisplayMax + 100);
            var built = new ContainerBuilder().WithHeaderSafe(tooLong, tooLong).Build();
            var text = (TextDisplayComponent)built.Components.First();
            Assert.IsTrue(text.Content.Length <= 2 * ComponentsV2Safe.TextDisplayMax + 3);
        }

        // ThrowIfModeMismatch(IMessage, bool) is a thin wrapper around this predicate; IMessage has no
        // in-repo test double (66 members, no mocking library referenced), so the predicate - the actual
        // decision logic - is covered directly instead.
        [TestMethod]
        public void IsComponentsV2_TrueOnlyWhenFlagSet() {
            MessageFlags? withFlag = MessageFlags.ComponentsV2;
            MessageFlags? withoutFlag = MessageFlags.Ephemeral;
            MessageFlags? none = null;

            Assert.IsTrue(withFlag.IsComponentsV2());
            Assert.IsFalse(withoutFlag.IsComponentsV2());
            Assert.IsFalse(none.IsComponentsV2());
        }
    }
}
