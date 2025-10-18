using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers;

public static partial class NasaHelper {
    // # Official NASA API is bugged and hasn't been updated in 14 years.
    // # It works 85% of the time, but when it goes bad, it goes really bad.
    // # Also big benefit of running our own instance is, API key is no longer needed.
    // # If for whatever reason we want to switch back to official NASA API,
    // # just change the BASE_URL and add api_key param to the Api URL.
    // public const string BASE_URL = "https://api.nasa.gov";
    private const string BASE_URL = "https://nasa.davidarthurcole.me";
    private const string APOD_ENDPOINT = "/v1/apod";
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

    public class GuildNasaApodCache {
        public Guid LastApodPostedId { get; set; } = Guid.Empty;
        public ulong ChannelId { get; set; } = 0;
    }

    public async Task<GuildNasaApodCache> GetNasaApodCache(this ApplicationDbContext db, Guild guild) {
        if(!db._cache.TryGetValue(guild.GetNASACacheKey(), out GuildNasaApodCache? cache)) {
            var latestPosted = await db.NasaApods.OrderByDescending(a => a.Date).FirstOrDefaultAsync(a => a._postedToBytes != null && a._postedToBytes.Length > 0 && a.PostedToEntries.Any(pte => pte.GuildID == guild.Id));
            db._cache.Set(guild.GetNASACacheKey(), cache, TimeSpan.FromDays(1));
        }
        return cache;
    }

    public static string InvalidateGuildNASACache(this ApplicationDbContext db, Guild guild) {
        db._cache.Set(guild.GetNASACacheKey(), new GuildNasaApodCache(), TimeSpan.FromMilliseconds(1));
        return guild.GetNASACacheKey();
    }

    private static string GetNASACacheKey(this Guild guild) {
        return $"NasaApodCache:Guild:{guild.Id}";
    }

    public static async Task<NasaApod?> GetNasaApodResponseAsync(ILogger<dynamic> _logger, CancellationToken cancellationToken) {
        string? streamContentString = null;
        try {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(NasaApiUrl, cancellationToken);
            if(response is null || !response.IsSuccessStatusCode) {
                _logger.LogWarning("Failed to retrieve NASA APOD. Status Code: {statusCode}", response?.StatusCode);
                return null;
            } else if(response.Content is null || response.Content.Headers.ContentLength == 0) {
                _logger.LogWarning("NASA APOD response content is empty.");
                return null;
            }

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var contentBuffer = new byte[response.Content.Headers.ContentLength ?? 0];
            await contentStream.ReadExactlyAsync(contentBuffer, cancellationToken);
            streamContentString = System.Text.Encoding.UTF8.GetString(contentBuffer);
            return JsonConvert.DeserializeObject<NasaApod>(streamContentString);
        } catch (HttpRequestException ex) {
            _logger.LogError("HTTP request error ({statusCode}) while fetching NASA APOD: {message}", ex.StatusCode, ex.Message);
            return null;
        } catch(JsonSerializationException ex) {
            _logger.LogError("Failed to deserialize NASA APOD response: {message}\nPath: {path}\nStream content string:\n{content}", ex.Message, ex.Path, streamContentString);
            return null;
        }
    }

    public static EmbedBuilder GetEmbedBuilder(this NasaApod apod) {
        var mediaTypeImage = apod.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase);
        var isImage = mediaTypeImage || apod.Url.EndsWith(".jpg") || apod.Url.EndsWith(".jpeg") || apod.Url.EndsWith(".png") || apod.Url.EndsWith(".gif");
        var builder = new EmbedBuilder()
            .WithTitle(apod.Title)
            .WithUrl(apod.BestUrl)
            .WithFooter($"{apod.Copyright?.Replace("\n", "") ?? "[Photographer Unknown]"} | {apod.DateString}");
        if(isImage) builder.WithImageUrl(apod.BestUrl); // Might need to be Url due to sizing constraints (?)
        else {
            if(TryExtractYouTubeId(apod.BestUrl) is string videoId) {
                var videoThumbnail = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg";
                builder.WithImageUrl(videoThumbnail);
            } else if(!string.IsNullOrEmpty(apod.ThumbnailUrl)) builder.WithImageUrl(apod.ThumbnailUrl);

            builder.WithDescription("_(Today's astronomy 'picture' is a video, click the title to watch it)_");
        }
        return builder;
    }

    public static async Task<string> GetNasaPictureAsB64OrEmpty(this NasaApod apod, ILogger logger) {
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
