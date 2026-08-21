using System;

namespace EGG9000.Common.Contracts {
    public static class BoardingGroupLaunch {
        // CC-only (Ultra) contracts get a 4th boarding group; everything else caps at BG3.
        public static int MaxBoardingGroup(bool ccOnly) => ccOnly ? 4 : 3;

        public static (bool AlreadyLaunched, DateTimeOffset LaunchTime) GetLaunchInfo(DateTimeOffset guildContractCreated, bool ccOnly, int targetBoardingGroup, DateTimeOffset now) {
            var contractDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(guildContractCreated, "Pacific Standard Time");
            var stageIndex = Math.Min(targetBoardingGroup - 1, MaxBoardingGroup(ccOnly) - 1);
            var launchTime = contractDate - contractDate.TimeOfDay + TimeSpan.FromHours(9 + stageIndex * 8);
            return (now >= launchTime, launchTime);
        }

        // True once the final boarding group for this contract has launched.
        public static bool AllBoardingGroupsLaunched(DateTimeOffset guildContractCreated, bool ccOnly, DateTimeOffset now) =>
            GetLaunchInfo(guildContractCreated, ccOnly, MaxBoardingGroup(ccOnly), now).AlreadyLaunched;

        // The boarding group that will actually sweep up an account whose own group is `group`:
        // the earliest group at or after its own whose launch is still in the future (each launch
        // includes all lower groups). Null once every eligible group has already launched.
        public static int? NextPickupBoardingGroup(DateTimeOffset guildContractCreated, bool ccOnly, int group, DateTimeOffset now) {
            var max = MaxBoardingGroup(ccOnly);
            for(var bg = Math.Max(group, 1); bg <= max; bg++) {
                if(!GetLaunchInfo(guildContractCreated, ccOnly, bg, now).AlreadyLaunched) return bg;
            }
            return null;
        }
    }
}
