using Discord;
using System;

namespace EGG9000.Common.Helpers.Discord {
    /*
     * Usage example:
     *
     *   var components = new ComponentsV2Builder(Color.Blue)
     *       .AddSection("**Coop: MyFarm**", user.AvatarUrl)   // section with thumbnail
     *       .AddTextDisplay($"Contract: **{contract.Name}**\nMembers: {coop.Members.Count}/{contract.MaxCoopSize}")
     *       .AddSeparator()
     *       .AddSection("Join now!", new ButtonBuilder("Join", "join_coop_MyFarm", ButtonStyle.Primary))
     *       .AddActionRow(row => row
     *           .WithButton("Refresh", "refresh_coop", ButtonStyle.Secondary)
     *           .WithButton("Leave", "leave_coop", ButtonStyle.Danger))
     *       .Build();
     *
     *   // Quick helpers for one-liner error/success messages:
     *   ComponentsV2Builder.ErrorV2("Coop not found.");
     *   ComponentsV2Builder.SuccessV2("Joined coop successfully!");
     *   
     *   Need to always set the message flag as flags = MessageFlags.ComponentsV2. and once this flag is set the message will only be updated with ComponentsV2, normal text or embeds will not work when a message is sent with this flag. 
     */
    public class ComponentsV2Builder(Color accent) {
        private readonly ContainerBuilder _container = new ContainerBuilder().WithAccentColor(accent);

        public ComponentsV2Builder AddTextDisplay(string content) {
            _container.AddComponent(new TextDisplayBuilder(content));
            return this;
        }

        public ComponentsV2Builder AddSeparator(SeparatorSpacingSize spacing = SeparatorSpacingSize.Small) {
            _container.AddComponent(new SeparatorBuilder(true, spacing));
            return this;
        }

        public ComponentsV2Builder AddSection(string text, string thumbnailUrl = null) {
            var section = new SectionBuilder();
            section.AddComponent(new TextDisplayBuilder(text));
            if(thumbnailUrl is not null)
                section.WithAccessory(new ThumbnailBuilder(new UnfurledMediaItemProperties(thumbnailUrl), null, false));
            _container.AddComponent(section);
            return this;
        }

        public ComponentsV2Builder AddSection(string text, ButtonBuilder button) {
            var section = new SectionBuilder();
            section.AddComponent(new TextDisplayBuilder(text));
            section.WithAccessory(button);
            _container.AddComponent(section);
            return this;
        }

        public ComponentsV2Builder AddActionRow(Action<ActionRowBuilder> configure) {
            _container.WithActionRow(configure);
            return this;
        }

        public MessageComponent Build() =>
            new ComponentBuilderV2().AddComponent(_container).Build();

        public static MessageComponent ErrorV2(string text) =>
            new ComponentsV2Builder(Color.Red).AddTextDisplay($"**Error**\n{text}").Build();

        public static MessageComponent SuccessV2(string text) =>
            new ComponentsV2Builder(Color.Green).AddTextDisplay($"**Success**\n{text}").Build();
    }
}