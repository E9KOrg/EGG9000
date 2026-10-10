using Discord;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers.Discord {
    public static class ComponentDisabling {
        public static MessageComponent DisabledCopy(IMessage message) => DisabledCopy(message.Components, message.IsComponentsV2());

        public static MessageComponent DisabledCopy(IReadOnlyCollection<IMessageComponent> components, bool componentsV2) {
            if(components is null || components.Count == 0) return null;
            var builders = components.Select(c => Disable(c.ToBuilder())).ToList();
            if(componentsV2) return new ComponentBuilderV2().WithComponents(builders).Build();
            return new ComponentBuilder().WithRows(builders.OfType<ActionRowBuilder>()).Build();
        }

        public static IMessageComponentBuilder Disable(IMessageComponentBuilder builder) {
            switch(builder) {
                case ButtonBuilder b:
                    if(b.Style != ButtonStyle.Link && b.Style != ButtonStyle.Premium) b.IsDisabled = true;
                    break;
                case SelectMenuBuilder s:
                    s.IsDisabled = true;
                    break;
                case SectionBuilder section:
                    if(section.Accessory is not null) Disable(section.Accessory);
                    break;
                case IComponentContainer container:
                    foreach(var child in container.Components) Disable(child);
                    break;
            }
            return builder;
        }
    }
}
