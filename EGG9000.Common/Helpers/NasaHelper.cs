using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Bot.Common.Helpers.ChannelHelper;

namespace EGG9000.Common.Helpers;

public static partial class NasaHelper {
    // # Official NASA API is bugged and hasn't been updated in 14 years.
    // # It works 85% of the time, but when it goes bad, it goes really bad.
    // # Also big benefit of running our own instance is, API key is no longer needed.
    // # If for whatever reason we want to switch back to official NASA API,
    // # just change the BASE_URL and add api_key param to the Api URL.
    // public const string BASE_URL = "https://api.nasa.gov";
    private const string BASE_URL = "https://nasa.davidarthurcole.me";
    private const string APOD_ENDPOINT = "/v1/apod/";
    private const string URL_PARAM_STRING = "?thumbs=true&hd=true";
    // public const string API_KEY = "";
    private const string NasaApiUrl = $"{BASE_URL}{APOD_ENDPOINT}{URL_PARAM_STRING}";

#nullable enable
    [GeneratedRegex(@"^(?:https?:\/{2})?(?:(?:[wm]|shorts)+\.)?you\.?tu\.?be(?:-nocookie)?(?:\.com)?\/(?:(?:embed|oembed(?:\?.+v%3D)|[ev]|(?:attribution_link.+v%3D)|watch\??(?:\/|(?:v|(?:feature|list|app)=.+&v)=)|(?:shorts|live))?\/?)(?<videoId>[\w\d-]+).*$")]
    private static partial Regex YouTubeRegex();
    private static string? TryExtractYouTubeId(string url) {
        var match = YouTubeRegex().Match(url);
        return match.Success ? match.Groups["videoId"].Value : null;
    }

    public static async Task<bool> FetchNewAPOD(ApplicationDbContext _db, ILogger logger, CancellationToken cancellationToken) {
        var latestApod = await GetNasaApodResponseAsync(logger, cancellationToken);
        if(latestApod is null) {
            logger.LogWarning("Failed to fetch latest APOD.");
            return false;
        } else if(_latestApodCache is not null && latestApod.ID == _latestApodCache.ID) {
            logger.LogInformation("No new APOD found.");
            return false;
        }

        var existingApod = await GetLatestApod(_db);
        if(existingApod is not null && latestApod.ID == existingApod.ID) {
            logger.LogInformation("No new APOD found against database.");
            _latestApodCache = existingApod;
            return false;
        }

        logger.LogInformation("New APOD found: {Title} ({Date})", latestApod.Title, latestApod.DateString);
        await _db.NasaApods.AddAsync(latestApod, cancellationToken);
        _latestApodCache = latestApod;
        return true;
    }

    public static async Task<NasaApod?> GetLatestApod(ApplicationDbContext _db) {
        _latestApodCache ??= await _db.NasaApods.OrderByDescending(a => a.DateString).FirstOrDefaultAsync();
        return _latestApodCache;
    }

    private static NasaApod? _latestApodCache = null;
#nullable disable

    public class GuildNasaApodDetails(Guild guild) {
        public Guild Guild { get; set; } = guild;
        public Guid LastApodPostedId { get; set; } = Guid.Empty;
        public ulong ChannelId { get; set; } = 0;
    }

    public static async Task<GuildNasaApodDetails> GetNasaApodCache(this ApplicationDbContext db, Guild guild) {
        if(!db._cache.TryGetValue(guild.GetNASACacheKey(), out GuildNasaApodDetails cache)) {
            var latestPosted = db.NasaApods
                .Where(a => a._postedToBytes != null)
                .OrderByDescending(a => a.DateString)
                .AsEnumerable()
                .FirstOrDefault(a => a.PostedToEntries.Any(pte => pte.GuildID == guild.Id));
            cache = new GuildNasaApodDetails(guild) {
                LastApodPostedId = latestPosted?.ID ?? Guid.Empty,
                ChannelId = guild.GetChannelId(GuildChannelType.NasaApod) ?? 0,
            };
            db._cache.Set(guild.GetNASACacheKey(), cache, TimeSpan.FromDays(1));
        }
        return cache;
    }

