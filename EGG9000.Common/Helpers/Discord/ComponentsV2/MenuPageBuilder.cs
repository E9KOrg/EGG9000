using Discord;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers.Discord.ComponentsV2 {
    // Reusable Components V2 "menu page": one accent-colored container holding a "# Title" header
    // (plus optional account line), description text, setting rows (bold label + value with one
    // button accessory), separators, select menus, and button rows. WithReturn always renders last
    // regardless of call order. Build() throws when the page would exceed Discord's
    // 40-components-per-message cap so the failure is loud at dev time instead of a silent 400.
    // Callers must send the result with flags: MessageFlags.ComponentsV2.
    public class MenuPageBuilder {
        private readonly ContainerBuilder _container = new();
        private (string customId, string label)? _return;

        public MenuPageBuilder(string title, string accountLine = null) {
            _container.WithAccentColor(Color.Blue).WithHeaderSafe(title, accountLine);
        }

        public MenuPageBuilder WithAccent(Color color) {
            _container.WithAccentColor(color);
            return this;
        }

        public MenuPageBuilder WithDescription(string markdown) => AddText(markdown);

        public MenuPageBuilder AddText(string markdown) {
            _container.WithTextDisplaySafe(markdown);
            return this;
        }

        public MenuPageBuilder AddRow(string label, string value) => AddText($"**{label}**\n{value}");

        public MenuPageBuilder AddRow(string label, string value, ButtonBuilder button) {
            _container.WithSection($"**{label}**\n{value}".Truncate(ComponentsV2Safe.TextDisplayMax), button);
            return this;
        }

        public MenuPageBuilder AddDivider() {
            _container.WithSeparator();
            return this;
        }

        public MenuPageBuilder AddSelect(SelectMenuBuilder select) {
            _container.WithActionRow(row => row.WithSelectMenu(select));
            return this;
        }

        public MenuPageBuilder AddButtons(params ButtonBuilder[] buttons) {
            foreach(var chunk in buttons.Chunk(5))
                _container.WithActionRow(row => {
                    foreach(var button in chunk)
                        row.WithButton(button);
                });
            return this;
        }

        public MenuPageBuilder WithReturn(string customId, string label = "← Return") {
            _return = (customId, label);
            return this;
        }

        public MessageComponent Build() {
            if(_return.HasValue)
                _container.WithActionRow(row => row.WithButton(_return.Value.label, _return.Value.customId, ButtonStyle.Secondary));

            // Count against the builder tree (not the built components) before calling Discord.NET's
            // own Build(): Discord.NET 3.20.1 enforces the same 40-component cap internally and throws
            // ArgumentException first, which would mask our more descriptive InvalidOperationException.
            var count = CountComponents(_container);
            if(count > ComponentsV2Safe.ComponentsPerMessageMax)
                throw new InvalidOperationException($"MenuPageBuilder produced {count} components; Discord's cap is {ComponentsV2Safe.ComponentsPerMessageMax}.");

            return new ComponentBuilderV2().AddComponent(_container).Build();
        }

        private static int CountComponents(IMessageComponentBuilder component) => 1 + component switch {
            ContainerBuilder c => c.Components.Sum(CountComponents),
            SectionBuilder s => s.Components.Sum(CountComponents) + (s.Accessory is null ? 0 : 1),
            ActionRowBuilder r => r.Components.Count,
            _ => 0
        };
    }
}
