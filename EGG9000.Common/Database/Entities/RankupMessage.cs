using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace EGG9000.Common.Database.Entities {
    public class RankupMessage {

        public const int GlobalPool = -1;

        [Key]
        public string InternalId { get; set; }

        public ulong GuildId { get; set; }
        public string GuildName { get; set; } = "";

        // GlobalPool (-1) = the global pool; otherwise a group base oom (0, 3, 6, ... 48, 51).
        public int GroupBaseOom { get; set; } = GlobalPool;

        // Template text: {{user}} {{rank}} {{eb}} {{oom}} {{emoji:name}} {{command:name}}
        public string Text { get; set; } = "";

        public int Weight { get; set; } = 1;

        public bool PalaceOnly { get; set; } = false;

        public string _subscribedGuildIds { get; set; } = "";
        [NotMapped]
        public List<ulong> SubscribedGuildIds {
            get {
                if(string.IsNullOrEmpty(_subscribedGuildIds)) return [];
                return [.. _subscribedGuildIds.Split(",").Select(ulong.Parse)];
            }
            set {
                _subscribedGuildIds = string.Join(",", value);
            }
        }

        public string CreatedByIdString { get; set; } = "";
        public ulong CreatedById {
            get {
                if(!ulong.TryParse(CreatedByIdString, out var id)) id = ulong.MaxValue;
                return id;
            }
            set {
                CreatedByIdString = value.ToString();
            }
        }
        public string CreatedBy { get; set; } = "";

        // Palace messages (owned by the palace guild) apply to every guild by default - they replace the
        // hardcoded defaults everyone used to get. This intentionally differs from FAQ's opt-in subscription model.
        public bool AppliesToGuild(Guild guild, ulong palaceGuildId) {
            if(GuildId == guild.DiscordSeverId || GuildId == guild.Id) return true;
            if(GuildId == palaceGuildId) return true;
            if(PalaceOnly) return false;
            return SubscribedGuildIds.Contains(guild.Id);
        }
    }
}