    private static void SetNasaApodCache(this ApplicationDbContext db, Guild guild, GuildNasaApodDetails cache) {
        db._cache.Set(guild.GetNASACacheKey(), cache, TimeSpan.FromDays(1));
    }

    public static string InvalidateGuildNASACache(this ApplicationDbContext db, Guild guild) {
        db._cache.Set(guild.GetNASACacheKey(), new GuildNasaApodDetails(guild), TimeSpan.FromMilliseconds(1));
        return guild.GetNASACacheKey();
    }

    private static string GetNASACacheKey(this Guild guild) {
        return $"NasaApodCache:Guild:{guild.Id}";
    }

    private static async Task<FileAttachment?> GetFileAttachmentOrNull(this NasaApod apod, ApplicationDbContext db, ILogger logger) {
        if (!db._cache.TryGetValue(apod.GetApodImageBytesKey(), out byte[] imageBytes)) {
            var b64String = await apod.GetNasaPictureAsB64OrEmpty(logger);
            if(string.IsNullOrEmpty(b64String)) return null;
            imageBytes = Convert.FromBase64String(b64String);
            db._cache.Set(apod.GetApodImageBytesKey(), imageBytes, TimeSpan.FromDays(7));
        }
        return new FileAttachment(new MemoryStream(imageBytes), "APOD.jpeg", "Astronomy Picture of the Day");
    }

    private static string GetApodImageBytesKey(this NasaApod apod) {
        return $"NasaApodImageBytes:Apod:{apod.ID}";
    }

    public static async Task<string> GetExplanationOrEmpty(Guid postGuid, ApplicationDbContext db) {
        if (!db._cache.TryGetValue(postGuid.GetApodExplanationKey(), out string explanation)) {
            var apod = await db.NasaApods.FirstOrDefaultAsync(a => a.ID == postGuid);
            if (apod is null) return string.Empty;
            explanation = apod.Explanation;
            db._cache.Set(postGuid.GetApodExplanationKey(), explanation, TimeSpan.FromDays(7));
        }
        return explanation;
    }

    private static string GetApodExplanationKey(this Guid apodId) {
        return $"NasaApodExplanation:Apod:{apodId}";
    }

    private static async Task<CustomDiscordMessage?> GetCustomMessage(this NasaApod apod, ApplicationDbContext db, ILogger logger) {
        var attachment = await apod.GetFileAttachmentOrNull(db, logger);
        if(attachment is null || attachment is not FileAttachment fileAttachment) {
            logger.LogWarning("Failed to get NASA APOD image attachment for APOD ID: {apodId}", apod.ID);
            return null;
        }
        var apodEmbed = apod.GetEmbedBuilder().WithImageUrl($"attachment://{fileAttachment.FileName}");
        return new CustomDiscordMessage {
            Embed = apodEmbed.Build(),
            File = fileAttachment,
            SendFile = true,
            Components = apod.CreateEphemeralExplanationButton()
        };
    }

    public static async Task<bool> TrySendNasaAPOD(this GuildNasaApodDetails details, NasaApod apod, DiscordHostedService client, ApplicationDbContext db, ILogger logger) {
        var customMessage = await apod.GetCustomMessage(db, logger);
        if (customMessage is null) {
            logger.LogWarning("Failed to get NASA APOD image attachment for APOD ID: {apodId}", apod.ID);
            return false;
        }
        var sentMessage = await DetermineAndSend(client, details.Guild, GuildChannelType.NasaApod, customMessage, logger);
        if (sentMessage != null) {
            SetNasaApodCache(db, details.Guild, new GuildNasaApodDetails(details.Guild) {
                LastApodPostedId = apod.ID,
                ChannelId = details.ChannelId
            });
        }
        return sentMessage != null;
    }

