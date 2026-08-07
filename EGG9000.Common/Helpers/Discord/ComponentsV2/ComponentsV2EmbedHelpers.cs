using Discord;
using System;
using System.Diagnostics;
using System.IO;

namespace EGG9000.Common.Helpers.Discord.ComponentsV2 {
    // ComponentsV2 equivalent of EGG9000.Common.Helpers.Discord.EmbedHelpers - same variants, same
    // Color per variant, so a call site can switch EmbedHelpers.EmbedError(x) for
    // ComponentsV2EmbedHelpers.Error(x) with a matching visual result. Every method here returns an
    // already-built MessageComponent; callers must pass flags: MessageFlags.ComponentsV2.
    public static class ComponentsV2EmbedHelpers {
        private static MessageComponent Framed(Color accent, string title, string text) =>
            new ComponentBuilderV2()
                .AddComponent(new ContainerBuilder()
                    .WithAccentColor(accent)
                    .WithHeader(title)
                    .WithSection(text, ThumbnailUrl))
                .Build();

        private const string ThumbnailUrl = "https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp";

        public static MessageComponent InProgress(string text) => Framed(Color.Blue, "Please wait...", text);

        public static MessageComponent Alert(string text) => Framed(Color.Orange, "Alert", text);

        public static MessageComponent Success(string text) => Framed(Color.Green, "Success", text);

        public static MessageComponent Warning(string text) => Framed(Color.LightOrange, "Warning", text);

        public static MessageComponent Error(string text, string name = "Error") => Framed(Color.Red, name, text);

        public static MessageComponent InternalError(string text) => Framed(Color.Red, "Internal Error", text);

        public static MessageComponent MakeCustom(EmbedHelpers.EmbedType embedType, string title, string text) =>
            Framed(embedType switch {
                EmbedHelpers.EmbedType.Success => Color.Green,
                EmbedHelpers.EmbedType.InProgress => Color.Blue,
                EmbedHelpers.EmbedType.Alert => Color.Orange,
                EmbedHelpers.EmbedType.Warning => Color.LightOrange,
                EmbedHelpers.EmbedType.Error => Color.Red,
                EmbedHelpers.EmbedType.InternalError => Color.Red,
                _ => Color.LighterGrey
            }, title, text);

        public static MessageComponent Custom(Color color, string title, string text) => Framed(color, title, text);

        // Red-framed page for modal validation failures: error text + Re-enter / Cancel buttons.
        // Used by the ContractSettings and ShipReturnDM modal handlers.
        public static MessageComponent ErrorWithRetry(string title, string error, string reenterCustomId, string cancelCustomId) =>
            new MenuPageBuilder(title)
                .WithAccent(Color.Red)
                .AddRow("ERROR", error)
                .AddButtons(
                    new ButtonBuilder("Re-enter", reenterCustomId, ButtonStyle.Primary),
                    new ButtonBuilder("Cancel", cancelCustomId, ButtonStyle.Secondary))
                .Build();

        public static MessageComponent ExceptionFrame(Exception e) {
            foreach(var frame in new StackTrace(e, true).GetFrames() ?? []) {
                if(frame.GetFileLineNumber() > 0) {
                    return InternalError(
                        $"**Message**:\n{e.Message}\n\n" +
                        $"**Frame info**:\n\t" +
                            $"File: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\t" +
                            $"Line: {frame.GetFileLineNumber()}"
                    );
                }
            }
            var frame2 = new StackTrace(e, true).GetFrame(0);
            if(frame2 is null) {
                return InternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\t(No stack trace available)");
            }
            return InternalError(
                $"**Message**:\n{e.Message}\n\n" +
                $"**Frame info**:\n\t" +
                    $"File: {Path.GetFileName(frame2.GetFileName() ?? "") ?? "(Unknown)"}\n\t" +
                    $"Line: {frame2.GetFileLineNumber()}"
            );
        }
    }
}
