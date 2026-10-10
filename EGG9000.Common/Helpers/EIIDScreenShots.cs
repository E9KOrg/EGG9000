using SkiaSharp;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public class EIIDScreenShots {
        private static readonly double MinRedPercent = 0.30;
        private static readonly double MinWhitePercent = 0.50;

        public static SKBitmap CropScreenShot(SKBitmap image) {
            var rgbaImage = image.Copy(SKColorType.Rgba8888);

            var rows = new ImageRowStats[image.Height];
            for(var y = 0; y < image.Height; y++) {
                rows[y] = new ImageRowStats();
                for(var x = 0; x < image.Width; x++) {
                    var pixel = rgbaImage.GetPixel(x, y);
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
                        rgbaImage.SetPixel(x, y, new SKColor(255, 0, 255));
                    }
                }
                if(rows[y].RedPixelCount < rows[y].WhitePixelCount && rows[y].WhitePixelCount > image.Width * MinWhitePercent) {
                    rows[y].MostlyWhite = true;
                    if(y > 0)
                        rows[y].MostlyWhiteRowCount = 1 + rows[y - 1].MostlyWhiteRowCount;
                    for(var x = 0; x < 20; x++) {
                        rgbaImage.SetPixel(x, y, new SKColor(0, 255, 255));
                    }
                }

            }

            var EILocation = FindEILocation(rows);

            for(var y = EILocation.yStart; y < EILocation.yEnd; y++) {
                for(var x = 20; x < 40; x++) {
                    rgbaImage.SetPixel(x, y, new SKColor(255, 255, 0));
                }
            }

            if(EILocation.yStart > 0) {
                var marked = rgbaImage;
                rgbaImage = marked.Crop(SKRectI.Create((int)(image.Width * 0.25), EILocation.yStart, (int)(image.Width * 0.5), EILocation.yEnd - EILocation.yStart));
                marked.Dispose();

                for(var x = 0; x < rgbaImage.Width; x++) {
                    for(var y = 0; y < rgbaImage.Height; y++) {
                        var p = rgbaImage.GetPixel(x, y);
                        rgbaImage.SetPixel(x, y, new SKColor(ContrastCurve(p.Red), ContrastCurve(p.Green), ContrastCurve(p.Blue)));
                    }
                }
                for(var x = 0; x < rgbaImage.Width; x++) {
                    for(var y = 0; y < rgbaImage.Height; y++) {
                        if(!IsProximateToBlack(rgbaImage, x, y)) {
                            rgbaImage.SetPixel(x, y, new SKColor(255, 255, 255));
                        }
                    }
                }
            }

            return rgbaImage;
        }

        public static byte ContrastCurve(byte b) {
            return (byte)Math.Clamp(b * 3 - 256 * 2, 0, 255);
        }

        public static void WriteText2(SKBitmap image, String text, SKColor color, int left, int top) {
            using var typeface = SKTypeface.FromFamilyName("Arial");
            using var font = new SKFont(typeface, 10);
            using var canvas = new SKCanvas(image);
            canvas.DrawTextFromTop(text, left, top, font, color);
        }

        public struct ImageRowStats {
            public int RedPixelCount;
            public int WhitePixelCount;
            public bool MostlyRed;
            public bool MostlyWhite;
            public int MostlyRedRowCount;
            public int MostlyWhiteRowCount;
        }

        public static bool IsWhite(SKColor color) {
            return color.Red >= 240 && color.Green >= 240 && color.Blue >= 240;
        }
        public static bool IsRed(SKColor color) {
            return color.Red >= 200 && color.Green <= 75 && color.Blue <= 75;
        }
        public static bool IsBlack(SKColor color) {
            return color.Red < 100 && color.Green < 100 && color.Blue < 100;
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

        public static bool IsProximateToBlack(SKBitmap image, int x, int y) {
            var pixel = image.GetPixel(x, y);
            if(IsBlack(pixel))
                return true;
            if(IsWhite(pixel))
                return false;
            // Go out and find the darkest pixel within 5 pixels
            (int x, int y) darkest = (x, y);
            for(var i = 0; i < image.Height / 30; i++) {
                darkest = FindDarkestNeighbor(image, darkest.x, darkest.y);
                if(darkest.x > -1 && IsBlack(image.GetPixel(darkest.x, darkest.y)))
                    return true;
            }
            return false;
        }

        public static (int x, int y) FindDarkestNeighbor(SKBitmap image, int x, int y) {
            var darkest = 255;
            var darkestx = -1;
            var darkesty = -1;

            for(var dx = -1; dx <= 1; dx++) {
                for(var dy = -1; dy <= 1; dy++) {
                    if(dx == 0 && dy == 0) continue;
                    if(x + dx < 0 || x + dx >= image.Width || y + dy < 0 || y + dy >= image.Height)
                        continue;
                    var red = image.GetPixel(x + dx, y + dy).Red;
                    if(red < darkest) {
                        darkest = red;
                        darkestx = x + dx;
                        darkesty = y + dy;
                    }
                }
            }
            return (darkestx, darkesty);
        }

        public static List<(SKBitmap, char)> generatedImages = null;
        private static readonly Object thisLock = new();


        public static string ReadText(SKBitmap image) {
            lock(thisLock) {
                if(generatedImages == null || generatedImages.Count == 0)
                    GenerateImages();
            }

            var charPositions = FindCharPositions(image);
            var outtext = "";
            foreach(var r in charPositions) {
                using var clone = image.Crop(r);
                outtext += FindMatch(clone);
            }
            return outtext;
        }

        public static SKBitmap SampleLetters(SKBitmap image) {
            lock(thisLock) {
                if(generatedImages == null || generatedImages.Count == 0)
                    GenerateImages();
            }

            var charPositions = FindCharPositions(image);
            foreach(var r in charPositions.Skip(1)) {

                var footerTop = generatedImages[0].Item1.Height * 2;
                var retImage = new SKBitmap(generatedImages.Sum(x => x.Item1.Width), footerTop + 20);
                retImage.Erase(SKColors.White);

                var currentX = 0;

                var maxC = 0.0;
                var maxCx1 = 0;
                var maxCx2 = 0;
                foreach(var image2 in generatedImages) {
                    using var cropped = image.Crop(r);
                    using var clone = ResizeToHeight(cropped, image2.Item1.Height);

                    var c = CompareImages(clone, image2.Item1, true);

                    if(c > maxC) {
                        maxC = c;
                        maxCx1 = currentX; maxCx2 = currentX + image2.Item1.Width;
                    }

                    using(var canvas = new SKCanvas(retImage)) {
                        canvas.DrawBitmap(clone, currentX, 0, SKSamplingOptions.Default);
                        canvas.DrawBitmap(image2.Item1, currentX, image2.Item1.Height, SKSamplingOptions.Default);
                        using var white = new SKPaint { Color = SKColors.White };
                        canvas.DrawRect(currentX, footerTop + 5, image2.Item1.Width, 15, white);
                    }

                    WriteText2(retImage, Math.Round(c, 3).ToString().Replace("0.", "."), new SKColor(0, 0, 127), currentX, footerTop + 5);


                    currentX += image2.Item1.Width;
                }

                using(var canvas = new SKCanvas(retImage)) {
                    using var green = new SKPaint { Color = new SKColor(0, 255, 0) };
                    canvas.DrawRect(maxCx1, footerTop + 15, maxCx2 - maxCx1, 5, green);
                }
                return retImage;
            }
            return null;
        }


        public static char FindMatch(SKBitmap image, bool showDebug = false) {

            var matches = generatedImages.Select(x => {

                return (CompareImages(image, x.Item1), x.Item2, showDebug);
            });
            return matches.MaxBy(x => x.Item1).Item2;
        }

        public static double CompareImages(SKBitmap image1, SKBitmap image2, bool showDebug = false) {
            using var i1c = showDebug ? image1.Copy() : ResizeToHeight(image1, image2.Height);

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
                    var red = i1c.GetPixel(x, y).Red;
                    if(Math.Abs(red - image2.GetPixel(x, y).Red) < 100) {
                        if(red < 100) {
                            matches += 1;
                            if(showDebug) image1.SetPixel(x, y, new SKColor(255, 0, 255));
                        } else {
                            matches++;
                            if(showDebug) image1.SetPixel(x, y, new SKColor(255, 255, 0));
                        }
                    }
                }
            }

            return (double)matches / ((width * height));
        }

        private static SKBitmap ResizeToHeight(SKBitmap image, int height) {
            var width = Math.Max(1, (int)Math.Round(image.Width * (double)height / image.Height));
            return image.ResizeTo(width, height);
        }

        public static void GenerateImages() {

            var chars = new List<char>();
            for(var i = 0; i < 10; i++) {
                chars.Add((i % 10).ToString()[0]);
            }

            var eiid = "EI" + new String([.. chars]);

            generatedImages = [];
            using var typeface = SKTypeface.FromFile("Fonts/always together.otf");
            using var font = new SKFont(typeface, 100);
            foreach(var r in eiid) {
                font.MeasureText(r.ToString(), out var rect);

                for(var i = -2; i <= 2; i++) {
                    for(var j = -2; j <= 2; j++) {
                        var image = new SKBitmap((int)rect.Width, (int)rect.Height);
                        image.Erase(SKColors.White);
                        using var canvas = new SKCanvas(image);
                        canvas.DrawTextFromTop(r.ToString(), -1 + i * 2, 8 + j * 2, font, new SKColor(20, 20, 20));
                        generatedImages.Add((image, r));
                    }
                }


            }
        }


        public static (int y1, int y2) FindCharacterTopBottom(SKBitmap rgbaImage, int x1, int x2) {
            var y1 = 0;
            for(var y = 0; y < rgbaImage.Height; y++) {
                var anyBlack = false;
                for(var x = x1; x <= x2; x++) {
                    var pixel = rgbaImage.GetPixel(x, y);
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

        public static List<SKRectI> FindCharPositions(SKBitmap rgbaImage) {
            var charPositions = new List<SKRectI>();
            for(var x = 0; x < rgbaImage.Width; x++) {
                var anyBlack = false;
                for(var y = 0; y < rgbaImage.Height; y++) {
                    var pixel = rgbaImage.GetPixel(x, y);
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
                            var pixel = rgbaImage.GetPixel(x, y);
                            if(IsBlack(pixel)) {
                                anyBlack = true;
                                y = rgbaImage.Height;
                            }
                        }
                        if(!anyBlack) {
                            var charY = FindCharacterTopBottom(rgbaImage, startx, x - 1);
                            charPositions.Add(SKRectI.Create(startx, charY.y1, x - startx - 1, charY.y2 - charY.y1));

                            break;
                        }
                    }
                }
            }
            return charPositions;
        }
    }
}
