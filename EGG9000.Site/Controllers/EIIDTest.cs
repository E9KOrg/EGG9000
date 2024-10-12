using Discord;
using Discord.WebSocket;

using EGG9000.Common.Helpers;

using MassTransit.SagaStateMachine;

using Microsoft.AspNetCore.Mvc;

using NuGet.Protocol;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Color = SixLabors.ImageSharp.Color;

namespace EGG9000.Site.Controllers {
    public class EIIDTest(DiscordSocketClient _client) : Controller {
        static string baseFolder = @"D:\Websites\EGG9000\EGG9000.Site\wwwroot\test";
        static string downloadFolder = baseFolder + @"\downloads";
        static string cropFolder = baseFolder + @"\crops";

        public IActionResult Index() {
            return View();
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

            var ret = TesseractHelper.RunTesseract(cropImage);

            if(!Regex.IsMatch(ret.Item2, @"EI\d{16}")) {
                Console.WriteLine(ret.Item2);
            }
            EIIDScreenShots.WriteText(cropImage, ret.Item2, new Color(Rgba32.ParseHex(Regex.IsMatch(ret.Item2, @"EI\d{16}") ? "#00AA00FF" : "#000000FF") ));



            var resultStream = new MemoryStream();
            cropImage.SaveAsPng(resultStream);
            resultStream.Position = 0;
            return File(resultStream, "image/png");
        }

       

    }
}
