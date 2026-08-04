using EGG9000.Common.Contracts;

namespace EGG9000.Site.Models.Home {
    public class Home_EggDayResults {
        public UserByAccount UserAccount { get; set; }
        public double EBGain { get; set; }
        public double SEGain { get; set; }
        public double EBGainPercent { get; set; }
        public double SEGainPercent { get; set; }
        public double StartEB { get; set; }
        public ulong PrestigeCount { get; set; }
    }
}
