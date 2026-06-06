using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EGG9000.Common.Helpers {
    public class EIIDScreenShots {
        private static readonly double MinRedPercent = 0.30;
        private static readonly double MinWhitePercent = 0.50;

        public static Image<Rgba32> CropScreenShot(Image image) {
            var rgbaImage = image.CloneAs<Rgba32>();

            var rows = new ImageRowStats[image.Height];
            for(var y = 0; y < image.Height; y++) {
                rows[y] = new ImageRowStats();
                for(var x = 0; x < image.Width; x++) {
                    var pixel = rgbaImage[x, y];
                    if(IsWhite(pixel)) {
                        rows[y].WhitePixelCount++;
                    }

                    if(IsRed(pixel)) {
                        rows[y].RedPixelCount++;
                    }
                }

                if(rows[y].RedPixelCount > rows[y].WhitePixelCount && rows[y].RedPixelCount > image.Width * MinRedPercent) {
                    rows[y].MostlyRed = true;
                    if(y > 0)
                        rows[y].MostlyRedRowCount = 1 + rows[y - 1].MostlyRedRowCount;

                    for(var x = 0; x < 20; x++) {
                        rgbaImage[x, y] = new Rgba32(255, 0, 255);
                    }
                }
                if(rows[y].RedPixelCount < rows[y].WhitePixelCount && rows[y].WhitePixelCount > image.Width * MinWhitePercent) {
                    rows[y].MostlyWhite = true;
                    if(y > 0)
                        rows[y].MostlyWhiteRowCount = 1 + rows[y - 1].MostlyWhiteRowCount;
                    for(var x = 0; x < 20; x++) {
                        rgbaImage[x, y] = new Rgba32(0, 255, 255);
                    }
                }

            }

            var EILocation = FindEILocation(rows);

            for(var y = EILocation.yStart; y < EILocation.yEnd; y++) {
                for(var x = 20; x < 40; x++) {
                    rgbaImage[x, y] = new Rgba32(255, 255, 0);
                }
            }

            if(EILocation.yStart > 0) {
                rgbaImage.Mutate(x =>
                x.Crop(new Rectangle((int)(image.Width * 0.25), EILocation.yStart, (int)(image.Width * 0.5), EILocation.yEnd - EILocation.yStart)));

                for(var x = 0; x < rgbaImage.Width; x++) {
                    for(var y = 0; y < rgbaImage.Height; y++) {
                        var pixel = new Rgba32(ContrastCurve(rgbaImage[x, y].R), ContrastCurve(rgbaImage[x, y].G), ContrastCurve(rgbaImage[x, y].B));
                        rgbaImage[x, y] = pixel;
                    }
                }
                for(var x = 0; x < rgbaImage.Width; x++) {
                    for(var y = 0; y < rgbaImage.Height; y++) {
                        if(!IsProximateToBlack(rgbaImage, x, y)) {
                            rgbaImage[x, y] = new Rgba32(255, 255, 255);
                        }
                    }
                }
            }

            return rgbaImage;
        }

        public static byte ContrastCurve(byte b) => (byte)Math.Clamp(b * 3 - 256 * 2, 0, 255);

        public static void WriteText2(Image<Rgba32> image, String text, Color color, int left, int top) {
            FontCollection collection = new();
            SystemFonts.TryGet("Arial", out var family);
            var font = family.CreateFont(10, FontStyle.Regular);


            var options = new TextOptions(font) {
                Dpi = 72,
                KerningMode = KerningMode.Standard
            };

            var rect = TextMeasurer.MeasureBounds(text, options);

            image.Mutate(x => x.DrawText(
                text,
                font,
                color,
                new PointF(left, top)
            ));
        }

        public struct ImageRowStats {
            public int RedPixelCount;
            public int WhitePixelCount;
            public bool MostlyRed;
            public bool MostlyWhite;
            public int MostlyRedRowCount;
            public int MostlyWhiteRowCount;
        }

        public static bool IsWhite(Rgba32 color) {
            return color.R >= 240 && color.G >= 240 && color.B >= 240;
        }
        public static bool IsRed(Rgba32 color) {
            return color.R >= 200 && color.G <= 75 && color.B <= 75;
        }
        public static bool IsBlack(Rgba32 color) {
            return color.R < 100 && color.G < 100 && color.B < 100;
        }

        public static (int yStart, int yEnd) FindEILocation(ImageRowStats[] rows) {
            for(var y = 1; y < rows.Length; y++) {
                // Find bottom of large mostly red sections
                if(rows[y].MostlyRedRowCount == 0 && rows[y - 1].MostlyRedRowCount > 40) {
                    for(var y2 = y; y2 < y + 300 && y2 < rows.Length; y2++) {
                        // Find bottom of large mostly white section close to red section
                        if(rows[y2].MostlyWhiteRowCount == 0 && rows[y2 - 1].MostlyWhiteRowCount > 30) {
                            return (y2 - rows[y2 - 1].MostlyWhiteRowCount, y2 - 1);
                        }
                    }
                }
            }
            return (0, 0);
        }

        public static bool IsProximateToBlack(Image<Rgba32> image, int x, int y) {
            if(IsBlack(image[x, y]))
                return true;
            if(IsWhite(image[x, y]))
                return false;
            // Go out and find the darkest pixel within 5 pixels
            (int x, int y) darkest = (x, y);
            for(var i = 0; i < image.Height / 30; i++) {
                darkest = FindDarkestNeighbor(image, darkest.x, darkest.y);
                if(darkest.x > -1 && IsBlack(image[darkest.x, darkest.y]))
                    return true;
            }
            return false;
        }

        public static (int x, int y) FindDarkestNeighbor(Image<Rgba32> image, int x, int y) {
            var darkest = 255;
            var darkestx = -1;
            var darkesty = -1;

            for(var dx = -1; dx <= 1; dx++) {
                for(var dy = -1; dy <= 1; dy++) {
                    if(dx == 0 && dy == 0) continue;
                    if(x + dx < 0 || x + dx >= image.Width || y + dy < 0 || y + dy >= image.Height)
                        continue;
                    if(image[x + dx, y + dy].R < darkest) {
                        darkest = image[x + dx, y + dy].R;
                        darkestx = x + dx;
                        darkesty = y + dy;
                    }
                }
            }
            return (darkestx, darkesty);
        }

        public static List<(Image<Rgba32>, char)> generatedImages = null;
        private static readonly Lock thisLock = new();


        public static string ReadText(Image<Rgba32> image) {
            lock(thisLock) {
                if(generatedImages == null || generatedImages.Count == 0)
                    GenerateImages();
            }

            var charPositions = FindCharPositions(image);
            var outtext = "";
            foreach(var r in charPositions) {
                var clone = image.Clone();
                clone.Mutate(x => x.Crop(r));
                outtext += FindMatch(clone);
            }
            return outtext;
        }

        public static char FindMatch(Image<Rgba32> image, bool showDebug = false) {

            var matches = generatedImages.Select(x => {

                return (CompareImages(image, x.Item1), x.Item2, showDebug);
            });
            return matches.MaxBy(x => x.Item1).Item2;
        }

        public static double CompareImages(Image<Rgba32> image1, Image<Rgba32> image2, bool showDebug = false) {
            var i1c = image1.Clone();
            if(!showDebug) i1c.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Pad, Size = new Size(0, image2.Height) }));

            double matches = 0;

            var width = Math.Min(i1c.Width, image2.Width);
            var height = Math.Min(i1c.Height, image2.Height);
            var checkPoint = (int)(width * 0.20);

            for(var x = 0; x < width; x++) {
                if(x == checkPoint) {
                    var currentRatio = matches / (checkPoint * height);
                    if(currentRatio < 0.6)
                        break;
                }
                for(var y = 0; y < height; y++) {
                    if(Math.Abs(i1c[x, y].R - image2[x, y].R) < 100) {
                        if(i1c[x, y].R < 100) {
                            matches += 1;
                            if(showDebug) image1[x, y] = new Rgba32(255, 0, 255);
                        } else {
                            matches++;
                            if(showDebug) image1[x, y] = new Rgba32(255, 255, 0);
                        }
                    }
                }
            }

            return (double)matches / ((width * height));
        }

        public static void GenerateImages() {

            var chars = new List<char>();
            for(var i = 0; i < 10; i++) {
                chars.Add((i % 10).ToString()[0]);
            }

            var eiid = "EI" + new String([.. chars]);

            generatedImages = [];
            FontCollection collection = new();
            var family = collection.Add("Fonts/always together.otf");
            var font = family.CreateFont(100, FontStyle.Italic);
            var options = new TextOptions(font) {
                Dpi = 72,
                KerningMode = KerningMode.Auto
            };
            foreach(var r in eiid) {
                var rect = TextMeasurer.MeasureBounds(r.ToString(), options);

                for(var i = -2; i <= 2; i++) {
                    for(var j = -2; j <= 2; j++) {
                        var image = new Image<Rgba32>((int)rect.Width, (int)rect.Height, new Rgba32(255, 255, 255));
                        image.Mutate(x => x.DrawText(r.ToString(),
                        font,
                        new Color(new Rgba32(20, 20, 20)),
                        new PointF(
                            -1 + i * 2, 8 + j * 2)));
                        generatedImages.Add((image, r));
                    }
                }
            }
        }


        public static (int y1, int y2) FindCharacterTopBottom(Image<Rgba32> rgbaImage, int x1, int x2) {
            var y1 = 0;
            for(var y = 0; y < rgbaImage.Height; y++) {
                var anyBlack = false;
                for(var x = x1; x <= x2; x++) {
                    var pixel = rgbaImage[x, y];
                    if(IsBlack(pixel)) {
                        anyBlack = true;
                        x = rgbaImage.Width;
                    }
                }
                if(anyBlack && y1 == 0) {
                    y1 = y;
                }
                if(!anyBlack && y1 > 0) {
                    return (y1, y - 1);
                }
            }
            return (0, 0);
        }

        public static List<Rectangle> FindCharPositions(Image<Rgba32> rgbaImage) {
            var charPositions = new List<Rectangle>();
            for(var x = 0; x < rgbaImage.Width; x++) {
                var anyBlack = false;
                for(var y = 0; y < rgbaImage.Height; y++) {
                    var pixel = rgbaImage[x, y];
                    if(IsBlack(pixel)) {
                        anyBlack = true;
                        y = rgbaImage.Height;
                    }
                }
                var startx = x;
                if(anyBlack) {
                    for(; x < rgbaImage.Width; x++) {
                        anyBlack = false;
                        for(var y = 0; y < rgbaImage.Height; y++) {
                            var pixel = rgbaImage[x, y];
                            if(IsBlack(pixel)) {
                                anyBlack = true;
                                y = rgbaImage.Height;
                            }
                        }
                        if(!anyBlack) {
                            var charY = FindCharacterTopBottom(rgbaImage, startx, x - 1);
                            charPositions.Add(new Rectangle(startx, charY.y1, x - startx - 1, charY.y2 - charY.y1));

                            break;
                        }
                    }
                }
            }
            return charPositions;
        }
    }
}
