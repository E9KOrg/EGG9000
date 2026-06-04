using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.AfxSets;
using static EGG9000.Common.Helpers.ArtifactHelpers;

namespace EGG9000.Site.Controllers {

    public class APIController(ApplicationDbContext db, Bugsnag.IClient bugsnag, IServiceProvider provider, ILogger<APIController> logger, IWebHostEnvironment env) : Controller {
        private readonly ApplicationDbContext _db = db;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly IServiceProvider _provider = provider;
        private readonly ILogger<APIController> _logger = logger;
        private readonly IWebHostEnvironment _env = env;

        private string GetWWWRelativePath(List<string> relativePathJoins) {
            var imageDur = _env.WebRootPath;
            // If there are additional path segments, combine them with the root path
            if(relativePathJoins != null && relativePathJoins.Count > 0) {
                imageDur = System.IO.Path.Combine(imageDur, System.IO.Path.Combine(relativePathJoins.ToArray()));
            }
            // Return the final path
            return System.IO.File.Exists(imageDur) ? imageDur : null;
        }

        [HttpPost]
        [Route("api/generateeventimage")]
        public IActionResult GenerateEventImage([FromHeader] string authenticationKey, [FromBody] Event customEvent) {
#if RELEASE
            if(string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken) {
                return NotFound();
            }
#endif 
            var imagePath = GetWWWRelativePath([
                "images/events",
                $"event_{customEvent.Type.ToLowerInvariant().Replace("-", "_")}.png"
            ]);
            if(imagePath == null) {
                Console.WriteLine("IMAGE PATH NOT FOUND!");
                return NotFound(new { message = $"Image for event type '{customEvent.Type}' not found." });
            }

            var baseImage = Image.Load(imagePath);
            var backgroundColor = customEvent.Type.ToLower() switch {
                "epic-research-sale" => "#ef4444",
                "piggy-boost" => "#f97316",
                "piggy-cap-boost" => "#f59e0b",
                "prestige-boost" => "#f59e0b",
                "earnings-boost" => "#84cc16",
                "gift-boost" => "#10b981",
                "drone-boost" => "#10b981",
                "research-sale" => "#14b8a6",
                "hab-sale" => "#06b6d4",
                "vehicle-sale" => "#0ea5e9",
                "boost-sale" => "#3b82f6",
                "boost-duration" => "#6366f1",
                "crafting-sale" => "#8b5cf6",
                "mission-fuel" => "#8b5cf6",
                "mission-capacity" => "#d946ef",
                "mission-duration" => "#ec4899",
                "shell-sale" => "#f43f5e",
                _ => "#9ca3af"
            };

            // Convert hex color to a Color object
            var bgColor = Color.ParseHex(backgroundColor);

            // Calculate new dimensions (110% of original for padding)
            var newWidth = (int)(baseImage.Width * 1.1);
            var newHeight = (int)(baseImage.Height * 1.1);

            // Create a new image with the new dimensions and the background color
            var newImage = new Image<Rgba32>(newWidth, newHeight);

            // If the event is ULTRA-only, use a color gradient from #f5a709 (left) to 900fb1 (right)
            if(customEvent.CcOnly) {
                // Define the gradient colors
                var leftColor = Color.ParseHex("#f5a709");
                var rightColor = Color.ParseHex("#900fb1");

                // Create a linear gradient brush from left to right
                var gradientBrush = new LinearGradientBrush(
                    new PointF(0, 0),             // Start point (left side)
                    new PointF(newWidth, 0),      // End point (right side)
                    GradientRepetitionMode.None,  // No repetition of gradient
                    new ColorStop(0, leftColor),  // Start with leftColor at 0%
                    new ColorStop(1, rightColor)  // End with rightColor at 100%
                );

                // Apply the gradient fill to the new image
                newImage.Mutate(ctx => ctx.Fill(gradientBrush));
            } else {
                newImage.Mutate(ctx => ctx.Fill(bgColor));
            }

            // Center the original image onto the new canvas
            var xPos = (newWidth - baseImage.Width) / 2;
            var yPos = (newHeight - baseImage.Height) / 2;
            newImage.Mutate(ctx => ctx.DrawImage(baseImage, new Point(xPos, yPos), 1f));

            using var ms = new MemoryStream();
            newImage.Save(ms, new PngEncoder());
            return File(ms.ToArray(), "image/png");
        }

