using System;
using System.Collections.Generic;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Models.Home {
    public class Home_CoopModel {
        public Ei.ContractCoopStatusResponse CoopStatus { get; set; }
        public Coop DbCoop { get; set; }
        public DBContract Contract { get; set; }
        public List<Home_CoopUserInfo> UserInfos { get; set; }
        public uint League { get; set; }
        public List<Home_GoalDetails> GoalDetails { get; set; }
        public double Progress { get; set; }
        public CoopDetails CoopDetails { get; set; }
        public List<DBCustomEgg> CustomEggs { get; set; }
    }

    public class Home_CoopUserInfo {
        public Ei.ContractCoopStatusResponse.Types.ContributionInfo Contribution { get; set; }
        public CustomBackup Backup { get; set; }
        public CustomFarm Farm { get; set; }
        public UserCoopXref Xref { get; set; }
        public double Projected { get; set; }
        public double Share { get; set; }
        public double ProjectedAbsolute { get; set; }
    }

    public class Home_GoalDetails {
        public Ei.Contract.Types.Goal Goal { get; set; }
        public Home_GoalStatus Status { get; set; }
        public double Progress { get; set; }
        public TimeSpan TimeLeft { get; set; }
    }

    public enum Home_GoalStatus {
        Completed,
        Achievable,
        NotAchievable,
        Never
    }
}
