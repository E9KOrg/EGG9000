using System;

namespace EGG9000.Common.Contracts {
    public static class BoardingGroupLaunch {
        // Only Ultra contracts launched on a Friday get a 4th boarding group (they share the launch
        // slot with normal contracts); everything else caps at BG3.
        public static int MaxBoardingGroup(DateTimeOffset guildContractCreated, bool ccOnly) {
            var contractDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(guildContractCreated, "Pacific Standard Time");
            return (ccOnly && contractDate.DayOfWeek == DayOfWeek.Friday) ? 4 : 3;
        }

        public static (bool AlreadyLaunched, DateTimeOffset LaunchTime) GetLaunchInfo(DateTimeOffset guildContractCreated, bool ccOnly, int targetBoardingGroup, DateTimeOffset now) {
            var contractDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(guildContractCreated, "Pacific Standard Time");
            var stageIndex = Math.Min(targetBoardingGroup - 1, MaxBoardingGroup(guildContractCreated, ccOnly) - 1);
            var launchTime = contractDate - contractDate.TimeOfDay + TimeSpan.FromHours(9 + stageIndex * 8);
            return (now >= launchTime, launchTime);
        }
    }
}
