
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

        //public ulong? DemeritLogChannel { get; set; }

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
        public bool HasChannel(GuildChannelType channelType) {
            return ChannelDetails.Any(x => x.ChannelType == channelType && x.Id > 0);
        }
        public string RolesToSync { get; set; }
        public bool DisableBG { get; set; }
        public bool AllowGuilds { get; set; }
        public string GroupRoles { get; set; }
        public bool PublicScoreGrid { get; set; }
    }

    [NotMapped]
    public class ChannelDetail {
        public GuildChannelType ChannelType { get; set; }
        public UInt64 Id { get; set; }
    }

    public enum GuildChannelType {
        [Description("Required: Greets new users and handles registering")]
        Welcome = 0,
        [Description("Required: Announces new users who registered and other various messages like new rank roles")]
        General = 1,
        [Description("Optional: Separate channel for rank-up messages. If not filled, will use 'General'")]
        AltRankup = 23,
        [Description("Required: Rules channel you want people to read before registering")]
        Rules = 2,
        [Description("Optional: Leaderboard of registered users")]
        Leaderboard = 3,
        [Description("Optional: Shows in-game daily events")]
        GameEvents = 4,
        [Description("Optional: FAQ Channel linked to when announcing new registered users")]
        FaqChannel = 5,
        [Description("Required: Category for contract channels")]
        ContractCategory = 6,
        [Description("Optional: Category for failed co-ops")]
        FailedCategory = 8,
        [Description("Optional: Channel for warning messages like having bot DMs blocked (can be the same as another channel)")]
        WarningMessagesForUser = 9,
        [Description("Optional: Shows limited time shells")]
        LimitedTimeShells = 10,
        [Description("Optional: Limited time shells notification role")]
        LimitedTimeShellsRole = 11,
        [Description("Optional: Outside Co-op Log")]
        OutsideCoopLog = 12,
        [Description("Optional: Missing Boarding Group Role")]
        MissingBoardingGroupRole = 14,
        [Description("Optional: Active Role (participated in a co-op in the last 3 weeks)")]
        ActiveRole = 15,
        [Description("Optional: Grade AAA Role")]
        GradeAAA = 16,
        [Description("Optional: Grade AA Role")]
        GradeAA = 17,
        [Description("Optional: Grade A Role")]
        GradeA = 18,
        [Description("Optional: Grade B Role")]
        GradeB = 19,
        [Description("Optional: Grade C Role")]
        GradeC = 20,
        [Description("Optional: Game Version Outdated Role")]
        GameVersionOutdated = 21,
        [Description("Optional: Demerit Log, adding this channel will automate demerits in co-ops")]
        DemeritLogChannel = 22,
        [Description("Optional: 'Android' Role")]
        AndroidRole = 24,
        [Description("Optional: 'iOS/Apple' Role")]
        IosRole = 25,
        [Description("Optional: 'Enlightenment Diamond' Role")]
        EnDRole = 26,
        [Description("Optional: 'Nobel prize in Animal Husbandry' Role")]
        NAHRole = 27,
        [Description("Optional: 'All-Star Club' Role")]
        ASCRole = 28,
        [Description("Optional: Channel for /callstaff messages")]
        CallStaffChannel = 29,
        [Description("Optional: Role for staff to ping in /callstaff instances")]
        CallStaffTagRole = 30,
        [Description("Optional: Role for standard subscriptions")]
        StandardSubscription = 31,
        [Description("Optional: Role for pro subscriptions")]
        ProSubscription = 32,
        [Description("Optional: Subscription-Only Contract Category, adding this will prevent sub-only contracts from appearing elsewhere.")]
        SubscriptionContractCategory = 33
    }
}
