using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

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

        [GuildConfig("Co-op Name Prefix", "Text", GuildConfigKind.String, Description = "Prefix for auto-generated co-op names")]
        public string CoopNamePrefix { get; set; }

        public string StaffCoopsMessageDetails { get; set; }

        public string LeaderboardImage { get; set; }
        [GuildConfig("Co-op Categories", "Lists", GuildConfigKind.CsvCategories, Description = "Categories new co-op channels are created under")]
        public string CoopCategories { get; set; }
        [GuildConfig("Finished Categories", "Lists", GuildConfigKind.CsvCategories, Description = "Categories finished co-op channels move to")]
        public string FinishedCategories { get; set; }

	    [GuildConfig("Minimum Running Score", "Numbers", GuildConfigKind.Float, Description = "Running-score threshold for slacker detection")]
	    public float MinimumRunningScore { get; set; }
        [GuildConfig("Add Outside Co-ops", "Toggles", GuildConfigKind.Bool, Description = "Add outside co-ops discovered from backups")]
        public bool AddOutsideCoops { get; set; } = true;

        public string _coopSettingsJson { get; set; }
        [NotMapped]
        private List<ServerCoopSetting> _coopSettings { get; set; }
        [NotMapped]
        public List<ServerCoopSetting> CoopSettings {
            get {
                _coopSettings ??= JsonConvert.DeserializeObject<List<ServerCoopSetting>>(_coopSettingsJson ?? "[]");
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
        public List<EventCustomization> EventCustomizations {
            get {
                _eventCustomizations ??= JsonConvert.DeserializeObject<List<EventCustomization>>(_eventCustomizationsJson ?? "[]");
                return _eventCustomizations;
            }
            set {
                _eventCustomizations = value;
                _eventCustomizationsJson = JsonConvert.SerializeObject(value);
            }
        }

        public string _faqTopicsJson { get; set; }
        [NotMapped]
        private List<FAQTopic> _faqTopics { get; set; }
        [GuildConfig("FAQ Topics Enabled", "Toggles", GuildConfigKind.Bool, Description = "Enable the FAQ topics feature")]
        public bool FAQTopicsEnabled { get; set; }
        [GuildConfig("FAQ Topic Cooldown (min)", "Numbers", GuildConfigKind.Int, Description = "Minutes between FAQ posts in a channel")]
        public int FAQTopicCooldownMinutes { get; set; }

        public string _channelDetailsJson { get; set; }
        [NotMapped]
        private List<ChannelDetail> _channelDetails { get; set; }
        [NotMapped]
        public List<ChannelDetail> ChannelDetails {
            get {
                _channelDetails ??= JsonConvert.DeserializeObject<List<ChannelDetail>>(_channelDetailsJson ?? "[]");
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
        public ulong? GetChannelId(GuildChannelType channelType) {
            return ChannelDetails.FirstOrDefault(x => x.ChannelType == channelType)?.Id;
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
        [GuildConfig("Roles to Sync", "Lists", GuildConfigKind.CsvRoles, Description = "Roles synced to overflow servers")]
        public string RolesToSync { get; set; }
        [GuildConfig("Disable Boarding Groups", "Toggles", GuildConfigKind.Bool, Description = "Disable boarding-group staggering")]
        public bool DisableBG { get; set; }
        [GuildConfig("Allow Guilds", "Toggles", GuildConfigKind.Bool, Description = "Let in-game guild members team up")]
        public bool AllowGuilds { get; set; }
        [GuildConfig("Group Roles", "Lists", GuildConfigKind.CsvRoles, Description = "Boarding-group roles")]
        public string GroupRoles { get; set; }
        [GuildConfig("Public Score Grid", "Toggles", GuildConfigKind.Bool, Description = "Let everyone view the score grid")]
        public bool PublicScoreGrid { get; set; }
        [GuildConfig("Remove Find Coop Spot", "Toggles", GuildConfigKind.Bool, Description = "Hide Find Coop Spot buttons")]
        public bool RemoveFindCoopSpot { get; set; }
        [GuildConfig("Show Contract Stats Embeds", "Toggles", GuildConfigKind.Bool, Description = "Show a live co-op stats embed inside each contract channel")]
        public bool ShowContractStatsEmbeds { get; set; }
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
    }

    /*
     * Start a property with the following to indicate..:
     * 
     * "/TC/" The ID can either use a channel or a thread - assumed to be channel-only otherwise
     * "/R/" The ID represents a role that is fillable
     * 
     * "Required: " The option is required for the bot to function normally
     * "Optional: " The option is not required, but a QOL
     */
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
        [Description("/R/Limited time shells notification role")]
        LimitedTimeShellsRole = 11,
        [Description("/TC/Optional: Outside Co-op Log")]
        OutsideCoopLog = 12,
        [Description("/R/Missing Boarding Group Role")]
        MissingBoardingGroupRole = 14,
        [Description("/R/Active Role (participated in a co-op in the last 3 weeks)")]
        ActiveRole = 15,
        [Description("/R/Grade AAA Role")]
        GradeAAA = 16,
        [Description("/R/Grade AA Role")]
        GradeAA = 17,
        [Description("/R/Grade A Role")]
        GradeA = 18,
        [Description("/R/Grade B Role")]
        GradeB = 19,
        [Description("/R/Grade C Role")]
        GradeC = 20,
        [Description("/R/Game Version Outdated Role")]
        GameVersionOutdated = 21,
        [Description("/TC/Optional: Demerit Log, adding this channel will automate demerits in co-ops")]
        DemeritLogChannel = 22,
        [Description("/R/'Android' Role")]
        AndroidRole = 24,
        [Description("/R/'iOS/Apple' Role")]
        IosRole = 25,
        [Description("/R/'Enlightenment Diamond' Role")]
        EnDRole = 26,
        [Description("/R/'Nobel prize in Animal Husbandry' Role")]
        NAHRole = 27,
        [Description("/R/'All-Star Club' Role")]
        ASCRole = 28,
        [Description("/TC/Optional: Where /callstaff messages will appear")]
        CallStaffChannel = 29,
        [Description("Optional: Where private /callstaff threads will be created, needs to be a channel accessible to everyone")]
        PrivateCallStaff = 45,
        [Description("/R/Role for staff to ping in /callstaff instances")]
        CallStaffTagRole = 30,
        [Description("/R/Role for standard subscriptions")]
        StandardSubscription = 31,
        [Description("/R/Role for pro subscriptions")]
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
        [Description("/R/Role for users that have the Standard Permit (must be paired with Pro Permit role)")]
        StandardPermitRole = 42,
        [Description("/R/Role for users that have the Pro Permit (must be paired with Standard Permit role)")]
        ProPermitRole = 43,
        /*[Description("/R/Users with this role will be added to all coop threads")]
        AllCoopsRole = 44*/
        [Description("/TC/Optional: Where NASA Astronomy Pictures of the Day (APOD) will be posted")]
        NasaApod = 46,
        [Description("/TC/Optional: Bot Log, gives status updates when the bot detects new contract and launches boarding groups")]
        BotLog = 47,
        [Description("/TC/Optional: Where server-wide co-op stats will be posted and kept updated")]
        CoopStatsChannel = 48,
    }
}
