using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.AfxSets;
using Discord.WebSocket;
using EGG9000.Site.Auth;
using EGG9000.Site.Models.Home;
using EGG9000.Site.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.ArtifactHelpers;

namespace EGG9000.Site.Controllers {

    // No controller-level [AllowAnonymous] here on purpose: it beats any [Authorize] on an action, so
    // adding one would silently unauthenticate the API-key endpoints below. Each open endpoint opts out
    // of the deny-by-default FallbackPolicy for itself.
    public class APIController(ApplicationDbContext db, Bugsnag.IClient bugsnag, IServiceProvider provider, ILogger<APIController> logger, IWebHostEnvironment env,
        Services.ArtifactImageRenderer renderer, DiscordSocketClient discord, IMemoryCache cache, LeaderboardService leaderboards) : E9KControllerBase {
        private readonly ApplicationDbContext _db = db;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly IServiceProvider _provider = provider;
        private readonly ILogger<APIController> _logger = logger;
        private readonly IWebHostEnvironment _env = env;
        private readonly Services.ArtifactImageRenderer _renderer = renderer;
        private readonly DiscordSocketClient _discord = discord;
        private readonly IMemoryCache _cache = cache;
        private readonly LeaderboardService _leaderboards = leaderboards;

        private void DrawArtifactCell(SKCanvas canvas, EggIncArtifactInstance inst, int cellX, int rowY, AfxSetsCreatorConfig config) {
            var isFrag = inst.Artifact.ToString().Contains("FRAGMENT", StringComparison.CurrentCultureIgnoreCase);
            var afName = inst.Artifact.ToString().ToUpper().Replace(" ", "_").Replace("'", "").Replace("_FRAGMENT", "");
            var afTier = isFrag ? 1 : (afName.Contains("_STONE") ? inst.Tier + 1 : inst.Tier);

            var afImagePath = GetWWWRelativePath(["images/artifacts", afName, $"{afName}_{afTier}.png"]);
            if(afImagePath == null) return;
            canvas.DrawRoundedTile(RarityColor(inst.Rarity), cellX, rowY, config.AFSize, config.AFSize, config.AFCornerRadius);
            canvas.DrawImageFile(afImagePath, cellX, rowY, config.AFSize);

            var stoneIndex = 1;
            foreach(var stone in inst.Stones ?? []) {
                var stoneName = stone.Artifact.ToString().ToUpper().Replace(" ", "_");
                var stonePath = GetWWWRelativePath(["images/artifacts", stoneName, $"{stoneName}_{stone.Tier + 1}.png"]);
                if(stonePath == null) continue;
                canvas.DrawImageFile(stonePath, cellX + config.AFSize - (int)(config.Padding * 0.5) - (config.StoneSize * stoneIndex), rowY + config.AFSize - (int)(config.Padding * 1.5), config.StoneSize);
                stoneIndex++;
            }
        }

        private string GetWWWRelativePath(List<string> relativePathJoins) {
            var imageDur = _env.WebRootPath;
            if(relativePathJoins != null && relativePathJoins.Count > 0) {
                imageDur = Path.Combine(imageDur, Path.Combine([.. relativePathJoins]));
            }
            return System.IO.File.Exists(imageDur) ? imageDur : null;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("api/generateeventimage")]
        public IActionResult GenerateEventImage([FromHeader] string authenticationKey, [FromBody] DBEvent customEvent) {
            if(BuildConfig.IsRelease && (string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken)) {
                return NotFound();
            }
            var imagePath = GetWWWRelativePath([
                "images/events",
                $"event_{customEvent.Type.ToLowerInvariant().Replace("-", "_")}.png"
            ]);
            if(imagePath == null) {
                Console.WriteLine("IMAGE PATH NOT FOUND!");
                return NotFound(new { message = $"Image for event type '{customEvent.Type}' not found." });
            }

            using var baseImage = SKImage.FromEncodedData(imagePath);
            if(baseImage == null) return NotFound(new { message = $"Image for event type '{customEvent.Type}' could not be decoded." });
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

            var newWidth = (int)(baseImage.Width * 1.1);
            var newHeight = (int)(baseImage.Height * 1.1);

            using var surface = SKSurface.Create(new SKImageInfo(newWidth, newHeight));
            var canvas = surface.Canvas;

            // CcOnly events (ULTRA-only) get a gradient background instead of a flat color.
            using(var fill = new SKPaint()) {
                if(customEvent.CcOnly) {
                    fill.Shader = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(newWidth, 0),
                        [SKColor.Parse("#f5a709"), SKColor.Parse("#900fb1")],
                        SKShaderTileMode.Clamp);
                } else {
                    fill.Color = SKColor.Parse(backgroundColor);
                }
                canvas.DrawRect(0, 0, newWidth, newHeight, fill);
            }

            canvas.DrawImage(baseImage, (newWidth - baseImage.Width) / 2, (newHeight - baseImage.Height) / 2, SKSamplingOptions.Default);
            return File(surface.EncodeImage(SKEncodedImageFormat.Png), "image/png");
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("api/generateinventoryb64")]
        public async Task<IActionResult> GenerateInventoryB64([FromHeader] string authenticationKey, [FromBody] InventoryAPIObject userObject) {
            if(BuildConfig.IsRelease && (string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken)) return NotFound();
            var user = await _db.DBUsers.FirstOrDefaultAsync(u => u.EIDs.Contains(userObject.EID));
            if(user == null) {
                return BadRequest(new { message = $"User with EID {userObject.EID} was not found." });
            }
            var account = user.EggIncAccounts.FirstOrDefault(a => a.Id == userObject.EID);
            if(account == null) {
                return BadRequest(new { message = $"Account with EID {userObject.EID} was not found for the user {user.DiscordUsername}" });
            }

            // The drawing (and the matching hover-target manifest) lives in ArtifactImageRenderer so the
            // bot endpoint and the MyFarms inventory tab paint from the exact same code. The bot only
            // wants the image bytes here; the manifest is ignored.
            var render = _renderer.RenderInventory(account, userObject.Config);
            if(!render.Ok) return BadRequest(new { message = render.Error });
            return File(render.Jpeg, "image/jpeg");
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("api/generateafxsetsb64")]
        public async Task<IActionResult> GenerateAfxSetsB64([FromHeader] string authenticationKey, [FromBody] AfxSetsAPIObject userObject) {
            if(BuildConfig.IsRelease && (string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken)) return NotFound();
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
            using var typeface = SKTypeface.FromFile(fontFilePath);
            using var font = new SKFont(typeface, config.TextFontSize);

            // Render every page, or just the one requested (used for paginated views that show a
            // single page at a time).
            var firstPageStart = 0;
            var lastPageStartExclusive = sets.Count;
            if(userObject.Page is int requestedPage) {
                firstPageStart = requestedPage * config.SetsPerPage;
                if(requestedPage < 0 || firstPageStart >= sets.Count) return BadRequest(new { message = $"Page {requestedPage} is out of range." });
                lastPageStartExclusive = firstPageStart + config.SetsPerPage;
            }

            var pages = new List<string>();
            for(var pageStart = firstPageStart; pageStart < lastPageStartExclusive; pageStart += config.SetsPerPage) {
                var pageSets = sets.Skip(pageStart).Take(config.SetsPerPage).ToList();
                var rowCount = pageSets.Count;

                var width = config.LabelWidth + (config.SlotsPerRow * config.AFSize) + (config.Padding * (config.SlotsPerRow + 1));
                var height = (rowCount * config.AFSize) + (config.Padding * (rowCount + 1));
                using var surface = SKSurface.Create(new SKImageInfo(width, height));
                var pageImage = surface.Canvas;
                pageImage.Clear(SKColor.Parse("#242422"));

                for(var r = 0; r < rowCount; r++) {
                    var set = pageSets[r];
                    var rowY = config.Padding + r * (config.AFSize + config.Padding);

                    var label = $"Set {pageStart + r + 1}";
                    pageImage.DrawTextFromTop(label, config.Padding, rowY + config.AFSize / 2f - config.TextFontSize / 2f, font, SKColors.White);

                    if(set.Count == 0) {
                        pageImage.DrawTextFromTop("(empty)", config.LabelWidth + config.Padding, rowY + config.AFSize / 2f - config.TextFontSize / 2f, font, SKColor.Parse("#8a8a86"));
                        continue;
                    }

                    for(var c = 0; c < set.Count && c < config.SlotsPerRow; c++) {
                        var cellX = config.LabelWidth + config.Padding * (c + 1) + c * config.AFSize;
                        DrawArtifactCell(pageImage, set[c], cellX, rowY, config);
                    }
                }

                pages.Add(Convert.ToBase64String(surface.EncodeImage(SKEncodedImageFormat.Jpeg)));
            }

            return Ok(new AfxSetsB64Response { Pages = pages });
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("api/generateartifactsetb64")]
        public IActionResult GenerateArtifactSetB64([FromHeader] string authenticationKey, [FromBody] ArtifactSetRenderRequest request) {
            if(BuildConfig.IsRelease && (string.IsNullOrEmpty(authenticationKey) || authenticationKey != SecretsHelper.BotToken)) return NotFound();
            if(request?.Artifacts is null || request.Artifacts.Count == 0) return BadRequest(new { message = "No artifacts provided." });

            var config = request.Config ?? new AfxSetsCreatorConfig(100);
            if(!config.IsValid(out var configError)) return BadRequest(new { message = configError });

            var fontFilePath = GetWWWRelativePath(["Always Together.otf"]);
            if(fontFilePath == null) return BadRequest(new { message = "`Always Together.otf` could not be found." });
            using var typeface = SKTypeface.FromFile(fontFilePath);
            using var font = new SKFont(typeface, config.TextFontSize);

            var width = config.LabelWidth + (config.SlotsPerRow * config.AFSize) + (config.Padding * (config.SlotsPerRow + 1));
            var height = config.AFSize + config.Padding * 2;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var pageImage = surface.Canvas;
            pageImage.Clear(SKColor.Parse("#242422"));

            var rowY = config.Padding;
            var label = request.Label ?? "Best Set";
            var maxLabelPx = config.LabelWidth - config.Padding;
            if(font.MeasureText(label) > maxLabelPx) {
                while(label.Length > 1 && font.MeasureText(label + "…") > maxLabelPx)
                    label = label[..^1];
                label += "…";
            }
            pageImage.DrawTextFromTop(label, config.Padding, rowY + config.AFSize / 2f - config.TextFontSize / 2f, font, SKColors.White);

            for(var c = 0; c < request.Artifacts.Count && c < config.SlotsPerRow; c++) {
                var inst = request.Artifacts[c];
                if(inst is null) continue;
                var cellX = config.LabelWidth + config.Padding * (c + 1) + c * config.AFSize;
                DrawArtifactCell(pageImage, inst, cellX, rowY, config);
            }

            return Ok(new ArtifactSetRenderResponse { Page = Convert.ToBase64String(surface.EncodeImage(SKEncodedImageFormat.Jpeg)) });
        }