        [HttpPost]
        [Route("api/generateinventoryb64")]
        public async Task<IActionResult> GenerateInventoryB64([FromHeader] string authenticationKey, [FromBody] InventoryAPIObject userObject) {
#if RELEASE
             if(string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken) return NotFound();
#endif
            var user = await _db.DBUsers.FirstOrDefaultAsync(u => u.EIDs.Contains(userObject.EID));
            if(user == null) {
                return BadRequest(new { message = $"User with EID {userObject.EID} was not found." });
            }
            var account = user.EggIncAccounts.FirstOrDefault(a => a.Id == userObject.EID);
            if(account == null) {
                return BadRequest(new { message = $"Account with EID {userObject.EID} was not found for the user {user.DiscordUsername}" });
            }

            var config = userObject.Config;
            if(config is null) return BadRequest(new { message = "Config was null." });
            if(!config.IsValid(out var configError)) return BadRequest(new { message = configError });
            var rows = config.Rows;
            var columns = config.Columns;

            var orderedList = GetOrderedInventory(account);
            if(orderedList is null) return BadRequest(new { message = $"`orderedList` was null." });

            var baseImage = new Image<Rgba32>(config.TotalWidth, config.TotalHeight);
            baseImage.Mutate(x => x.Fill(Color.ParseHex("#242422")));

            var fontFilePath = GetWWWRelativePath(["Always Together.otf"]);
            if(fontFilePath == null) return BadRequest(new { message = $"`Always Together.otf` could not be found." });
            var font = new FontCollection().Add(fontFilePath).CreateFont(config.TextFontSize, FontStyle.Bold);

            var index = 0;
            foreach(var groupedAf in orderedList) {
                var isFrag = groupedAf.Artifact.Artifact.ToString().ToUpper().Contains("FRAGMENT");
                var afName = groupedAf.Artifact.Artifact.ToString().ToUpper().Replace(" ", "_").Replace("'", "").Replace("_FRAGMENT", "");
                var afTier = isFrag ? 1 : (afName.Contains("_STONE") ? groupedAf.Artifact.Tier + 1 : groupedAf.Artifact.Tier);
                var afCount = groupedAf.Count;
                var afStones = groupedAf.Artifact.Stones;

                var (x, y) = GetPositionInGrid(index, rows, columns, config.AFSize, config.Padding);

                var backgroundColor = groupedAf.Artifact.Rarity switch {
                    1 => Color.ParseHex("#383834"),
                    2 => Color.ParseHex("#6cb6d9"),
                    3 => Color.ParseHex("#b72de0"),
                    4 => Color.ParseHex("#f2d61b"),
                    _ => Color.ParseHex("#383834")
                };

                var afImagePath = GetWWWRelativePath([
                    "images/artifacts",
                    afName,
                    $"{afName}_{afTier}.png"
                ]);
                if(afImagePath == null) continue;
                using var afImage = Image.Load(afImagePath);

                afImage.Mutate(i => { i.Resize(new Size(config.AFSize, config.AFSize)); });

                var stoneImages = new List<Image>();
                foreach(var stone in afStones) {
                    var stoneName = stone.Artifact.ToString().ToUpper().Replace(" ", "_");
                    var stoneTier = stone.Tier + 1;
                    var stonePath = GetWWWRelativePath([
                        "images/artifacts",
                        stoneName,
                        $"{stoneName}_{stoneTier}.png"
                    ]);
                    if(stonePath == null) continue;
                    var stoneImage = Image.Load(stonePath);
                    stoneImage.Mutate(i => { i.Resize(new Size(config.StoneSize, config.StoneSize), true); });
                    stoneImages.Add(stoneImage);
                }

                Image textImage = null;
                if(afCount != 1) {
                    var textWidth = Math.Max(config.TextHeight, (afCount.ToString().Length * config.TextBaseWidth) + config.TextBaseWidth);
                    textImage = new Image<Rgba32>(textWidth, config.TextHeight);
                    textImage.Mutate(x => x
                        .Fill(Color.ParseHex("#4f4f4f")) // Fill the image with a background color
                        .Fill(Color.Transparent, new RectangularPolygon(config.TextCornerRadius, config.TextCornerRadius, textWidth - config.TextCornerRadius, config.TextCornerRadius))); // Create a transparent rectangle with rounded corners
                    textImage.Mutate(x => x.ApplyRoundedCorners(config.TextCornerRadius));
                    var text = afCount.ToString();
                    var center = new PointF(textImage.Width / 2, textImage.Height / 2);
                    var measured = TextMeasurer.MeasureSize(text, new TextOptions(font));
                    var textPosition = new PointF(center.X - measured.Width / 2, center.Y - measured.Height / 2);

                    textImage.Mutate(x => x.DrawText(text, font, Color.White, textPosition));
                }

                using var backgroundImage = BackgroundImage(backgroundColor, config.AFSize, config.AFCornerRadius);
                backgroundImage.Mutate(i => { i.DrawImage(afImage, new Point(0, 0), 1f); });

                baseImage.Mutate(b => { b.DrawImage(backgroundImage, new Point(x, y), 1f); });
                if(textImage != null) {
                    var baseCenter = new Point(x + backgroundImage.Width, y + backgroundImage.Height);
                    var textPosition = new Point(baseCenter.X - (int)(textImage.Width / 1.5), baseCenter.Y - (int)(textImage.Height / 1.5));
                    baseImage.Mutate(b => { b.DrawImage(textImage, textPosition, 1f); });
                } else if(stoneImages.Count > 0) {
                    var stoneIndex = 1;
                    foreach(var stoneImage in stoneImages) {
                        baseImage.Mutate(b => { b.DrawImage(stoneImage, new Point(x + config.AFSize - (int)(config.Padding * 0.5) - (config.StoneSize * stoneIndex), (int)(y + config.AFSize - (config.Padding * 1.5))), 1f); });
                        stoneIndex++;
                    }
                }
                index++;
            }

            using var ms = new MemoryStream();
            baseImage.Save(ms, new JpegEncoder());
            return File(ms.ToArray(), "image/jpeg");
        }

