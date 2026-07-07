using Discord;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    public class ComponentsV2EmbedHelpersTests {
        private static ContainerComponent Container(MessageComponent built) =>
            (ContainerComponent)built.Components.First();

        private static string HeaderText(ContainerComponent container) =>
            ((TextDisplayComponent)container.Components.Last()).Content;

        private static string BodyText(ContainerComponent container) {
            var section = (SectionComponent)container.Components.First();
            return ((TextDisplayComponent)section.Components.First()).Content;
        }

        [TestMethod]
        public void Success_UsesGreenAccentAndTitle() {
            var container = Container(ComponentsV2EmbedHelpers.Success("done"));
            Assert.AreEqual(Color.Green, container.AccentColor);
            Assert.AreEqual("# Success", HeaderText(container));
            StringAssert.Contains(BodyText(container), "done");
        }

        [TestMethod]
        public void Error_UsesRedAccentAndCustomName() {
            var container = Container(ComponentsV2EmbedHelpers.Error("bad thing", "Custom Error"));
            Assert.AreEqual(Color.Red, container.AccentColor);
            Assert.AreEqual("# Custom Error", HeaderText(container));
            StringAssert.Contains(BodyText(container), "bad thing");
        }

        [TestMethod]
        public void Warning_UsesLightOrangeAccent() {
            var container = Container(ComponentsV2EmbedHelpers.Warning("careful"));
            Assert.AreEqual(Color.LightOrange, container.AccentColor);
        }

        [TestMethod]
        public void MakeCustom_MapsEmbedTypeToSameColorsAsEmbedHelpers() {
            var container = Container(ComponentsV2EmbedHelpers.MakeCustom(EGG9000.Common.Helpers.Discord.EmbedHelpers.EmbedType.Alert, "Heads up", "text"));
            Assert.AreEqual(Color.Orange, container.AccentColor);
        }

        [TestMethod]
        public void ExceptionFrame_IncludesMessageAndFileLine() {
            Exception thrown;
            try {
                throw new InvalidOperationException("boom");
            } catch(Exception e) {
                thrown = e;
            }

            var container = Container(ComponentsV2EmbedHelpers.ExceptionFrame(thrown));
            Assert.AreEqual(Color.Red, container.AccentColor);
            StringAssert.Contains(BodyText(container), "boom");
            StringAssert.Contains(BodyText(container), "Line:");
        }
    }
}
