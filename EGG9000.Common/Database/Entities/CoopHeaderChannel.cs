using System;

namespace EGG9000.Common.Database.Entities {
    public class CoopHeaderChannel {
        public ulong GuildId { get; set; }
        public string ContractID { get; set; }
        public uint League { get; set; }
        public ulong ServerId { get; set; }
        public int ChannelIndex { get; set; }

        public ulong ChannelId { get; set; }
        public ulong WebhookId { get; set; }
        public string WebhookToken { get; set; }
        public DateTimeOffset Created { get; set; }
    }
}
