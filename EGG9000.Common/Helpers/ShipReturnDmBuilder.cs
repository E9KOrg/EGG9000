using Discord;

using EGG9000.Bot;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;

using static EGG9000.Bot.Helpers.DiscordHelpersExt;
using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Helpers {
    public static class ShipReturnDmBuilder {
        public record FuelLine(string Emoji, double Amount, double Percent);

        public record ShipReturnDmModel(
            Spaceship Ship,
            long ReturnUnix,
            long LastBackupUnix,
            bool NeedsFuel,
            bool HasReturned,
            string AccountName,
            bool MultiAccount,
            IReadOnlyList<FuelLine> FuelTank,
            ulong UserId,
            int AccountIndex,
            string SiteBaseUrl);

        public static bool ShouldMarkSent(DMResult result) {
            return result is DMResult.Success or DMResult.CannotSendToUser;
        }

        public static (Embed Embed, MessageComponent Components) Build(ShipReturnDmModel m) {
            var shipName = MissionHelpers.GetProperShipName(m.Ship);
            var tag = MissionHelpers.GetShipEmojiTag(m.Ship);
            var fuelClause = m.NeedsFuel ? " and your current ship needs fueling." : " and your current ship is ready.";
            var color = m.HasReturned ? Color.Green : (m.NeedsFuel ? new Color(0xE0, 0xA0, 0x10) : Color.Blue);

            var lead = m.HasReturned
                ? $"Your **{shipName}** has returned{fuelClause}"
                : $"Your **{shipName}** returns <t:{m.ReturnUnix}:R>{fuelClause}";
            var description = $"{lead}\nLast backup <t:{m.LastBackupUnix}:R>.";
            if(m.MultiAccount) description += $"\n*(For {m.AccountName})*";

            var embed = new EmbedBuilder()
                .WithTitle($"{tag} {shipName}".Trim())
                .WithColor(color)
                .WithDescription(description);

            var url = MissionHelpers.GetShipEmojiUrl(m.Ship);
            if(url != "") embed.WithThumbnailUrl(url);

            if(m.FuelTank.Count > 0) {
                embed.AddField("Fuel Tank", string.Join("\n", m.FuelTank.Select(f =>
                    $"{f.Emoji} {Bar(f.Percent)} {f.Amount.ToEggString()} ({Math.Round(f.Percent)}%)")));
            }

            var components = new ComponentBuilder().WithButton("Ship DM Settings", $"SRDMenu:{m.UserId}");
            if(!string.IsNullOrEmpty(m.SiteBaseUrl)) {
                components.WithButton("View on site", style: ButtonStyle.Link,
                    url: $"{m.SiteBaseUrl.TrimEnd('/')}/MyFarms#shipsfarms{m.AccountIndex}");
            }

            return (embed.Build(), components.Build());
        }

        public static ShipReturnDmModel SampleModel(ulong userId, List<DBCustomEgg> dbEggs, string siteBaseUrl) {
            var now = DateTimeOffset.UtcNow;
            var fuel = new List<FuelLine> {
                new(EggIncStatics.GetEggById(Ei.Egg.Dilithium, null, dbEggs).emoji, 6e12, 75),
                new(EggIncStatics.GetEggById(Ei.Egg.Antimatter, null, dbEggs).emoji, 4e12, 50),
                new(EggIncStatics.GetEggById(Ei.Egg.DarkMatter, null, dbEggs).emoji, 2e12, 25)
            };
            return new ShipReturnDmModel(
                Ship: Spaceship.Atreggies,
                ReturnUnix: now.AddMinutes(5).ToUnixTimeSeconds(),
                LastBackupUnix: now.AddMinutes(-3).ToUnixTimeSeconds(),
                NeedsFuel: true,
                HasReturned: false,
                AccountName: "Sample",
                MultiAccount: false,
                FuelTank: fuel,
                UserId: userId,
                AccountIndex: 0,
                SiteBaseUrl: siteBaseUrl);
        }

        private static string Bar(double percent) {
            var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 10);
            return new string('█', filled) + new string('░', 10 - filled);
        }
    }
}