        [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
        [HttpGet]
        [Route("api/LeaderboardJson")]
        // Keys issued before the move to /api were documented against this path. Attribute routing
        // replaces the conventional route, so without this line every existing consumer 404s.
        // Kept for compatibility for now while existing users switch to api/
        [Route("Home/LeaderboardJson")]
        public async Task<IActionResult> LeaderboardJson() {
            var guildId = GetGuildId();
            var guild = _discord.Guilds.FirstOrDefault(x => x.Id == guildId);
            if(guild == null) return StatusCode(503);
            await guild.DownloadUsersAsync();
            var leaderboard = await _leaderboards.GetLeaderboardAsync(guildId);

            var membersOfGuildOnly = User.Claims.FirstOrDefault(x => x.Type == "MembersOfGuildOnly")?.Value;
            if(!string.IsNullOrWhiteSpace(membersOfGuildOnly))
                leaderboard = [.. leaderboard.Where(x => string.Equals(x.Account?.Guild?.Trim(), membersOfGuildOnly.Trim(), StringComparison.OrdinalIgnoreCase))];

            var result = leaderboard.Select(x => new Home_LeaderboardApiItem {
                DiscordName = x.DisplayName,
                DiscordId = x.DisplayDiscordId,
                EggIncName = x.Backup.UserName,
                EarningsBonus = x.Backup.EarningsBonus,
                SoulEggs = x.Backup.SoulEggs,
                EggsOfProphecy = x.Backup.EggsOfProphecy,
                MER = x.Backup.MER,
                EggsOfTruth = x.Backup.EggsOfTruth,
                NumPrestiges = x.Backup.NumPrestiges
            }).ToList();
            return Json(result);
        }

        // Co-op membership does not turn over fast enough for a minute of staleness to matter, and this
        // is the only thing standing between a polling client and a full re-read of every co-op on a
        // contract, blob decode included.
        private static readonly TimeSpan GuildCoopsCacheTtl = TimeSpan.FromSeconds(60);

        [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
        [HttpGet]
        [Route("api/GuildCoopsJson")]
        public async Task<IActionResult> GuildCoopsJson(string contractId) {
            var guildTag = User.Claims.FirstOrDefault(x => x.Type == "MembersOfGuildOnly")?.Value;
            var error = ValidateGuildCoopsRequest(contractId, guildTag);
            if(error != null) return BadRequest(new { error });

            var guildId = GetGuildId();
            var cacheKey = BuildGuildCoopsCacheKey(guildId, contractId, guildTag);
            if(!_cache.TryGetValue(cacheKey, out List<GuildCoopApiItem> coops)) {
                coops = await GuildCoops.QueryAsync(_db, guildId, contractId, guildTag, HttpContext.RequestAborted);
                _cache.Set(cacheKey, coops, GuildCoopsCacheTtl);
            }
            return Json(coops);
        }

        // Returns null when the request is servable, otherwise the message to hand back as a 400.
        public static string ValidateGuildCoopsRequest(string contractId, string guildTag) {
            if(string.IsNullOrWhiteSpace(contractId))
                return "A contractId is required.";
            // The claim is only added when the key carries a guild, so an absent one means the key was
            // never scoped. Fails loudly to better inform the user.
            if(string.IsNullOrWhiteSpace(guildTag))
                return "This API key is not scoped to a guild.";
            return null;
        }

        public static string BuildGuildCoopsCacheKey(ulong guildId, string contractId, string guildTag) {
            return $"guildcoops:{guildId}:{contractId}:{guildTag?.Trim().ToLowerInvariant()}";
        }
    }
}
