// Because of namespacing rules around extensions, we have to keep this here,
// but namespace it in Discord.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Discord {
#pragma warning restore IDE0130 // Namespace does not match folder structure
    // Extends Discord.NET's ComponentBuilderV2 and ContainerBuilder with convenience overloads.
    // Discord.NET already provides ComponentContainerExtensions with generic WithTextDisplay,
    // WithSeparator, WithActionRow, WithSection(container, content, accessory, ...) etc - only add
    // shorthands here that aren't already covered generically. For EmbedHelpers-equivalent
    // Success/Error/Warning/etc variants, see ComponentsV2EmbedHelpers. For length/count safety,
    // see ComponentsV2Safe.
    //
    // Usage:
    //   await interaction.RespondAsync(
    //       components: new ComponentBuilderV2()
    //           .AddComponent(new ContainerBuilder()
    //               .WithAccentColor(Color.Blue)
    //               .WithSection("**Coop: MyFarm**", user.AvatarUrl)
    //               .WithTextDisplay($"Contract: **{contract.Name}**\nMembers: {count}/{max}")
    //               .WithSeparator()
    //               .WithSection("Join now!", new ButtonBuilder("Join", "join_coop_MyFarm", ButtonStyle.Primary))
    //               .WithActionRow(row => row
    //                   .WithButton("Refresh", "refresh_coop", ButtonStyle.Secondary)
    //                   .WithButton("Leave", "leave_coop", ButtonStyle.Danger)))
    //           .Build(),
    //       flags: MessageFlags.ComponentsV2);
    //
    //   Note: MessageFlags.ComponentsV2 is required; once set, normal text and embeds are ignored,
    //   and Discord will not allow it to be unset on a later edit of the same message.

    public static class ComponentBuilderV2Extensions {
        public static T WithHeader<T>(this T container, string title, string accountLine = null) where T : class, IComponentContainer, IStaticComponentContainer {
            var header = accountLine is null ? $"# {title}" : $"# {title}\n{accountLine}";
            return container.WithTextDisplay(header);
        }

#nullable enable
        public static T WithSection<T>(this T container, string text, string? thumbnailUrl = null) where T : IComponentContainer {
            var section = new SectionBuilder();
            section.AddComponent(new TextDisplayBuilder(text));
            if (thumbnailUrl is not null)
                section.WithAccessory(new ThumbnailBuilder(new UnfurledMediaItemProperties(thumbnailUrl), null, false));
            container.AddComponent(section);
            return container;
        }
#nullable disable

        public static T WithSection<T>(this T container, string text, ButtonBuilder button) where T : IComponentContainer {
            var section = new SectionBuilder();
            section.AddComponent(new TextDisplayBuilder(text));
            section.WithAccessory(button);
            container.AddComponent(section);
            return container;
        }
    }
}
