using Discord;

using EGG9000.Common.Helpers.Discord.ComponentsV2;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;

namespace EGG9000.Test.ComponentsV2 {
    [TestClass]
    public class MenuPageBuilderTests {
        private static ContainerComponent Container(MessageComponent built) => (ContainerComponent)built.Components.Single();

        [TestMethod]
        public void Header_TitleOnly() {
            var built = new MenuPageBuilder("Test Menu").Build();
            var text = (TextDisplayComponent)Container(built).Components.First();
            Assert.AreEqual("# Test Menu", text.Content);
        }

        [TestMethod]
        public void Header_WithAccountLine_AppendsSecondLine() {
            var built = new MenuPageBuilder("Test Menu", "**Account:** Foo 1.000q").Build();
            var text = (TextDisplayComponent)Container(built).Components.First();
            Assert.AreEqual("# Test Menu\n**Account:** Foo 1.000q", text.Content);
        }

        [TestMethod]
        public void Accent_DefaultsToBlue_WithAccentOverrides() {
            Assert.AreEqual((Color)Color.Blue, Container(new MenuPageBuilder("A").Build()).AccentColor);
            Assert.AreEqual((Color)Color.Red, Container(new MenuPageBuilder("A").WithAccent(Color.Red).Build()).AccentColor);
        }

        [TestMethod]
        public void AddRow_WithButton_BuildsSectionWithAccessory() {
            var built = new MenuPageBuilder("A")
                .AddRow("Break", "Not on break", new ButtonBuilder("Set Break", "MCSBreak:0,1", ButtonStyle.Primary))
                .Build();
            var section = Container(built).Components.OfType<SectionComponent>().Single();
            var text = (TextDisplayComponent)section.Components.Single();
            Assert.AreEqual("**Break**\nNot on break", text.Content);
            var accessory = (ButtonComponent)section.Accessory;
            Assert.AreEqual("Set Break", accessory.Label);
            Assert.AreEqual("MCSBreak:0,1", accessory.CustomId);
        }

        [TestMethod]
        public void AddRow_TextOnly_NoSection() {
            var built = new MenuPageBuilder("A").AddRow("Break", "Not on break").Build();
            Assert.AreEqual(0, Container(built).Components.OfType<SectionComponent>().Count());
            Assert.IsTrue(Container(built).Components.OfType<TextDisplayComponent>().Any(t => t.Content == "**Break**\nNot on break"));
        }

        [TestMethod]
        public void AddButtons_SevenButtons_ChunksIntoTwoRows() {
            var buttons = Enumerable.Range(0, 7).Select(i => new ButtonBuilder($"B{i}", $"id{i}", ButtonStyle.Primary)).ToArray();
            var built = new MenuPageBuilder("A").AddButtons(buttons).Build();
            var rows = Container(built).Components.OfType<ActionRowComponent>().ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(5, rows[0].Components.Count);
            Assert.AreEqual(2, rows[1].Components.Count);
        }

        [TestMethod]
        public void WithReturn_RendersLast_SecondaryStyle() {
            var built = new MenuPageBuilder("A")
                .WithReturn("MCSMenu:0,1")
                .AddRow("Row", "value", new ButtonBuilder("Btn", "id", ButtonStyle.Primary))
                .Build();
            var last = Container(built).Components.Last();
            var row = (ActionRowComponent)last;
            var button = (ButtonComponent)row.Components.Single();
            Assert.AreEqual("← Return", button.Label);
            Assert.AreEqual("MCSMenu:0,1", button.CustomId);
            Assert.AreEqual(ButtonStyle.Secondary, button.Style);
        }

        [TestMethod]
        public void Build_Over40Components_Throws() {
            var page = new MenuPageBuilder("A");
            for(var i = 0; i < 14; i++)
                page.AddRow($"Row{i}", "v", new ButtonBuilder($"B{i}", $"id{i}", ButtonStyle.Primary));
            Assert.ThrowsExactly<InvalidOperationException>(() => page.Build());
        }

        [TestMethod]
        public void ErrorWithRetry_RedAccent_TwoButtons() {
            var built = ComponentsV2EmbedHelpers.ErrorWithRetry("Redo Leggacies Menu", "bad input", "RLThreshModal:0,1", "MCSRL:0,1");
            var container = (ContainerComponent)built.Components.Single();
            Assert.AreEqual((Color)Color.Red, container.AccentColor);
            var row = container.Components.OfType<ActionRowComponent>().Single();
            var labels = row.Components.OfType<ButtonComponent>().Select(b => b.Label).ToList();
            CollectionAssert.AreEqual(new[] { "Re-enter", "Cancel" }, labels);
        }
    }
}