    public static async Task<bool> TrySendLatestNasaAPODAdHoc(this FauxCommand command, NasaApod apod, ApplicationDbContext db, ILogger logger) {
        var customMessage = await apod.GetCustomMessage(db, logger);
        if(customMessage is null) {
            logger.LogWarning("Failed to get NASA APOD image attachment for APOD ID: {apodId}", apod.ID);
            return false;
        }
    }

    private static MessageComponent CreateEphemeralExplanationButton(this NasaApod apod) =>
        new ComponentBuilder().WithButton("Explanation", $"APODExplanation:{apod.ID}", ButtonStyle.Primary).Build();

#nullable enable
    public static async Task<NasaApod?> GetNasaApodResponseAsync(ILogger logger, CancellationToken cancellationToken) {
        string? streamContentString = null;
        try {
            using var httpClient = new HttpClient();
            logger.LogInformation("Trying to fetch from: {}", NasaApiUrl);
            var response = await httpClient.GetAsync(NasaApiUrl, cancellationToken);
            if(response is null || !response.IsSuccessStatusCode) {
                logger.LogWarning("Failed to retrieve NASA APOD. Status Code: {statusCode}", response?.StatusCode);
                return null;
            } else if(response.Content is null || response.Content.Headers.ContentLength == 0) {
                logger.LogWarning("NASA APOD response content is empty.");
                return null;
            }

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var contentBuffer = new byte[response.Content.Headers.ContentLength ?? 0];
            await contentStream.ReadExactlyAsync(contentBuffer, cancellationToken);
            streamContentString = System.Text.Encoding.UTF8.GetString(contentBuffer);
            return JsonConvert.DeserializeObject<NasaApod>(streamContentString);
        } catch (HttpRequestException ex) {
            logger.LogError("HTTP request error ({statusCode}) while fetching NASA APOD: {message}", ex.StatusCode, ex.Message);
            return null;
        } catch(JsonSerializationException ex) {
            logger.LogError("Failed to deserialize NASA APOD response: {message}\nPath: {path}\nStream content string:\n{content}", ex.Message, ex.Path, streamContentString);
            return null;
        }
    }

    private static EmbedBuilder GetEmbedBuilder(this NasaApod apod) {
        var mediaTypeImage = apod.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase);
        var isImage = mediaTypeImage || apod.Url.EndsWith(".jpg") || apod.Url.EndsWith(".jpeg") || apod.Url.EndsWith(".png") || apod.Url.EndsWith(".gif");
        var builder = new EmbedBuilder()
            .WithTitle(apod.Title)
            .WithUrl(apod.BestUrl)
            .WithFooter($"{apod.Copyright?.Replace("\n", "") ?? "[Photographer Unknown]"} | {apod.DateString}");
        if(isImage) builder.WithImageUrl(apod.BestUrl);
        else {
            if(TryExtractYouTubeId(apod.BestUrl) is string videoId) {
                var videoThumbnail = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg";
                builder.WithImageUrl(videoThumbnail);
            } else if(!string.IsNullOrEmpty(apod.ThumbnailUrl)) builder.WithImageUrl(apod.ThumbnailUrl);

            builder.WithDescription("_(Today's astronomy 'picture' is a video, click the title to watch it)_");
        }
        return builder;
    }

    private static async Task<string> GetNasaPictureAsB64OrEmpty(this NasaApod apod, ILogger logger) {
        try {
            using var client = new HttpClient();
            var response = await client.GetAsync(apod.BestUrl);
            if(response.IsSuccessStatusCode) {
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                return Convert.ToBase64String(imageBytes);
            } else return string.Empty;
        } catch (Exception e) {
            logger.LogWarning("Failed to download NASA APOD image from URL: {url}\n{stack}", apod.BestUrl, e.StackTrace);
            return string.Empty;
        }
    }
}
