using Discord;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EGG9000.Common.Database.Entities {
    public class FAQTopic {

        [Key]
        public string InternalId { get; set; }

        public string Name { get; set; } = "";
        public string _keywords { get; set; } = "";
        [NotMapped]
        public List<string> Keywords {
            get {
                return JsonConvert.DeserializeObject<List<string>>(_keywords);
            }
        }
        public int Weight { get; set; } = 0;
        public string Explanation { get; set; } = "";
        public bool StaffOnly { get; set; } = false;
        public bool PalaceOnly { get; set; } = false;

        public string CreatedByIdString { get; set; } = "";
        public ulong CreatedById {
            get {
                if(!ulong.TryParse(CreatedByIdString, out var id)) {
                    id = ulong.MaxValue;
                }
                return id;
            }
            set {
                CreatedByIdString = value.ToString();
            }
        }

        
        public string CreatedBy { get; set; } = "";

        public string GuildName { get; set; } = "";
        public string GuildIdString { get; set; } = "";
        public ulong GuildId { 
            get {
                if (!ulong.TryParse(GuildIdString, out var id)) {
                    id = ulong.MaxValue;
                }
                return id;
            }
            set {
                GuildIdString = value.ToString();
            }
        }
        public string _subscribedGuildIds { get; set; } = "";
        [NotMapped]
        public List<ulong> SubscribedGuildIds {
            get {
                if(string.IsNullOrEmpty(_subscribedGuildIds)) return [];
                return _subscribedGuildIds.Split(",").ToList()
                    .Select(ulong.Parse).ToList();
            }
            set {
                _subscribedGuildIds = string.Join(",", value);
            }
        }

        public string EmbedColorHex { get; set; } = "";

        [NotMapped]
        public Color EmbedColor {
            get {
                if(string.IsNullOrEmpty(EmbedColorHex) || !Regex.IsMatch(EmbedColorHex, @"^#?[0-9a-fA-F]{6}")) return Color.DarkerGrey;
                else return new Color(uint.Parse(EmbedColorHex.Replace("#", ""), NumberStyles.HexNumber));
            }
        }

        public bool PalaceFAQAppliesToGuild(Guild guild) {
            if(GuildId == guild.DiscordSeverId || GuildId == guild.Id) return true;
            if(PalaceOnly) return false;
            return SubscribedGuildIds.Contains(guild.Id);
        }

        public string ImageUrl { get; set; } = "";
    }
}
