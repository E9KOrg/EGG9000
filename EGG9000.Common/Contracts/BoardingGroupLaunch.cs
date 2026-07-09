using System;

namespace EGG9000.Common.Contracts {
    public static class BoardingGroupLaunch {
        public static (bool AlreadyLaunched, DateTimeOffset LaunchTime) GetLaunchInfo(DateTimeOffset guildContractCreated, bool ccOnly, int targetBoardingGroup, DateTimeOffset now) {
            var contractDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(guildContractCreated, "Pacific Standard Time");
            var maxBoardingGroup = (ccOnly && contractDate.DayOfWeek == DayOfWeek.Friday) ? 4 : 3;
            var stageIndex = Math.Min(targetBoardingGroup - 1, maxBoardingGroup - 1);
            var launchTime = contractDate - contractDate.TimeOfDay + TimeSpan.FromHours(9 + stageIndex * 8);
            return (now >= launchTime, launchTime);
        }
    }
}
