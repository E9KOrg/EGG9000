using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

using MassTransit.SagaStateMachine;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Color = SixLabors.ImageSharp.Color;

namespace EGG9000.Site.Controllers {
    public class EIIDTest(DiscordSocketClient _client, ApplicationDbContext _db) : Controller {
        static string baseFolder = @"D:\Websites\EGG9000\EGG9000.Site\wwwroot\test";
        static string downloadFolder = baseFolder + @"\downloads";
        static string cropFolder = baseFolder + @"\crops";

        public IActionResult Index() {
            return View();
        }

        public IActionResult TestSample() {
            return View();
        }

        public IActionResult FindDuplicates() {
            string folder = @"D:\Websites\EGG9000\EGG9000.Site\wwwroot\test\downloads";
            var files = Directory.GetFiles(folder);

            var duplicates = new Dictionary<string, List<string>>();
            foreach(var file in files) {
                using(var md5 = MD5.Create()) {
                    using var stream = System.IO.File.OpenRead(file);
                    var hash = Convert.ToBase64String(md5.ComputeHash(stream));
                    if(duplicates.ContainsKey(hash)) {
                        duplicates.First(x => x.Key == hash).Value.Add(file);
                    } else {
                        duplicates.Add(hash, new List<string> { file });
                    }
                }
            }

            return Content(duplicates.Where(x => x.Value.Count> 1).Count().ToString());
        }

        public async Task<IActionResult> Verify() {
            string folder = @"D:\Websites\EGG9000\EGG9000.Site\wwwroot\test\downloads";
            var files = Directory.GetFiles(folder);
            var messages = await _client.GetGuild(656455567858073601).GetThreadChannel(1294422983904985098).GetMessagesAsync().FlattenAsync();


            var discordUserIds = messages.Where(x => x.Embeds.Count > 0).Select(x => GetUserIDFromEmbed(x)).ToList();

            var dbusers = await _db.DBUsers.Where(x => discordUserIds.Contains(x.DiscordId)).ToListAsync();

            var sb = new StringBuilder();

            var success = 0;

            Int64 totalTime = 0;

            foreach(var file in files) {
                var image = SixLabors.ImageSharp.Image.Load(file);

                var sw = new Stopwatch();
                sw.Start();

                var cropImage = EIIDScreenShots.CropScreenShot(image);

                var cropTime = sw.ElapsedMilliseconds;


                var outtext = EIIDScreenShots.ReadText(cropImage);

                var matchTime = sw.ElapsedMilliseconds;

                sw.Stop();


                var messageId = Regex.Match(file, @"(\d+)\.png").Groups[1].Value;

                var message = messages.FirstOrDefault(x => x.Id.ToString() == messageId);

                var dbuser = dbusers.First(x => x.DiscordId == GetUserIDFromEmbed(message));

                var idMatches = dbuser.EggIncAccounts.Any(x => x.Id == outtext);

                var matchNumbers = dbuser.EggIncAccounts.Any(x => x.Id.Skip(2).ToString() == outtext.Skip(2).ToString());

                if(idMatches) {
                    sb.AppendLine($"{outtext} matches!");
                    success++;
                } else {
                    sb.AppendLine($"{outtext} unable to find match, {string.Join(",", dbuser.EggIncAccounts.Select(x => x.Id))}, {dbuser.DiscordUsername}, {message.Id}");
                }
                
                Console.WriteLine($"Crop: {cropTime} Match {matchTime}");
                totalTime += cropTime + matchTime;
            }

            Console.WriteLine((totalTime / files.Count()).ToString());

            sb.AppendLine($"{((success / files.Count()) * 100)}% Success Rate");

            return Content(sb.ToString());
        }

        private ulong GetUserIDFromEmbed(IMessage message) {
            return ulong.Parse(Regex.Match(message.Embeds.First().Description, @"<@(\d+)>").Groups[1].Value);
        }

        public IActionResult Test(string filename) {
            return Image(Path.Combine(downloadFolder, filename));
        }