        [HttpPost]
        [Route("api/generateafxsetsb64")]
        public async Task<IActionResult> GenerateAfxSetsB64([FromHeader] string authenticationKey, [FromBody] AfxSetsAPIObject userObject) {
#if RELEASE
             if(string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken) return NotFound();
#endif
            if(userObject is null || string.IsNullOrWhiteSpace(userObject.EID) || userObject.Config is null) return BadRequest(new { message = "Invalid request body." });

            var user = await _db.DBUsers.FirstOrDefaultAsync(u => u.EIDs.Contains(userObject.EID));
            if(user == null) return BadRequest(new { message = $"User with EID {userObject.EID} was not found." });
            var account = user.EggIncAccounts.FirstOrDefault(a => a.Id == userObject.EID);
            if(account == null) return BadRequest(new { message = $"Account with EID {userObject.EID} was not found." });

            var sets = account.Backup?.ArtifactSets;
            if(sets is null || sets.Count == 0) return BadRequest(new { message = "No artifact sets." });

            var config = userObject.Config;
            if(!config.IsValid(out var configError)) return BadRequest(new { message = configError });

            var fontFilePath = GetWWWRelativePath(["Always Together.otf"]);
            if(fontFilePath == null) return BadRequest(new { message = "`Always Together.otf` could not be found." });
            var font = new FontCollection().Add(fontFilePath).CreateFont(config.TextFontSize, FontStyle.Bold);

            var pages = new List<string>();
            for(var pageStart = 0; pageStart < sets.Count; pageStart += config.SetsPerPage) {
                var pageSets = sets.Skip(pageStart).Take(config.SetsPerPage).ToList();
                var rowCount = pageSets.Count;

                var width = config.LabelWidth + (config.SlotsPerRow * config.AFSize) + (config.Padding * (config.SlotsPerRow + 1));
                var height = (rowCount * config.AFSize) + (config.Padding * (rowCount + 1));
                using var pageImage = new Image<Rgba32>(width, height);
                pageImage.Mutate(x => x.Fill(Color.ParseHex("#242422")));

                for(var r = 0; r < rowCount; r++) {
                    var set = pageSets[r];
                    var rowY = config.Padding + r * (config.AFSize + config.Padding);

                    // "Set N" label (global index, 1-based)
                    var label = $"Set {pageStart + r + 1}";
                    pageImage.Mutate(x => x.DrawText(label, font, Color.White, new PointF(config.Padding, rowY + config.AFSize / 2f - config.TextFontSize / 2f)));

                    if(set.Count == 0) {
                        pageImage.Mutate(x => x.DrawText("(empty)", font, Color.ParseHex("#8a8a86"), new PointF(config.LabelWidth + config.Padding, rowY + config.AFSize / 2f - config.TextFontSize / 2f)));
                        continue;
                    }

                    for(var c = 0; c < set.Count && c < config.SlotsPerRow; c++) {
                        var inst = set[c];
                        var cellX = config.LabelWidth + config.Padding * (c + 1) + c * config.AFSize;

                        var isFrag = inst.Artifact.ToString().Contains("FRAGMENT", StringComparison.CurrentCultureIgnoreCase);
                        var afName = inst.Artifact.ToString().ToUpper().Replace(" ", "_").Replace("'", "").Replace("_FRAGMENT", "");
                        var afTier = isFrag ? 1 : (afName.Contains("_STONE") ? inst.Tier + 1 : inst.Tier);

                        var bg = inst.Rarity switch {
                            1 => Color.ParseHex("#383834"),
                            2 => Color.ParseHex("#6cb6d9"),
                            3 => Color.ParseHex("#b72de0"),
                            4 => Color.ParseHex("#f2d61b"),
                            _ => Color.ParseHex("#383834")
                        };

                        var afImagePath = GetWWWRelativePath(["images/artifacts", afName, $"{afName}_{afTier}.png"]);
                        if(afImagePath == null) continue;
                        using var afImage = Image.Load(afImagePath);
                        afImage.Mutate(i => i.Resize(new Size(config.AFSize, config.AFSize)));

                        using var background = BackgroundImage(bg, config.AFSize, config.AFCornerRadius);
                        background.Mutate(i => i.DrawImage(afImage, new Point(0, 0), 1f));
                        pageImage.Mutate(b => b.DrawImage(background, new Point(cellX, rowY), 1f));

                        var stoneIndex = 1;
                        foreach(var stone in inst.Stones ?? new()) {
                            var stoneName = stone.Artifact.ToString().ToUpper().Replace(" ", "_");
                            var stonePath = GetWWWRelativePath(["images/artifacts", stoneName, $"{stoneName}_{stone.Tier + 1}.png"]);
                            if(stonePath == null) continue;
                            using var stoneImage = Image.Load(stonePath);
                            stoneImage.Mutate(i => i.Resize(new Size(config.StoneSize, config.StoneSize), true));
                            pageImage.Mutate(b => b.DrawImage(stoneImage, new Point(cellX + config.AFSize - (int)(config.Padding * 0.5) - (config.StoneSize * stoneIndex), rowY + config.AFSize - (int)(config.Padding * 1.5)), 1f));
                            stoneIndex++;
                        }
                    }
                }

                using var ms = new MemoryStream();
                pageImage.Save(ms, new JpegEncoder());
                pages.Add(Convert.ToBase64String(ms.ToArray()));
            }

            return Ok(new AfxSetsB64Response { Pages = pages });
        }
    }
}
