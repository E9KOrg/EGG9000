
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class Guild {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string ActiveElites { get; set; }
        public string InactiveElites { get; set; }
        public string ActiveStandards { get; set; }
        public string InactiveStandards { get; set; }

        public ulong DiscordSeverId { get; set; }
        public string OverflowServersJson { get; set; }
        [NotMapped]
        public ReadOnlyCollection<ulong> OverflowServers {
            get {
                return JsonConvert.DeserializeObject<ReadOnlyCollection<ulong>>(OverflowServersJson ?? "[]");
            }
        }

        public ulong? DemeritLogChannel { get; set; }

        public string CoopNamePrefix { get; set; }

        public string StaffCoopsMessageDetails { get; set; }

        //public ulong? ChannelWarningMessageForUser { get; set; }

        public string LeaderboardImage { get; set; }
        //public ulong? EliteCategory { get; set; }
        //public ulong? StandardCategory { get; set; }
        //public ulong? WelcomeChannel { get; set; }
        //public ulong? GeneralChannel { get; set; }
        //public ulong? RulesChannel { get; set; }
        //public ulong? LeaderboardChannel { get; set; }
        //public ulong? GameEventsChannel { get; set; }
        //public ulong? FaqChannel { get; set; }
        //public ulong? FailedCategory { get; set; }
        public string CoopCategories { get; set; }
        public string FinishedCategories { get; set; }
        
        public string _channelDetailsJson { get; set; }
        [NotMapped]
        private List<ChannelDetail> _channelDetails { get; set; }
        [NotMapped]
        public List<ChannelDetail> ChannelDetails {
            get {
                if(_channelDetails == null) {
                    _channelDetails = JsonConvert.DeserializeObject<List<ChannelDetail>>(_channelDetailsJson ?? "[]");
                }
                return _channelDetails;
            }
            set {
                value.RemoveAll(x => x.Id == 0);
                _channelDetails = value;
                _channelDetailsJson = JsonConvert.SerializeObject(value);
            }
        }
    }

    [NotMapped]
    public class ChannelDetail {
        public GuildChannelType ChannelType { get; set; }
        public UInt64 Id { get; set; }
    }

    public enum GuildChannelType {
        [Description("Required: Greets new users and handles registering")]
        Welcome,
        [Description("Required: Announces new users who registered and other various messages like new rank roles")]
        General,
        [Description("Required: Rules channel you want people to read before registering")]
        Rules,
        [Description("Optional: Leaderboard of registered users")]
        Leaderboard,
        [Description("Optional: Shows in-game daily events")]
        GameEvents,
        [Description("Optional: FAQ Channel linked to when announcing new registered users")]
        FaqCategory,
        [Description("Required: Category for elite contract Channels")]
        EliteCategory,
        [Description("Optional: Category for standard contract Channels")]
        StandardCategory,
        [Description("Optional: Category for failed co-ops")]
        FailedCategory,
        [Description("Optional: Channel for warning messages like having bot DMs blocked (can be the same as another channel)")]
        WarningMessagesForUser
    }
}
