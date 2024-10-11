using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;

namespace EGG9000.Common.Helpers {
    public static class TesseractHelper {

        public static Image<Rgba32> GetCroppedImage(this Image<Rgba32> original) {
            // Crop 20% off the top and bottom
            var croppedImage = original.CropImage(0.20f, 0.20f);

            // Find the y-value of the lowest red (or near-red) pixel on the screen
            var lowestRedY = FindLowestRedPixel(croppedImage);

            // Crop 10% of the image height below that y-value
            var yCrop = croppedImage.CropBelowY(lowestRedY, 0.1f);

            // Crop to keep the middle 40% of the image
            var finalCrop = yCrop.CropImageMiddleP(0.4f);

            // Return the image
            return finalCrop;
        }

        private static Image<Rgba32> CropImage(this Image<Rgba32> original, float topPercent, float bottomPercent) {
            var width = original.Width;
            var height = original.Height;

            // Calculate the new height and starting Y position after cropping the top and bottom
            var cropStartY = (int)(height * topPercent);
            var cropHeight = height - (int)(height * (topPercent + bottomPercent));

            // Define the crop area
            var cropRectangle = new Rectangle(0, cropStartY, width, cropHeight);

            // Crop the image based on the calculated area
            original.Mutate(x => x.Crop(cropRectangle));

            return original;
        }

        private static Image<Rgba32> CropImageMiddleP(this Image<Rgba32> original, float middlePercent) {
            var width = original.Width;
            var height = original.Height;

            // Calculate the amount to crop from the left and right
            var cropAmount = (1 - middlePercent) / 2;

            // Calculate the new width and starting X position for cropping
            var cropStartX = (int)(width * cropAmount);
            var cropWidth = (int)(width * middlePercent);

            // Define the crop area to keep the middle portion of the image
            var cropRectangle = new Rectangle(cropStartX, 0, cropWidth, height);

            // Crop the image based on the calculated area
            original.Mutate(x => x.Crop(cropRectangle));

            return original;
        }

        private static Image<Rgba32> CropBelowY(this Image<Rgba32> original, int yValue, float bottomPercentage) {
            var width = original.Width;
            var height = original.Height;

            // Calculate how much height we want to crop based on the percentage
            var cropHeight = (int)(height * bottomPercentage);
            var cropStartY = Math.Min(yValue + 1, height - cropHeight);

            // Define the crop area below the red pixel
            var cropRectangle = new Rectangle(0, cropStartY, width, cropHeight);

            // Perform the crop
            original.Mutate(x => x.Crop(cropRectangle));
            return original;
        }

        private static int FindLowestRedPixel(Image<Rgba32> image) {
            var lowestY = -1;
            var width = image.Width;
            var height = image.Height;

            // Iterate through the image pixels from bottom to top
            for(var y = height - 1; y >= 0; y--) {
                for(var x = 0; x < width; x++) {
                    var pixel = image[x, y];
                    if(IsRed(pixel)) {
                        lowestY = y;
                        break;
                    }
                }
                if(lowestY != -1) break; // Stop after finding the lowest red pixel
            }

            return lowestY;
        }

        private static bool IsRed(Rgba32 color) {
            // Define the condition for "red" or "near-red" pixels
            return color.R > 150 && color.G < 100 && color.B < 100;
        }

        private static readonly List<string> CommonMisDetectionList = [
            "£1",
            "E1"
        ];

        public static string RunTesseract(Image<Rgba32> image) {
            var extractedText = "";

            using(var imageStream = new MemoryStream()) {
                // Preprocessing the image
                image.Mutate(x => x.Grayscale()); // Convert to grayscale
                image.Mutate(x => x.Contrast(1.2f)); // Increase contrast
                image.Mutate(x => x.GaussianSharpen()); // Maybe?

                image.SaveAsPng(imageStream);
                imageStream.Seek(0, SeekOrigin.Begin);

                // Dynamically set the path to the embedded tessdata directory
                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
                Environment.SetEnvironmentVariable("TESSDATA_PREFIX", tessDataPath);

                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var pix = Pix.LoadFromMemory(imageStream.ToArray());
                using var page = engine.Process(pix);
                extractedText = page.GetText();
            }
            // Split after any newlines, keeping the longest string
            var splits = extractedText.Split("\n");
            extractedText = splits.MaxBy(s => s.Length);
            // Replace "L"s with 4s
            extractedText = extractedText.Replace("L", "4");
            // Replace ":"s with 4s
            extractedText = extractedText.Replace(":", "4");
            // Replace ")"s with 2s
            extractedText = extractedText.Replace(")", "2");
            // Remove spaces
            extractedText = extractedText.Replace(" ", "");

            // Common mis-detections
            foreach(var misDetection in CommonMisDetectionList) {
                if(extractedText.StartsWith(misDetection)) {
                    extractedText = string.Concat("EI", extractedText.AsSpan(misDetection.Length));
                }
            }

            // Look for the EI pattern specifically in the extracted text
            var eiNumber = System.Text.RegularExpressions.Regex.Match(extractedText, @"EI\d{16}");
            return eiNumber.Success ? eiNumber.Value : $"No regex match!\n{extractedText}";
        }
    }
}