        public async Task<IActionResult> DownloadNew() {
            var files = Directory.GetFiles(downloadFolder);
            var messages = await _client.GetGuild(656455567858073601).GetThreadChannel(1294422983904985098).GetMessagesAsync().FlattenAsync();

            foreach(var message in messages.Where(x => x.Attachments.Any())) {
                if(!files.Any(x => x.StartsWith(message.Id.ToString()))) {
                    var httpClient = new HttpClient();
                    var responseStream = await httpClient.GetStreamAsync(message.Attachments.First().Url);
                    using var fileStream = new FileStream($"{downloadFolder}\\{message.Id}.png", FileMode.Create);
                    responseStream.CopyTo(fileStream);
                }
            }

            return Content("Success");
        }

        public IActionResult Image(string location) {
            var image = SixLabors.ImageSharp.Image.Load(location);

            var cropImage = EIIDScreenShots.CropScreenShot(image);



            //cropImage.SaveAsPng(Path.Combine(cropFolder, Regex.Match(location, @"\d+\.png").Value));

            //var ret = TesseractHelper.RunTesseract(cropImage);

            //if(!Regex.IsMatch(ret.Item2, @"EI\d{16}")) {
            //    Console.WriteLine(ret.Item2);
            //}
            //EIIDScreenShots.WriteText(cropImage, ret.Item2, new Color(Rgba32.ParseHex(Regex.IsMatch(ret.Item2, @"EI\d{16}") ? "#00AA00FF" : "#000000FF")));

            var outtext = EIIDScreenShots.ReadText(cropImage);
            EIIDScreenShots.WriteText(cropImage, outtext, new Color(Rgba32.ParseHex(Regex.IsMatch(outtext, @"EI\d{16}") ? "#00AA00FF" : "#000000FF")));



            var resultStream = new MemoryStream();
            cropImage.SaveAsPng(resultStream);
            resultStream.Position = 0;
            return File(resultStream, "image/png");
        }
        public IActionResult ImageSample(string location) {
            var image = SixLabors.ImageSharp.Image.Load(location);

            var cropImage = EIIDScreenShots.CropScreenShot(image);

            cropImage = EIIDScreenShots.SampleLetters(cropImage);

            var resultStream = new MemoryStream();
            cropImage.SaveAsPng(resultStream);
            resultStream.Position = 0;
            return File(resultStream, "image/png");
        }

        public IActionResult ShowRandom() {
            return View();
        }
        public IActionResult Random() {
            var chars = new List<char>();
            for(var i = 0; i < 16; i++) {
                chars.Add((i % 10).ToString()[0]);
            }

            Random rng = new Random();


            chars = chars.OrderBy(_ => rng.Next()).ToList();

            var eiid = "EI" + new String(chars.ToArray());

            FontCollection collection = new();
            FontFamily family = collection.Add("Fonts/always together.otf");
            Font font = family.CreateFont((float)(rng.NextDouble() * 40 + 20), FontStyle.Italic);


            var options = new TextOptions(font) {
                Dpi = 72,
                KerningMode = KerningMode.Standard
            };

            var rect = TextMeasurer.MeasureBounds(eiid, options);

            var image = new Image<Rgba32>((int)(rect.Width * 2), (int)(rect.Height * 1.5), new Rgba32(255, 255, 255));

            var color = (byte)(rng.NextDouble() * 50 + 135);



            image.Mutate(x => x
            //.Resize(new ResizeOptions {
            //    Mode = ResizeMode.BoxPad,
            //    Position = AnchorPositionMode.Bottom,
            //    PadColor = new Rgba32(255, 255, 255),
            //    Size = new Size(image.Width, (int)(image.Height * 1.2)),
            //})
            .GaussianBlur((float)rng.NextDouble() * 5)
            .DrawText(
                eiid,
                font,
                new Color(new Rgba32(color, color, color)),
                new PointF(
                    (image.Width - rect.Width) / 2,
                        (image.Height - rect.Height) / 2
                        )));




            var resultStream = new MemoryStream();
            image.SaveAsJpeg(resultStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = (int?)(rng.NextDouble() * 50 + 25) });
            resultStream.Position = 0;

            var image2 = SixLabors.ImageSharp.Image.Load(resultStream);

            image2.SaveAsPng(Path.Combine(cropFolder + @"\traindata", eiid + ".png"));

            System.IO.File.WriteAllText(Path.Combine(cropFolder + @"\traindata", eiid + ".gt.txt"), eiid);


            resultStream.Position = 0;
            return File(resultStream, "image/jpeg");
        }
    }
}
