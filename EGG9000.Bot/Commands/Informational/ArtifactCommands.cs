using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using EGG9000.Common.Commands;
using EGG9000.Common.Services;
using System.IO;
using SixLabors.ImageSharp.Formats.Png;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Discord.WebSocket;
using EGG9000.Common.Helpers;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp.Drawing;
using SixLabors.Fonts;

namespace EGG9000.Bot.Commands {
    public static class ArtifactCommands {

        // This method can be seen as an inline implementation of an `IImageProcessor`:
        // (The combination of `IImageOperations.Apply()` + this could be replaced with an `IImageProcessor`)
        private static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context, float cornerRadius) {
            var size = context.GetCurrentSize();
            var corners = BuildCorners(size.Width, size.Height, cornerRadius);

            context.SetGraphicsOptions(new GraphicsOptions() {
                Antialias = true,

                // Enforces that any part of this shape that has color is punched out of the background
                AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
            });

            // Mutating in here as we already have a cloned original
            // use any color (not Transparent), so the corners will be clipped
            foreach(var path in corners) {
                context = context.Fill(Color.Red, path);
            }

            return context;
        }

        private static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius) {
            // First create a square
            var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

            // Then cut out of the square a circle so we are left with a corner
            IPath cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

            // Corner is now a corner shape positions top left
            // let's make 3 more positioned correctly, we can do that by translating the original around the center of the image.

            float rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
            float bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

            // Move it across the width of the image - the width of the shape
            IPath cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
            IPath cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
            IPath cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

            return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
        }

        [SlashCommand(Description = "View a user's inventory", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task ViewInventory(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, [SlashParam(Required = false)] bool showinchannel = false) {
            await command.RespondAsync("Getting backups...", ephemeral: !showinchannel);
            var userid = useraccount.Split("|")[0];
            if(userid is null) await command.ModifyOriginalResponseAsync($"⚠︎ Error: User id could not be found from param");
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbuser is null) await command.ModifyOriginalResponseAsync($"⚠︎ Error: DB user could not be found from user ID {userid}");
            var account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])];
            if(account is null) await command.RespondAsync($"⚠︎ Error: User account for {userid} could not be found");

            await command.RespondWithFileAsync(new Discord.FileAttachment(new MemoryStream(Convert.FromBase64String(InventoryB64(account))), "image.png", "Inventory Image"), text: " ");
        }

        [SlashCommand(Description = "View your inventory")]
        public static async Task ViewInventory(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }
            var accountIndex = int.Parse(useraccount.Split("|")[1]);
            var account = user.EggIncAccounts[accountIndex];
            await command.RespondWithFileAsync(new Discord.FileAttachment(new MemoryStream(Convert.FromBase64String(InventoryB64(account))), "image.png", "Inventory Image"), text: " ");
        }

        private static Image BackgroundImage(Color backgroundColor, int size, int radius) {
            var image = new Image<Rgba32>(size, size);
            var graphicsOptions = new GraphicsOptions {
                Antialias = true,
            };

            var cornerRadius = (int)(size * radius);

            image.Mutate(x => x
                .Fill(backgroundColor) // Fill the image with a background color
                .Fill(Color.Transparent, new RectangularPolygon(cornerRadius, cornerRadius, size - cornerRadius, size - cornerRadius))); // Create a transparent rectangle with rounded corners

            image.Mutate(x => x.ApplyRoundedCorners(radius));

            return image;
        }

        private static (int x, int y) GetPositionInGrid(int index, int rows, int columns, int itemSize, int padding) {
            if(index < 0 || index >= rows * columns) {
                throw new ArgumentException("Index is out of range for the given grid size.");
            }
            var column = index / rows;
            var row = index % rows;
            var x = column * (itemSize + padding) + padding;
            var y = row * (itemSize + padding) + padding;
            return (x, y);
        }

        private static string InventoryB64(EggIncAccount account) {

            /*
             * Constants that will determine how the image comes out
             */
            var padding = 30;
            var afSize = 200;
            var stoneSize = (int)(afSize / 4.5);
            var radius = 50;

            var textHeight = 50;
            var textBaseWidth = 24;
            var textRadius = (int)(textHeight / 2.5);
            var textFontSize = 40;
            var textOffset = (int)(textBaseWidth / 2.5);

            var orderedList = account.Backup.ArtifactHall.Where(a => a.Count > 0).ToList().OrderByDescending(i => i.Artifact.Rarity).ToList();
            var rarityGroupedAfs = orderedList.GroupBy(a => a.Artifact.Rarity).ToList();
            orderedList = new List<ArtifactCount>();
            foreach(var rarityGrouping in rarityGroupedAfs) {
                orderedList.AddRange(rarityGrouping.OrderByDescending(g => ArtifactHelpers.GetAFOrder(g.Artifact.Artifact.Replace(" Fragment", "")) + 0.05 * g.Artifact.Tier + 0.01 * g.Artifact.Stones.Count).ToList());
            }
            var skipIndexes = new List<int>();
            foreach(var acount in orderedList) {
                var selfIndex = orderedList.IndexOf(acount);
                if(acount.Artifact.Stones.Count > 0 || skipIndexes.Contains(selfIndex)) continue;

                var others = orderedList.Where(a => a.Artifact.Equals(acount.Artifact) && orderedList.IndexOf(a) != selfIndex).ToList();
                foreach(var other in others) skipIndexes.Add(orderedList.IndexOf(other));
                acount.Count += others.Count;
            }
            Console.WriteLine("SkipIndexes: " + string.Join(",", skipIndexes.Select(s => s.ToString()).ToList()));
            var removed = 0;
            foreach(var skipIndex in skipIndexes) {
                orderedList.RemoveAt(skipIndex - removed);
                removed++;
            }

            var (rows, columns) = FindClosestGridSize(orderedList.Count);

            var totalWidth = (columns * afSize) + (padding * (columns + 1));
            var totalHeight = (rows * afSize) + (padding * (rows + 1));

            var baseImage = new Image<Rgba32>(totalWidth, totalHeight);
            baseImage.Mutate(x => x.Fill(Color.ParseHex("#242422")));

            var index = 0;
            foreach(var groupedAf in orderedList) {

                var isFrag = groupedAf.Artifact.Artifact.ToString().ToUpper().Contains("FRAGMENT");
                var afName = groupedAf.Artifact.Artifact.ToString().ToUpper().Replace(" ", "_").Replace("'", "").Replace("_FRAGMENT", "");
                var afTier = isFrag ? 1 : (afName.Contains("_STONE") ? groupedAf.Artifact.Tier + 1 : groupedAf.Artifact.Tier);
                var afCount = groupedAf.Count;
                var afStones = groupedAf.Artifact.Stones;

                var (x, y) = GetPositionInGrid(index, rows, columns, afSize, padding);

                var backgroundColor = groupedAf.Artifact.Rarity switch {
                    1 => Color.ParseHex("#383834"),
                    2 => Color.ParseHex("#6cb6d9"),
                    3 => Color.ParseHex("#b72de0"),
                    4 => Color.ParseHex("#f2d61b"),
                    _ => Color.ParseHex("#383834")
                };

                var afImage = Image.Load($"../../../../EGG9000.Site\\wwwroot\\images\\artifacts\\{afName}\\{afName}_{afTier}.png");
                afImage.Mutate(i => { i.Resize(new Size(afSize, afSize)); });

                var stoneImages = new List<Image>();
                foreach(var stone in afStones) {
                    var stoneName = stone.Artifact.ToString().ToUpper().Replace(" ", "_");
                    var stoneTier = stone.Tier + 1;
                    var stoneImage = Image.Load($"../../../../EGG9000.Site\\wwwroot\\images\\artifacts\\{stoneName}\\{stoneName}_{stoneTier}.png");
                    stoneImage.Mutate(i => { i.Resize(new Size(stoneSize, stoneSize)); });
                    stoneImages.Add(stoneImage);
                }

                Image textImage = null;
                if(afCount != 1) {
                    var textLength = afCount.ToString().Length;
                    var textWidth = (textLength * textBaseWidth) + textBaseWidth;
                    textImage = new Image<Rgba32>(textWidth, textHeight);
                    textImage.Mutate(x => x
                        .Fill(Color.ParseHex("#121212")) // Fill the image with a background color
                        .Fill(Color.Transparent, new RectangularPolygon(textRadius, textRadius, textWidth - textRadius, textRadius))); // Create a transparent rectangle with rounded corners
                    textImage.Mutate(x => x.ApplyRoundedCorners(textRadius));

                    textImage.Mutate(x => x.DrawText(afCount.ToString(), new Font(SystemFonts.Get("Arial"), textFontSize), Color.White, new Point(textOffset * (textLength > 1 ? textLength / 2 : 1), textOffset)));
                }

                var backgroundImage = BackgroundImage(backgroundColor, afSize, radius);
                backgroundImage.Mutate(i => { i.DrawImage(afImage, new Point(0, 0), 1f); });

                try {
                    baseImage.Mutate(b => { b.DrawImage(backgroundImage, new Point(x, y), 1f); });
                    if(textImage != null) {
                        baseImage.Mutate(b => { b.DrawImage(textImage, new Point(x + afSize - (padding * afCount.ToString().Length), y + afSize - padding), 1f); });
                    } else if(stoneImages.Count > 0) {
                        var stoneIndex = 1;
                        foreach(var stoneImage in stoneImages) {
                            baseImage.Mutate(b => { b.DrawImage(stoneImage, new Point(x + afSize - (int)(padding * 0.5) - (stoneSize * stoneIndex), (int)(y + afSize - (padding * 1.5))), 1f); });
                            stoneIndex++;
                        }
                    }
                } catch(Exception ex) {
                    Console.WriteLine($"Attempting to draw over point: X:{x},Y:{y}");
                }
                index++;
            }

            var b64 = baseImage.ToBase64String(PngFormat.Instance);
            return (b64.Replace("data:image/png;base64,", ""));
        }

        private static (int rows, int columns) FindClosestGridSize(int itemCount) {
            if(itemCount <= 0) {
                throw new ArgumentException("itemCount must be a positive integer.");
            }

            var closestSquareRoot = (int)Math.Floor(Math.Sqrt(itemCount));
            var rows = closestSquareRoot;
            var columns = itemCount / rows;

            while(rows * columns < itemCount) {
                columns++;
            }

            return (rows, columns);
        }

        private class AfxSetBuilder {
            public Discord.ComponentBuilder ComponentBuilder { get; set; }
            public Discord.EmbedBuilder EmbedBuilder { get; set; }
            public AfxSetBuilder() { }
        }

        public static Discord.Color RandomColor() {
            var random = new Random();

            // Generate random values for red, green, and blue components.
            var red = (byte)random.Next(256);
            var green = (byte)random.Next(256);
            var blue = (byte)random.Next(256);

            // Create and return the Discord.Color.
            return new Discord.Color(red, green, blue);
        }

        public static string GetAfxSetString(List<EggIncArtifactInstance> set) {
            return string.Join("\n", set.Select(s => ArtifactHelpers.GetAfEmoji(s) + ArtifactHelpers.GetRarityEmoji(s) + string.Join("", s.Stones.Select(st => ArtifactHelpers.GetAfEmoji(st)).ToList())));
        }

        public static string GetAfxString(EggIncArtifactInstance instance) {
            return ArtifactHelpers.GetAfEmoji(instance) + ArtifactHelpers.GetRarityEmoji(instance) + string.Join("", instance.Stones.Select(st => ArtifactHelpers.GetAfEmoji(st))).ToString();
        }

        [SlashCommand(Description = "Show off your saved Artifact Sets")]
        public static async Task SavedAfSets(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }
            var accountIndex = int.Parse(useraccount.Split("|")[1]);
            var account = user.EggIncAccounts[accountIndex];
            var afxSets = account.Backup?.ArtifactSets;
            if(afxSets is null || afxSets.Count == 0) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Backup is empty, or no Artifact Sets were found for this account");
                return;
            }

            var builder = AFXSetEmbedBuilder(user, accountIndex, afxSets, afxSets[0]);
            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Components = builder.ComponentBuilder?.Build();
                x.Embed = builder.EmbedBuilder.Build();
            });
        }

        [ComponentCommand]
        public static async Task LoadAFXSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {

            var dataItems = data.Split(",");
            var discordId = ulong.Parse(dataItems[0] ?? "-1");
            var accountIndex = int.Parse(dataItems[1] ?? "-1");
            var currentSetIndex = int.Parse(dataItems[2] ?? "-1");

            if(discordId < 0 || accountIndex < 0 || currentSetIndex < 0) return;

            var user = db.DBUsers.FirstOrDefault(x => x.DiscordId == discordId);
            if(user is null || user.EggIncAccounts.Count - 1 < accountIndex) return;

            var account = user.EggIncAccounts[accountIndex];
            var afxSets = account.Backup?.ArtifactSets;
            if(afxSets is null) return;

            var builder = AFXSetEmbedBuilder(user, accountIndex, afxSets, afxSets[currentSetIndex]);
            await component.UpdateAsync(x => {
                x.Content = "";
                x.Components = builder.ComponentBuilder?.Build();
                x.Embed = builder.EmbedBuilder.Build();
            });
        }

        private static AfxSetBuilder AFXSetEmbedBuilder(DBUser user, int accountIndex, List<List<EggIncArtifactInstance>> afxSets, List<EggIncArtifactInstance> currentSet) {
            var builder = new AfxSetBuilder() {
                ComponentBuilder = null
            };

            var componentBuilder = new Discord.ComponentBuilder();
            var buttonCount = 0;

            var currentSetIndex = afxSets.IndexOf(currentSet);
            var setsCount = afxSets.Count;

            var account = user.EggIncAccounts[accountIndex];
            var accText = user.EggIncAccounts.Count > 1 ? $"For account: {account.Backup?.UserName ?? "[No Name]"} ({account.Backup?.EarningsBonus.ToEggString() ?? "No EB"})" : "";

            var embedBuilder = new Discord.EmbedBuilder().WithAuthor(
                new Discord.EmbedAuthorBuilder()
                    .WithName($"Set {currentSetIndex + 1}")
                    .WithIconUrl("https://cdn.discordapp.com/emojis/877681508607987772.webp")
                ).WithColor(RandomColor())
                .WithDescription(GetAfxSetString(currentSet));
            if(accText != "")
                embedBuilder.WithFooter(new Discord.EmbedFooterBuilder().WithText(accText));

            if(currentSetIndex > 0 && setsCount > 1 && afxSets[currentSetIndex - 1] is not null) {
                componentBuilder.WithButton($"← Set {currentSetIndex}", $"LoadAFXSet:{user.DiscordId},{accountIndex},{currentSetIndex - 1}"); buttonCount++;
            }
            if(currentSetIndex < afxSets.Count - 1 && afxSets[currentSetIndex + 1] is not null) {
                componentBuilder.WithButton($"Set {currentSetIndex + 2} →", $"LoadAFXSet:{user.DiscordId},{accountIndex},{currentSetIndex + 1}"); buttonCount++;
            }
            if(buttonCount > 0) builder.ComponentBuilder = componentBuilder;

            builder.EmbedBuilder = embedBuilder;
            return builder;
        }

    }
}
