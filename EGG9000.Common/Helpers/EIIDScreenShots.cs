using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class EIIDScreenShots {
        static double MinRedPercent = 0.30;
        static double MinWhitePercent = 0.50;

        public static Image<Rgba32> CropScreenShot(Image image) {
            var rgbaImage = image.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgba32>();

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
                        //pixel = new Rgba32(rgbaImage[x, y].R, rgbaImage[x, y].R, rgbaImage[x, y].R);
                        rgbaImage[x, y] = pixel;
                    }
                }
                //rgbaImage.Mutate(x => x.GaussianSharpen(10));

            }

            return rgbaImage;
        }

        public static byte ContrastCurve(byte b) {
            //Try without contrast first
            return b;
            return Math.Clamp((byte)(b * 2 - 256 * 1), (byte)0, (byte)255);
        }

        public static void WriteText(Image<Rgba32> image, String text, Color color) {
            FontFamily fontFamily;
            const float TextPadding = 0f;

            FontCollection collection = new();
            FontFamily family = collection.Add("Fonts/always together.otf");
            Font font = family.CreateFont(24, FontStyle.Italic);


            var options = new TextOptions(font) {
                Dpi = 72,
                KerningMode = KerningMode.Standard
            };

            var rect = TextMeasurer.MeasureBounds(text, options);

            image.Mutate(x => x
            .Resize(new ResizeOptions {  
                Mode = ResizeMode.BoxPad, 
                Position = AnchorPositionMode.Bottom, 
                PadColor = new Rgba32(255,255,255),
                Size = new Size(image.Width, (int)(image.Height * 1.2)),
            })
            .DrawText(
                text,
                font,
                color,
                new PointF((image.Width - rect.Width) / 2,
                        5)));
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

        public static (int yStart, int yEnd) FindEILocation(ImageRowStats[] rows) {
            for(var y = 1; y < rows.Length; y++) {
                // Find bottom of large mostly red sections
                if(rows[y].MostlyRedRowCount == 0 && rows[y - 1].MostlyRedRowCount > 40) {
                    //Console.WriteLine($"Found Large Mostly Red Section at {y}");
                    for(var y2 = y; y2 < y + 300 && y2 < rows.Length; y2++) {
                        // Find bottom of large mostly white section close to red section

                        if(rows[y2].MostlyWhiteRowCount == 0 && rows[y2 - 1].MostlyWhiteRowCount > 30) {
                            //Console.WriteLine($"Found Large Mostly White Section at {y2}");
                            return ((int)(y + (y2 - y) * 0.05), y2);
                        }
                    }
                }
            }
            return (0, 0);
        }
    }
}
