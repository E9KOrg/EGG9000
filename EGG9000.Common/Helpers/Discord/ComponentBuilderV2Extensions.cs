namespace Discord {
    // Extends Discord.NET's ComponentBuilderV2 and ContainerBuilder with convenience overloads,
    // Discord.NET already provides ComponentContainerExtensions with generic WithTextDisplay,
    // WithSeparator, WithActionRow, etc. This file adds higher level shorthands for sections
    // and static factories for common one-liner responses.
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
    //   ComponentBuilderV2Extensions.ErrorV2("Coop not found.");
    //   ComponentBuilderV2Extensions.SuccessV2("Joined successfully!");
    //
    //   Note: MessageFlags.ComponentsV2 is required; once set, normal text and embeds are ignored.


    public static class ComponentBuilderV2Extensions {
        public static T WithSection<T>(this T container, string text, string? thumbnailUrl = null) where T : IComponentContainer {
            var section = new SectionBuilder();
            section.AddComponent(new TextDisplayBuilder(text));
            if (thumbnailUrl is not null)
                section.WithAccessory(new ThumbnailBuilder(new UnfurledMediaItemProperties(thumbnailUrl), null, false));
            container.AddComponent(section);
            return container;
        }

        public static T WithSection<T>(this T container, string text, ButtonBuilder button) where T : IComponentContainer {
            var section = new SectionBuilder();
            section.AddComponent(new TextDisplayBuilder(text));
            section.WithAccessory(button);
            container.AddComponent(section);
            return container;
        }

        public static MessageComponent ErrorV2(string text) =>
            new ComponentBuilderV2()
                .AddComponent(new ContainerBuilder()
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay($"**Error**\n{text}"))
                .Build();

        public static MessageComponent SuccessV2(string text) =>
            new ComponentBuilderV2()
                .AddComponent(new ContainerBuilder()
                    .WithAccentColor(Color.Green)
                    .WithTextDisplay($"**Success**\n{text}"))
                .Build();
    }
}
