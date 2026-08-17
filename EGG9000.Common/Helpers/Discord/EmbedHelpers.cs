using Discord;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace EGG9000.Common.Helpers.Discord {
    public class EmbedHelpers {

        public enum EmbedType {
            Success = 0,
            InProgress = 1,
            Alert = 2,
            Warning = 3,
            Error = 4,
            InternalError = 5,
            UnStyled = 6,
        }

        public static readonly string IconUrl = "https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp";

        private static Color ColorFor(EmbedType type) => type switch {
            EmbedType.Success => Color.Green,
            EmbedType.InProgress => Color.Blue,
            EmbedType.Alert => Color.Orange,
            EmbedType.Warning => Color.LightOrange,
            EmbedType.Error => Color.Red,
            EmbedType.InternalError => Color.Red,
            EmbedType.UnStyled => Color.DarkerGrey,
            _ => Color.LighterGrey
        };

private static Embed Build(EmbedType type, string authorName, string text, IEnumerable<EmbedFieldBuilder> fields) {
            // Materialize once: a lazily-evaluated IEnumerable would otherwise be walked twice
            EmbedFieldBuilder[] fieldArray = fields as EmbedFieldBuilder[] ?? [.. fields];

            if(fieldArray.Length > EmbedBuilder.MaxFieldCount) {
                throw new ArgumentOutOfRangeException(
                    nameof(fields),
                    fieldArray.Length,
                    $"An embed may contain at most {EmbedBuilder.MaxFieldCount} fields."
                );
            }

            return new EmbedBuilder()
                .WithColor(ColorFor(type))
                .WithDescription(text)
                .WithAuthor(new EmbedAuthorBuilder().WithName(authorName).WithIconUrl(IconUrl))
                .WithFields(fieldArray)
                .Build();
        }

        public static Embed EmbedInProgress(string text, params IEnumerable<EmbedFieldBuilder> fields)
            => Build(EmbedType.InProgress, "Please wait...", text, fields);

        public static Embed EmbedAlert(string text, params IEnumerable<EmbedFieldBuilder> fields)
            => Build(EmbedType.Alert, "Alert", text, fields);

        public static Embed EmbedSuccess(string text, params IEnumerable<EmbedFieldBuilder> fields)
            => Build(EmbedType.Success, "Success", text, fields);

        public static Embed EmbedWarning(string warningText, params IEnumerable<EmbedFieldBuilder> fields)
            => Build(EmbedType.Warning, "Warning", warningText, fields);

        public static Embed EmbedError(string errorText, string name = "Error", params IEnumerable<EmbedFieldBuilder> fields)
            => Build(EmbedType.Error, name, errorText, fields);

        public static Embed EmbedInternalError(string internalErrorText, params IEnumerable<EmbedFieldBuilder> fields)
            => Build(EmbedType.InternalError, "Internal Error", internalErrorText, fields);

        public static Embed EmbedCustom(EmbedType type, string title, string text, params IEnumerable<EmbedFieldBuilder> fields)
            => Build(type, title, text, fields);

        public static Embed EmbedExceptionFrame(Exception e) {
            foreach(var frame in new StackTrace(e, true).GetFrames() ?? []) {
                if(frame.GetFileLineNumber() > 0) {
                    return EmbedInternalError(
                        $"**Message**:\n{e.Message}\n\n" +
                        $"**Frame info**:\n\t" +
                            $"File: {Path.GetFileName(frame.GetFileName() ?? "")}\n\t" +
                            $"Line: {frame.GetFileLineNumber()}"
                    );
                }
            }
            var frame2 = new StackTrace(e, true).GetFrame(0);
            if(frame2 is null) {
                return EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\t(No stack trace available)");
            }
            return EmbedInternalError(
                $"**Message**:\n{e.Message}\n\n" +
                $"**Frame info**:\n\t" +
                    $"File: {Path.GetFileName(frame2.GetFileName() ?? "")}\n\t" +
                    $"Line: {frame2.GetFileLineNumber()}"
            );
        }

    }
}
