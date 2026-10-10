using Discord;

using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Helpers.Discord.ComponentsV2;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    public class ComponentDisablingTests {
        [TestMethod]
        public void V1_ButtonsAndSelects_AllDisabled() {
            var built = new ComponentBuilder()
                .WithSelectMenu(new SelectMenuBuilder().WithCustomId("menu").AddOption("A", "a"))
                .WithButton("◀", "prev", ButtonStyle.Secondary, row: 1)
                .WithButton("▶", "next", ButtonStyle.Secondary, row: 1)
                .Build();

            var disabled = ComponentDisabling.DisabledCopy(built.Components, componentsV2: false);

            var rows = disabled.Components.Cast<ActionRowComponent>().ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(((SelectMenuComponent)rows[0].Components.Single()).IsDisabled);
            Assert.IsTrue(rows[1].Components.Cast<ButtonComponent>().All(b => b.IsDisabled));
        }

        [TestMethod]
        public void V1_LinkButton_LeftAlone() {
            var built = new ComponentBuilder()
                .WithButton("Docs", style: ButtonStyle.Link, url: "https://example.com")
                .WithButton("Go", "go")
                .Build();

            var disabled = ComponentDisabling.DisabledCopy(built.Components, componentsV2: false);

            var buttons = ((ActionRowComponent)disabled.Components.Single()).Components.Cast<ButtonComponent>().ToList();
            Assert.IsFalse(buttons[0].IsDisabled);
            Assert.IsTrue(buttons[1].IsDisabled);
        }

        [TestMethod]
        public void V1_PreservesCustomIdsAndLabels() {
            var built = new ComponentBuilder().WithButton("Next", "AfxSetsPage:1,0,2").Build();

            var disabled = ComponentDisabling.DisabledCopy(built.Components, componentsV2: false);

            var button = (ButtonComponent)((ActionRowComponent)disabled.Components.Single()).Components.Single();
            Assert.AreEqual("AfxSetsPage:1,0,2", button.CustomId);
            Assert.AreEqual("Next", button.Label);
        }

        [TestMethod]
        public void V2_SectionAccessoryAndNestedRows_Disabled() {
            var built = new MenuPageBuilder("Menu")
                .AddRow("Break", "Not on break", new ButtonBuilder("Set Break", "MCSBreak:0,1", ButtonStyle.Primary))
                .AddSelect(new SelectMenuBuilder().WithCustomId("pick").AddOption("A", "a"))
                .WithReturn("MCSMenu:0,1")
                .Build();

            var disabled = ComponentDisabling.DisabledCopy(built.Components, componentsV2: true);

            var container = (ContainerComponent)disabled.Components.Single();
            var accessory = (ButtonComponent)container.Components.OfType<SectionComponent>().Single().Accessory;
            Assert.IsTrue(accessory.IsDisabled);
            var rows = container.Components.OfType<ActionRowComponent>().ToList();
            Assert.IsTrue(((SelectMenuComponent)rows[0].Components.Single()).IsDisabled);
            Assert.IsTrue(((ButtonComponent)rows[1].Components.Single()).IsDisabled);
        }

        [TestMethod]
        public void V2_KeepsTextAndAccent() {
            var built = new MenuPageBuilder("Menu").WithAccent(Color.Red).WithDescription("hello").Build();

            var disabled = ComponentDisabling.DisabledCopy(built.Components, componentsV2: true);

            var container = (ContainerComponent)disabled.Components.Single();
            Assert.AreEqual((Color)Color.Red, container.AccentColor);
            Assert.IsTrue(container.Components.OfType<TextDisplayComponent>().Any(t => t.Content == "hello"));
        }

        [TestMethod]
        public void Empty_ReturnsNull() {
            Assert.IsNull(ComponentDisabling.DisabledCopy([], componentsV2: false));
            Assert.IsNull(ComponentDisabling.DisabledCopy(null, componentsV2: true));
        }

        [TestMethod]
        public void DoesNotMutateSource() {
            var built = new ComponentBuilder().WithButton("Go", "go").Build();

            ComponentDisabling.DisabledCopy(built.Components, componentsV2: false);

            var original = (ButtonComponent)((ActionRowComponent)built.Components.Single()).Components.Single();
            Assert.IsFalse(original.IsDisabled);
        }
    }
}
