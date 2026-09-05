using MessagePack;

namespace EGG9000.Common.Database.Entities;

public partial class NasaApod {
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
}
