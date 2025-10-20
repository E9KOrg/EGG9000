using Discord;
using System;
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
        }

        
        public static Embed EmbedInProgress(string text) {
            return new EmbedBuilder().WithColor(Color.Blue).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Please wait...").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedAlert(string text) {
            return new EmbedBuilder().WithColor(Color.Orange).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Alert").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }
        
        public static Embed EmbedSuccess(string text) {
            return new EmbedBuilder().WithColor(Color.Green).WithDescription(text).WithAuthor(new EmbedAuthorBuilder().WithName("Success").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedWarning(string warningText) {
            return new EmbedBuilder().WithColor(Color.LightOrange).WithDescription(warningText).WithAuthor(new EmbedAuthorBuilder().WithName("Warning").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedError(string errorText, string name = "Error") {
            return new EmbedBuilder().WithColor(Color.Red).WithDescription(errorText).WithAuthor(new EmbedAuthorBuilder().WithName(name).WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedInternalError(string errorText) {
            return new EmbedBuilder().WithColor(Color.Red).WithDescription(errorText).WithAuthor(new EmbedAuthorBuilder().WithName("Internal Error").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedExceptionFrame(Exception e) {
            var frame = new StackTrace(e, true).GetFrame(0);
            return EmbedInternalError(
                $"**Message**:\n{e.Message}\n\n" +
                $"**Frame info**:\n\t" +
                    $"File: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\t" +
                    $"Line: {frame.GetFileLineNumber()}"
            );
        }

        public static Embed MakeCustomEmbed(EmbedType embedType, string embedTitle, string embedText) {
            return new EmbedBuilder().WithColor(embedType switch {
                EmbedType.Success => Color.Green,
                EmbedType.InProgress => Color.Blue,
                EmbedType.Alert => Color.Orange,
                EmbedType.Warning => Color.LightOrange,
                EmbedType.Error => Color.Red,
                EmbedType.InternalError => Color.Red,
                _ => Color.LighterGrey
            }).WithDescription(embedText).WithAuthor(new EmbedAuthorBuilder().WithName(embedTitle).WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }

        public static Embed EmbedCustom(Color embedColor, string embedTitle, string embedText) {
            return new EmbedBuilder().WithColor(embedColor).WithDescription(embedText).WithAuthor(new EmbedAuthorBuilder().WithName(embedTitle).WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }
    }
}
