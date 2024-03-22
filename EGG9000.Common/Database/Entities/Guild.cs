
using EGG9000.Common.Migrations;
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

        public bool AddOutsideCoops { get; set; } = true;

        public string _coopSettingsJson { get; set; }
        [NotMapped]
        private List<ServerCoopSetting> _coopSettings { get; set; }
        [NotMapped]
        public List<ServerCoopSetting> CoopSettings {
            get {
                if(_coopSettings == null) {
                    _coopSettings = JsonConvert.DeserializeObject<List<ServerCoopSetting>>(_coopSettingsJson ?? "[]");
                }
                return _coopSettings;
            }
            set {
                value.RemoveAll(x => !x.Enabled && !x.Locked);
                _coopSettings = value;
                _coopSettingsJson = JsonConvert.SerializeObject(value);
            }
        }

        public string _eventCustomizationsJson { get; set; }
        [NotMapped]
        private List<EventCustomization> _eventCustomizations { get; set; }
        [NotMapped]
        public List<EventCustomization> EventCustomzations {
            get {
                if(_eventCustomizations == null) {
                    _eventCustomizations = JsonConvert.DeserializeObject<List<EventCustomization>>(_eventCustomizationsJson ?? "[]");
                }
                return _eventCustomizations;
            }
            set {
                _eventCustomizations = value;
                _eventCustomizationsJson = JsonConvert.SerializeObject(value);
            }
        }

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
        public ServerCoopSetting GetCoopSetting(GuildCoopSetting coopSetting) {
            return CoopSettings.FirstOrDefault(s => s.CoopSetting == coopSetting) ?? new ServerCoopSetting { CoopSetting = coopSetting };
        }
        public bool IsLockedAndEnabled(GuildCoopSetting coopSetting) {
            var setting = CoopSettings.FirstOrDefault(s => s.CoopSetting == coopSetting);
            return setting != null && setting.Enabled && setting.Locked;
        }
        public bool IsLockedAndDisabled(GuildCoopSetting coopSetting) {
            var setting = CoopSettings.FirstOrDefault(s => s.CoopSetting == coopSetting);
            return setting != null && !setting.Enabled && setting.Locked;
        }
        public string RolesToSync { get; set; }
        public bool DisableBG { get; set; }
        public bool AllowGuilds { get; set; }
        public string GroupRoles { get; set; }
        public bool PublicScoreGrid { get; set; }
        public bool RemoveFindCoopSpot { get; set; }
    }

    [NotMapped]
    public class ServerCoopSetting {
        public GuildCoopSetting CoopSetting { get; set; }
        public bool Enabled { get; set; } = false;
        public bool Locked { get; set; } = false;
    }

    public enum GuildCoopSetting {
        [Description("All assigned members have joined the co-op")]
        PingOnFull = 0,
        [Description("Highest assigned EB has joined")]
        PingOnHighestEB = 1,
        [Description("Co-op has finished")]
        PingOnFinished = 2,
        [Description("Co-op is cleared for exit")]
        PingOnEveryoneCheckedIn = 3,
        [Description("Any non-bot message is sent in channel")]
        PingOnMessage = 4,
        [Description("Additional DM alongside the standard @mention in the co-op channel")]
        PingOnCoopCreated = 5,
        [Description("Get notified when someone adds/removes a Tachyon Deflector")]
        PingOnTachyonChange = 6,
        [Description("Get notified when your co-op will complete as soon as everyone checks in")]
        PingOnCompleteOnCheckIn = 7
    }

    [NotMapped]
    public class ChannelDetail {
        public GuildChannelType ChannelType { get; set; }
        public ulong Id { get; set; }
        public bool ThreadAndChannel { get; set; } = false;
    }

    public enum GuildChannelType {
        [Description("Required: Greets new users and handles registering")]
        Welcome = 0,
        [Description("/TC/Required: Announces new users who registered and other various messages like new rank roles")]
        General = 1,
        [Description("/TC/Optional: Separate channel for rank-up messages. If not filled, will use 'General'")]
        AltRankup = 23,
        [Description("Required: Rules channel you want people to read before registering")]
        Rules = 2,
        [Description("Optional: Leaderboard of registered users")]
        Leaderboard = 3,
        [Description("Optional: Shows in-game daily events")]
        GameEvents = 4,
        [Description("/TC/Optional: FAQ Channel linked to when announcing new registered users")]
        FaqChannel = 5,
        [Description("Required: Category for contract channels")]
        ContractCategory = 6,
        [Description("Required: Category for failed co-ops")]
        FailedCategory = 8,
        [Description("/TC/Optional: Channel for warning messages like having bot DMs blocked (can be the same as another channel)")]
        WarningMessagesForUser = 9,
        [Description("Optional: Shows limited time shells")]
        LimitedTimeShells = 10,
        [Description("/R/Optional: Limited time shells notification role")]
        LimitedTimeShellsRole = 11,
        [Description("/TC/Optional: Outside Co-op Log")]
        OutsideCoopLog = 12,
        [Description("/R/Optional: Missing Boarding Group Role")]
        MissingBoardingGroupRole = 14,
        [Description("/R/Optional: Active Role (participated in a co-op in the last 3 weeks)")]
        ActiveRole = 15,
        [Description("/R/Optional: Grade AAA Role")]
        GradeAAA = 16,
        [Description("/R/Optional: Grade AA Role")]
        GradeAA = 17,
        [Description("/R/Optional: Grade A Role")]
        GradeA = 18,
        [Description("/R/Optional: Grade B Role")]
        GradeB = 19,
        [Description("/R/Optional: Grade C Role")]
        GradeC = 20,
        [Description("/R/Optional: Game Version Outdated Role")]
        GameVersionOutdated = 21,
        [Description("/TC/Optional: Demerit Log, adding this channel will automate demerits in co-ops")]
        DemeritLogChannel = 22,
        [Description("/R/Optional: 'Android' Role")]
        AndroidRole = 24,
        [Description("/R/Optional: 'iOS/Apple' Role")]
        IosRole = 25,
        [Description("/R/Optional: 'Enlightenment Diamond' Role")]
        EnDRole = 26,
        [Description("/R/Optional: 'Nobel prize in Animal Husbandry' Role")]
        NAHRole = 27,
        [Description("/R/Optional: 'All-Star Club' Role")]
        ASCRole = 28,
        [Description("/TC/Optional: Where /callstaff messages will appear")]
        CallStaffChannel = 29,
        [Description("/R/Optional: Role for staff to ping in /callstaff instances")]
        CallStaffTagRole = 30,
        [Description("/R/Optional: Role for standard subscriptions")]
        StandardSubscription = 31,
        [Description("/R/Optional: Role for pro subscriptions")]
        ProSubscription = 32,
        [Description("Optional: Subscription-Only Contract Category, adding this will prevent sub-only contracts from appearing elsewhere.")]
        SubscriptionContractCategory = 33,
        [Description("Optional: Subscription-Only Event Channel, adding this will prevent sub-only events from appearing elsewhere.")]
        SubscriptionGameEvents = 34,
        [Description("/TC/Optional: Merit Log, all merits added to users will appear in this channel")]
        MeritLogChannel = 35,
        [Description("Optional: Thread ID where messages will show up if previously banned EI numbers are used in /register")]
        BannedUserThread = 36,
        [Description("/TC/Optional: Where potential cheaters will be outed.")]
        CheaterThread = 37,
        /*[Description("Optional: Channel ID where non-ultra members will be pinged if an ultra contract appears that they have not completed")]
        UnobtainedUltraChannel = 38*/
        [Description("/TC/Optional: Where changes in players' ULTRA status will be logged")]
        UltraLog = 39,
        [Description("/TC/Optional: Where players who join coops while on break will be logged")]
        BreakCoopLog = 40,
        [Description("/TC/Optional: Where players can talk to staff of the server")]
        TalkToStaff = 41,
        [Description("/R/Optional: Role for users that have the Standard Permit (must be paired with Pro Permit role)")]
        StandardPermitRole = 42,
        [Description("/R/Optional: Role for users that have the Pro Permit (must be paired with Standard Permit role)")]
        ProPermitRole = 43
    }
}
