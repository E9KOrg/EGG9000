using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.ArtifactImaging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static EGG9000.Common.Helpers.ArtifactHelpers;

namespace EGG9000.Site.Services {
    // Draws artifact images and, in the same pass, records where every artifact landed so the site can
    // overlay hover targets on top of them. Two shapes are produced from one shared cell painter:
    //   - the full inventory grid (MyFarms Inventory tab + the bot's image endpoint)
    //   - a single 4-slot active-artifact set (the Ships & Farms cards)
    // Building the image and the hotspot manifest in the same loop guarantees the hover targets line up
    // with what was actually painted.
    public sealed class ArtifactImageRenderer(IWebHostEnvironment env) {
        private readonly IWebHostEnvironment _env = env;

        private const string BackgroundHex = "#242422";
        // Background for the best (highest-rate) set so the winning combo reads as green at a glance.
        private const string BestBackgroundHex = "#274629";

        public sealed class RenderResult {
            public byte[] Jpeg { get; init; }
            public ArtifactOverlayManifest Manifest { get; init; }
            public string Error { get; init; }
            public bool Ok => Error is null;
        }

        // Picks which candidate set wins (highest rate) so the page never decides the "best" itself - it
        // asks the renderer, then renders that one set with highlightBest: true. Returns -1 when there are
        // no candidates. Ties go to the first occurrence.
        public static int BestSetIndex(IReadOnlyList<double> rates) {
            if(rates is null || rates.Count == 0) return -1;
            var bestIndex = 0;
            for(var i = 1; i < rates.Count; i++) {
                if(rates[i] > rates[bestIndex]) bestIndex = i;
            }
            return bestIndex;
        }

        // Convenience overload for the site: picks a near-square grid the same way the bot's InventoryB64
        // helper does, so the on-site image matches the Discord one.
        public RenderResult RenderInventory(EggIncAccount account) {
            var orderedList = GetOrderedInventory(account);
            if(orderedList is null || orderedList.Count == 0) return new RenderResult { Error = "No artifacts to display." };
            var (rows, columns) = FindClosestGridSize(orderedList.Count);
            return RenderInventory(account, new InventoryCreatorConfig(100, 30, rows, columns));
        }

        public RenderResult RenderInventory(EggIncAccount account, InventoryCreatorConfig config) {
            if(config is null) return new RenderResult { Error = "Config was null." };
            if(!config.IsValid(out var configError)) return new RenderResult { Error = configError };

            var orderedList = GetOrderedInventory(account);
            if(orderedList is null) return new RenderResult { Error = "Could not read the artifact inventory." };

            var fontPath = WWWPath("Always Together.otf");
            if(fontPath is null) return new RenderResult { Error = "`Always Together.otf` could not be found." };
            var font = new FontCollection().Add(fontPath).CreateFont(config.TextFontSize, FontStyle.Bold);

            using var baseImage = new Image<Rgba32>(config.TotalWidth, config.TotalHeight);
            baseImage.Mutate(x => x.Fill(Color.ParseHex(BackgroundHex)));

            var manifest = new ArtifactOverlayManifest { Width = config.TotalWidth, Height = config.TotalHeight };

            var index = 0;
            foreach(var groupedAf in orderedList) {
                var artifact = groupedAf.Artifact;
                var afCount = groupedAf.Count;
                var (x, y) = GetPositionInGrid(index, config.Rows, config.Columns, config.AFSize, config.Padding);

                // Stacked duplicates (count > 1) are stoneless, so a count badge takes the corner instead
                // of stone icons. PaintCell still records the hotspot with the right copy count.
                var drawStones = afCount == 1;
                if(PaintCell(baseImage, manifest, artifact, afCount, x, y, config.AFSize, config.Padding, config.StoneSize, config.AFCornerRadius, drawStones) && afCount != 1) {
                    DrawCountBadge(baseImage, font, afCount, x, y, config);
                }
                index++;
            }

            return new RenderResult { Jpeg = Encode(baseImage), Manifest = manifest };
        }

        // A single farm's active artifacts as one row of slots, with the same hover targets. Used inline by
        // the Ships & Farms cards in place of the old text list.
        public RenderResult RenderSet(IReadOnlyList<EggIncArtifactInstance> artifacts, bool highlightBest = false) {
            var slots = artifacts?.Where(a => a is not null).ToList() ?? new List<EggIncArtifactInstance>();
            if(slots.Count == 0) return new RenderResult { Error = "No artifacts." };

            const int afSize = 100;
            const int padding = 20;
            var stoneSize = (int)(afSize / 4.5);
            var cornerRadius = afSize / 4;

            var width = (slots.Count * afSize) + (padding * (slots.Count + 1));
            var height = afSize + (padding * 2);

            using var baseImage = new Image<Rgba32>(width, height);
            baseImage.Mutate(x => x.Fill(Color.ParseHex(highlightBest ? BestBackgroundHex : BackgroundHex)));

            var manifest = new ArtifactOverlayManifest { Width = width, Height = height };

            for(var i = 0; i < slots.Count; i++) {
                var x = padding + i * (afSize + padding);
                PaintCell(baseImage, manifest, slots[i], 1, x, padding, afSize, padding, stoneSize, cornerRadius, drawStones: true);
            }

            return new RenderResult { Jpeg = Encode(baseImage), Manifest = manifest };
        }

