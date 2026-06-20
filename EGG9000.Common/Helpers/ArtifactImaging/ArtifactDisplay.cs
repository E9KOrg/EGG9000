using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace EGG9000.Common.Helpers.ArtifactImaging {
    // Turns an artifact (or stone) instance into the bits we show on hover: its tier name, rarity, and the
    // in-game effect line, in the same shape the game's artifact card uses. The combos table and the
    // inventory overlay both read from here so labels stay consistent across the site.
    //
    // Two flavours of output:
    //   Title()       - plain text, e.g. "Glistening Beak Of Midas +500%". Kept for any non-HTML caller.
    //   TooltipHtml() - rich multi-line HTML for the JS tooltip: bold name, colour-coded rarity, a green
    //                   value, and one line per slotted stone (same-type stones grouped as "(x3)").
    public static class ArtifactDisplay {
        private static readonly TextInfo _titleCase = new CultureInfo("en-US", false).TextInfo;

        public static string RarityName(int rarity) => rarity switch {
            1 => "Common",
            2 => "Rare",
            3 => "Epic",
            4 => "Legendary",
            _ => "Common"
        };

        // The short value string the game shows, e.g. "+20%", "Guaranteed". Comes from the artifact data
        // when available; otherwise we fall back to deriving it from the instance's boost value so combos
        // suggestions (which can carry synthetic instances) still read sensibly.
        public static string Effect(EggIncArtifactInstance artifact) {
            if(artifact is null || IsFragment(artifact)) return "";
            var effect = SafeEffect(artifact);
            if(effect.HasValue && !string.IsNullOrWhiteSpace(effect.Value.Size)) return FormatEffectSize(effect.Value.Size, artifact);
            return DerivedValue(artifact);
        }

        // Plain-text label: tier name plus value. Title-cased so it reads cleanly.
        public static string Title(EggIncArtifactInstance artifact) {
            if(artifact is null) return "";
            var name = _titleCase.ToTitleCase(SafeName(artifact));
            var value = Effect(artifact);
            return string.IsNullOrEmpty(value) ? name : $"{name} {value}";
        }

        // Rich tooltip HTML for one artifact and (optionally) its stones. count is how many copies this
        // tile represents - shown as "x N" when greater than one.
        public static string TooltipHtml(EggIncArtifactInstance artifact, int count = 1) {
            if(artifact is null) return "";
            var sb = new StringBuilder();

            sb.Append("<div class=\"afx-tip-title\">");
            sb.Append("<span class=\"afx-tip-name\">").Append(Encode(_titleCase.ToTitleCase(SafeName(artifact)))).Append("</span>");
            sb.Append(" <span class=\"afx-tip-tier\">(T").Append(SafeTier(artifact)).Append(")</span>");
            sb.Append(' ').Append(RarityHtml(artifact.Rarity));
            if(count > 1) sb.Append(" <span class=\"afx-tip-count\">x ").Append(count).Append("</span>");
            sb.Append("</div>");

            var effectLine = EffectLineHtml(artifact);
            if(effectLine is not null) sb.Append("<div class=\"afx-tip-effect\">").Append(effectLine).Append("</div>");

            foreach(var stoneLine in StoneLinesHtml(artifact)) {
                sb.Append("<div class=\"afx-tip-stone\">").Append(stoneLine).Append("</div>");
            }

            return sb.ToString();
        }

        // The effect sentence with the value coloured: "<green>+20%</green> chance of gold in gifts...".
        private static string EffectLineHtml(EggIncArtifactInstance artifact) {
            if(IsFragment(artifact)) return null;
            var effect = SafeEffect(artifact);
            if(effect.HasValue && !string.IsNullOrWhiteSpace(effect.Value.Target)) {
                var size = FormatEffectSize(effect.Value.Size, artifact);
                if(string.IsNullOrWhiteSpace(size) || IsZeroEffect(size)) return Encode(effect.Value.Target);
                return $"<span class=\"afx-tip-value\">{Encode(size)}</span> {Encode(effect.Value.Target)}";
            }
            // No data-backed sentence (e.g. a synthetic combos instance): show just the derived value.
            var derived = DerivedValue(artifact);
            return string.IsNullOrEmpty(derived) ? null : $"<span class=\"afx-tip-value\">{Encode(derived)}</span>";
        }

        // Converts large percentage strings from JSON (e.g. "+1100%") to multiplier format ("12x"),
        // matching the same threshold DerivedValue uses.
        private static string FormatEffectSize(string jsonSize, EggIncArtifactInstance artifact) {
            if(artifact is null || artifact.Additive ||
               artifact.Boost == JsonData.EiStatics.EggIncBoostTypeEnum.HostArtifactsOnElightenment)
                return jsonSize ?? "";
            if(artifact.Value >= 2) return $"{artifact.Value}x";
            return jsonSize ?? "";
        }

        private static bool IsZeroEffect(string size) {
            if(string.IsNullOrWhiteSpace(size)) return false;
            var trimmed = size.Trim().TrimStart('+', '-').TrimEnd('%', 'x');
            return double.TryParse(trimmed, out var val) && val == 0;
        }

        // One line per distinct stone slotted into the artifact. Identical stones collapse into a single
        // line tagged with their count, e.g. "(x3) +5% egg laying rate".
        private static IEnumerable<string> StoneLinesHtml(EggIncArtifactInstance artifact) {
            if(artifact.Stones is null || artifact.Stones.Count == 0) yield break;

            // Group by identity (id + tier + rarity) while keeping the order stones were slotted in.
            var groups = new List<(EggIncArtifactInstance Stone, int Count)>();
            foreach(var stone in artifact.Stones) {
                var existing = groups.FindIndex(g => g.Stone.Id == stone.Id && g.Stone.Tier == stone.Tier && g.Stone.Rarity == stone.Rarity);
                if(existing >= 0) groups[existing] = (groups[existing].Stone, groups[existing].Count + 1);
                else groups.Add((stone, 1));
            }

            foreach(var (stone, groupCount) in groups) {
                var prefix = groupCount > 1 ? $"<span class=\"afx-tip-count\">(x{groupCount})</span> " : "";
                var effect = SafeEffect(stone);
                if(effect.HasValue && !string.IsNullOrWhiteSpace(effect.Value.Target)) {
                    var size = FormatEffectSize(effect.Value.Size, stone);
                    if(string.IsNullOrWhiteSpace(size) || IsZeroEffect(size))
                        yield return $"{prefix}{Encode(effect.Value.Target)}";
                    else
                        yield return $"{prefix}<span class=\"afx-tip-value\">{Encode(size)}</span> {Encode(effect.Value.Target)}";
                } else {
                    yield return $"{prefix}{Encode(_titleCase.ToTitleCase(SafeName(stone)))}";
                }
            }
        }

        private static string RarityHtml(int rarity) =>
            $"<span class=\"afx-tip-rarity afx-rarity-{rarity}\">{Encode(RarityName(rarity))}</span>";

        private static bool IsFragment(EggIncArtifactInstance artifact) =>
            artifact?.Artifact?.Contains("Fragment", StringComparison.OrdinalIgnoreCase) == true;

        // Derived value string for instances the data file can't resolve. Mirrors the old combos
        // artifactTemplate() so those suggestions keep their familiar "+50%" / "2x" / "+3" look.
        private static string DerivedValue(EggIncArtifactInstance artifact) {
            if(artifact is null) return "";
            if(artifact.Boost == JsonData.EiStatics.EggIncBoostTypeEnum.HostArtifactsOnElightenment) {
                var pct = Math.Round(artifact.Value * 100);
                return pct == 0 ? "" : $"{pct}%";
            }
            if(artifact.Additive) return artifact.Value == 0 ? "" : $"+{artifact.Value}";
            if(artifact.Value < 2) {
                var pct = Math.Round((artifact.Value - 1) * 100);
                return pct == 0 ? "" : $"+{pct}%";
            }
            return $"{artifact.Value}x";
        }

        private static (string Size, string Target)? SafeEffect(EggIncArtifactInstance artifact) {
            try { return EggIncArtifacts.GetEffectDisplay(artifact); }
            catch { return null; }
        }

        private static int SafeTier(EggIncArtifactInstance artifact) {
            try { return EggIncArtifacts.GetDisplayTier(artifact); }
            catch { return Math.Max(1, (int)artifact.Tier); }
        }

        private static string SafeName(EggIncArtifactInstance artifact) {
            // GetProperNameFromJson is what the combos table has always used, so try it first to keep
            // those labels unchanged. It throws on fragments, and GetTierName knows how to strip the
            // "Fragment" suffix, so that's the fallback.
            try {
                return EggIncArtifacts.GetProperNameFromJson(artifact);
            } catch {
                try {
                    return EggIncArtifacts.GetTierName(artifact);
                } catch {
                    return artifact.Artifact ?? "Unknown";
                }
            }
        }

        private static string Encode(string s) => WebUtility.HtmlEncode(s ?? "");
    }
}
