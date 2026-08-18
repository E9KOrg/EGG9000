using EGG9000.Onboarding.Steps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace EGG9000.Test.Onboarding {
    [TestClass]
    [TestCategory("Unit")]
    public class GuildSelectorTests {
        private static readonly List<GuildChoice> Two = [
            new(1108127105088241746UL, "My Dev Server"),
            new(9876543210987654321UL, "Some Other Server")
        ];

        [TestMethod]
        public void Select_RequestedIdPresent_ReturnsItWithoutPrompting() {
            var promptCalled = false;
            var choice = GuildSelector.Select(Two, 9876543210987654321UL, () => { promptCalled = true; return "1"; }, new StringWriter());
            Assert.AreEqual(9876543210987654321UL, choice.Id);
            Assert.AreEqual("Some Other Server", choice.Name);
            Assert.IsFalse(promptCalled, "A supplied --guild must not prompt.");
        }

        [TestMethod]
        public void Select_RequestedIdAbsent_ThrowsAndNamesAvailableGuilds() {
            var ex = Assert.ThrowsExactly<ArgumentException>(
                () => GuildSelector.Select(Two, 111UL, () => "1", new StringWriter()));
            StringAssert.Contains(ex.Message, "My Dev Server");
        }

        [TestMethod]
        public void Select_SingleGuild_AutoSelectsWithoutPrompting() {
            var promptCalled = false;
            List<GuildChoice> one = [new(42UL, "Only Server")];
            var choice = GuildSelector.Select(one, null, () => { promptCalled = true; return "1"; }, new StringWriter());
            Assert.AreEqual(42UL, choice.Id);
            Assert.IsFalse(promptCalled);
        }

        [TestMethod]
        public void Select_NoRequestedId_PromptsAndUsesTheAnswer() {
            var choice = GuildSelector.Select(Two, null, () => "2", new StringWriter());
            Assert.AreEqual(9876543210987654321UL, choice.Id);
        }

        [TestMethod]
        public void Select_InvalidThenValidAnswer_Reprompts() {
            var answers = new Queue<string>(["0", "banana", "1"]);
            var choice = GuildSelector.Select(Two, null, answers.Dequeue, new StringWriter());
            Assert.AreEqual(1108127105088241746UL, choice.Id);
        }

        [TestMethod]
        public void Select_NoGuilds_Throws() {
            var ex = Assert.ThrowsExactly<ArgumentException>(
                () => GuildSelector.Select([], null, () => "1", new StringWriter()));
            StringAssert.Contains(ex.Message, "not in any");
        }

        [TestMethod]
        public void Select_InputUnavailable_Throws() {
            Assert.ThrowsExactly<ArgumentException>(
                () => GuildSelector.Select(Two, null, () => null!, new StringWriter()));
        }

        // --no-wait promises a scripted run cannot hang. Without this the picker's while(true) loop
        // would sit in Console.ReadLine forever on any host with a live stdin.
        [TestMethod]
        public void Select_PromptNotAllowed_ThrowsInsteadOfBlocking() {
            var ex = Assert.ThrowsExactly<ArgumentException>(
                () => GuildSelector.Select(Two, null, () => { Assert.Fail("must not prompt"); return ""; }, new StringWriter(), allowPrompt: false));
            StringAssert.Contains(ex.Message, "--guild");
        }

        // One guild needs no prompt, so a scripted run must still succeed rather than fail on the
        // non-interactive check.
        [TestMethod]
        public void Select_PromptNotAllowedButOnlyOneGuild_StillSelectsIt() {
            IReadOnlyList<GuildChoice> one = [new GuildChoice(1, "Only")];

            var choice = GuildSelector.Select(one, null, () => { Assert.Fail("must not prompt"); return ""; }, new StringWriter(), allowPrompt: false);

            Assert.AreEqual(1UL, choice.Id);
        }
    }
}
