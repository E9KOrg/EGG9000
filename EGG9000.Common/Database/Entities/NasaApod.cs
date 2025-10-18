
using MessagePack;
using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EGG9000.Common.Database.Entities;

[Table("NasaApods")]
public class NasaApod {
    // There's no ID for APOD (thanks NASA!), so we hash the url and the title to ID it
    [System.ComponentModel.DataAnnotations.Key]
    public Guid ID {
        get {
            if (_idCache == Guid.Empty) {
                var inputBytes = Encoding.UTF8.GetBytes($"{Url}|{Title}");
                var hashBytes = System.Security.Cryptography.SHA1.HashData(inputBytes);
                _idCache = new Guid([.. hashBytes.Take(16)]);
            }
            return _idCache;
        }
    }
    private Guid _idCache = Guid.Empty;

    // Actual properties from NASA APOD API
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

    // Our own storage properties
    [JsonIgnore]
    public byte[] _postedToBytes { get; set; }
    [NotMapped]
    private PostedToEntry[] _postedToEntries { get; set; }
    [NotMapped]
    public PostedToEntry[] PostedToEntries {
        get {
            if (_postedToEntries != null) return _postedToEntries;
            if (_postedToBytes == null || _postedToBytes.Length == 0) return [];
            _postedToEntries = MessagePackSerializer.Deserialize<PostedToEntry[]>(_postedToBytes);
            return _postedToEntries;
        }
        set {
            _postedToEntries = value;
            _postedToBytes = MessagePackSerializer.Serialize(value);
        }
    }

    [MessagePackObject]
    public class PostedToEntry {
        public PostedToEntry() { }

        public PostedToEntry(Guild dbGuild, ulong channelId = 0) {
            GuildID = dbGuild.Id;
            ChannelID = dbGuild.GetChannelId(GuildChannelType.NasaApod) ?? channelId;
        }

        public PostedToEntry(ulong guildId, ulong channelId) {
            GuildID = guildId;
            ChannelID = channelId;
        }

        [Key(0)]
        public ulong GuildID { get; set; }
        [Key(1)]
        public ulong ChannelID { get; set; }
    }


    [JsonIgnore]
    [NotMapped]
    public string BestUrl {
        get {
            if (_bestUrlCache == string.Empty) {
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
            if (_dateCache == DateTimeOffset.MinValue) {
                _dateCache = DateTimeOffset.ParseExact(DateString, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return _dateCache;
        }
    }
    private DateTimeOffset _dateCache = DateTimeOffset.MinValue;
}
