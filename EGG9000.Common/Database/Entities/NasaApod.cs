using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EGG9000.Common.Database.Entities;

[Table("NasaApods")]
public partial class NasaApod {
    // There's no ID for APOD (thanks NASA!), so we hash the url and the title to ID it
    [Key]
    public Guid ID {
        get {
            if(_idCache == Guid.Empty) {
                var inputBytes = Encoding.UTF8.GetBytes($"{Url}|{Title}");
                var hashBytes = SHA256.HashData(inputBytes);
                _idCache = new Guid([.. hashBytes.Take(16)]);
            }
            return _idCache;
        }
        private set { _idCache = value; }
    }
    private Guid _idCache = Guid.Empty;

    [JsonProperty("title")]
    public string Title { get; set; }
    [JsonProperty("url")]
    public string Url { get; set; }
#nullable enable
    [JsonProperty("hdurl")]
    public string? HdUrl { get; set; }
    [JsonProperty("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }
#nullable disable
    [JsonProperty("media_type")]
    public string MediaType { get; set; }
    [JsonProperty("date")]
    public string DateString { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    [JsonProperty("explanation")]
    public string Explanation { get; set; }
#nullable enable
    [JsonProperty("copyright")]
    public string? Copyright { get; set; }
#nullable disable

    [JsonIgnore]
    public byte[] _postedToBytes { get; set; }
    [NotMapped]
    private readonly MessagePackBlobAccessor<PostedToEntry[]> _postedTo = new(whenNull: () => []);
    [NotMapped]
    private readonly Lock _postedToLock = new();
    [NotMapped]
    public PostedToEntry[] PostedToEntries {
        get {
            lock(_postedToLock)
                return _postedTo.Get(_postedToBytes);
        }
        set {
            lock(_postedToLock)
                _postedToBytes = _postedTo.Set(value ?? [], _postedToBytes);
        }
    }

    [JsonIgnore]
    [NotMapped]
    public string BestUrl {
        get {
            if(_bestUrlCache == string.Empty) {
                _bestUrlCache = string.IsNullOrEmpty(HdUrl) ? Url : HdUrl;
            }
            return _bestUrlCache;
        }
    }
    private string _bestUrlCache = string.Empty;

    [JsonIgnore]
    [NotMapped]
    public DateTimeOffset Date {
        get {
            if(_dateCache == DateTimeOffset.MinValue) {
                _dateCache = DateTimeOffset.ParseExact(DateString, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return _dateCache;
        }
    }
    private DateTimeOffset _dateCache = DateTimeOffset.MinValue;
}