        // Paints one artifact cell (rarity background + artifact, plus stone icons in the corner) and
        // records its hover target. Returns false when the artifact sprite is missing so callers can skip
        // any follow-up drawing for that cell.
        private bool PaintCell(Image<Rgba32> baseImage, ArtifactOverlayManifest manifest, EggIncArtifactInstance artifact, int count, int x, int y, int afSize, int padding, int stoneSize, int cornerRadius, bool drawStones) {
            var isFrag = artifact.Artifact.Contains("FRAGMENT", StringComparison.CurrentCultureIgnoreCase);
            var afName = artifact.Artifact.ToString().ToUpper().Replace(" ", "_").Replace("'", "").Replace("_FRAGMENT", "");
            var afTier = isFrag ? 1 : (afName.Contains("_STONE") ? artifact.Tier + 1 : artifact.Tier);

            var afImagePath = WWWPath("images", "artifacts", afName, $"{afName}_{afTier}.png");
            if(afImagePath is null) return false;
            using var afImage = Image.Load(afImagePath);
            afImage.Mutate(i => i.Resize(new Size(afSize, afSize)));

            // One hotspot per cell. Its tooltip lists the artifact and any slotted stones, so the small
            // stone icons drawn in the corner don't need separate hover targets.
            manifest.Hotspots.Add(MakeHotspot(x, y, afSize, afSize, manifest, ArtifactDisplay.TooltipHtml(artifact, count)));

            using var backgroundImage = BackgroundImage(RarityColor(artifact.Rarity), afSize, cornerRadius);
            backgroundImage.Mutate(i => i.DrawImage(afImage, new Point(0, 0), 1f));
            baseImage.Mutate(b => b.DrawImage(backgroundImage, new Point(x, y), 1f));

            if(drawStones) {
                var stoneIndex = 1;
                foreach(var stone in artifact.Stones ?? new List<EggIncArtifactInstance>()) {
                    var stoneName = stone.Artifact.ToString().ToUpper().Replace(" ", "_");
                    var stonePath = WWWPath("images", "artifacts", stoneName, $"{stoneName}_{stone.Tier + 1}.png");
                    if(stonePath is null) continue;
                    using var stoneImage = Image.Load(stonePath);
                    stoneImage.Mutate(i => i.Resize(new Size(stoneSize, stoneSize), true));
                    var sx = x + afSize - (int)(padding * 0.5) - (stoneSize * stoneIndex);
                    var sy = (int)(y + afSize - (padding * 1.5));
                    baseImage.Mutate(b => b.DrawImage(stoneImage, new Point(sx, sy), 1f));
                    stoneIndex++;
                }
            }
            return true;
        }

        private static byte[] Encode(Image<Rgba32> image) {
            using var ms = new MemoryStream();
            image.Save(ms, new JpegEncoder());
            return ms.ToArray();
        }

        private static void DrawCountBadge(Image<Rgba32> baseImage, Font font, int afCount, int x, int y, InventoryCreatorConfig config) {
            var text = afCount.ToString();
            var textWidth = Math.Max(config.TextHeight, (text.Length * config.TextBaseWidth) + config.TextBaseWidth);
            using var textImage = new Image<Rgba32>(textWidth, config.TextHeight);
            textImage.Mutate(c => c
                .Fill(Color.ParseHex("#4f4f4f"))
                .Fill(Color.Transparent, new SixLabors.ImageSharp.Drawing.RectangularPolygon(config.TextCornerRadius, config.TextCornerRadius, textWidth - config.TextCornerRadius, config.TextCornerRadius)));
            textImage.Mutate(c => c.ApplyRoundedCorners(config.TextCornerRadius));
            var center = new PointF(textImage.Width / 2f, textImage.Height / 2f);
            var measured = TextMeasurer.MeasureSize(text, new TextOptions(font));
            textImage.Mutate(c => c.DrawText(text, font, Color.White, new PointF(center.X - measured.Width / 2, center.Y - measured.Height / 2)));

            var baseCenter = new Point(x + config.AFSize, y + config.AFSize);
            var textPosition = new Point(baseCenter.X - (int)(textImage.Width / 1.5), baseCenter.Y - (int)(textImage.Height / 1.5));
            baseImage.Mutate(b => b.DrawImage(textImage, textPosition, 1f));
        }

        private static Color RarityColor(int rarity) => rarity switch {
            1 => Color.ParseHex("#383834"),
            2 => Color.ParseHex("#6cb6d9"),
            3 => Color.ParseHex("#b72de0"),
            4 => Color.ParseHex("#f2d61b"),
            _ => Color.ParseHex("#383834")
        };

        // Pixel rect -> percentage-of-image hotspot.
        private static ArtifactHotspot MakeHotspot(int x, int y, int w, int h, ArtifactOverlayManifest manifest, string tip) => new() {
            X = Round(x * 100.0 / manifest.Width),
            Y = Round(y * 100.0 / manifest.Height),
            W = Round(w * 100.0 / manifest.Width),
            H = Round(h * 100.0 / manifest.Height),
            Tip = tip
        };

        private static double Round(double v) => Math.Round(v, 3);

        private string WWWPath(params string[] parts) {
            var path = _env.WebRootPath;
            if(parts is { Length: > 0 }) path = System.IO.Path.Combine(path, System.IO.Path.Combine(parts));
            return System.IO.File.Exists(path) ? path : null;
        }
    }
}
