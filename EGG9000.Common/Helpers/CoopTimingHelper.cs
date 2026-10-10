using System;
using EGG9000.Common.Database.Entities;

namespace EGG9000.Common.Helpers {
    // All configurable co-op timing math in one place so it can be unit tested
    // without Discord or database dependencies.
    public static class CoopTimingHelper {
        public const int DefaultOfflineDemeritHours = 30;
        public const int DefaultJoinTimeHours = 18;
        public const int DefaultJoinTimeUltraHours = 24;
        public const int DefaultOfflineWarningHours = 22;
        // Legacy silo-midpoint ceiling, kept only for DisableBG servers.
        public const double LegacyAlertCeilingHours = 30.0;
        // BG-disabled servers keep the original fixed empty-silo cadence.
        public const int LegacyEmptySiloDemeritHours = 18;
        public const int DefaultSiloReminderFirstHours = 12;
        public const int DefaultSiloReminderSecondHours = 24;

        public static int GetHoursToKick(Guild guild, bool ccOnly) {
            if(ccOnly)
                return guild.JoinTimeUltraHours >= 1 ? guild.JoinTimeUltraHours : DefaultJoinTimeUltraHours;
            return guild.JoinTimeHours >= 1 ? guild.JoinTimeHours : DefaultJoinTimeHours;
        }

        public static (double First, double Second) GetJoinReminderHours(int hoursToKick) {
            return (hoursToKick / 3.0, hoursToKick * 2.0 / 3.0);
        }

        public static SleepDemeritCheck EvaluateSleepDemerit(Guild guild, double hoursSleeping, double timeEmpty, int demeritsGiven) {
            if(guild.DisableBG) {
                var legacyNextAt = (demeritsGiven + 1) * LegacyEmptySiloDemeritHours;
                return new SleepDemeritCheck(timeEmpty > legacyNextAt, legacyNextAt, OfflineBased: false);
            }
            var interval = guild.OfflineDemeritHours >= 1 ? guild.OfflineDemeritHours : DefaultOfflineDemeritHours;
            var nextAt = (demeritsGiven + 1) * interval;
            return new SleepDemeritCheck(hoursSleeping > nextAt, nextAt, OfflineBased: true);
        }

        public static SleepAlertCheck EvaluateSleepAlert(Guild guild, double hoursSleeping, double siloTimeHours) {
            if(guild.DisableBG) {
                var legacyAlertAt = (LegacyAlertCeilingHours - siloTimeHours) / 2 + siloTimeHours;
                // DemeritAtHours is filled in for completeness but no caller reads it on this
                // branch: the warning DM only prints it when OfflineBased is true.
                return new SleepAlertCheck(hoursSleeping >= legacyAlertAt, legacyAlertAt,
                    siloTimeHours + LegacyEmptySiloDemeritHours, OfflineBased: false);
            }
            var alertAt = guild.OfflineWarningHours >= 1 ? guild.OfflineWarningHours : DefaultOfflineWarningHours;
            var threshold = guild.OfflineDemeritHours >= 1 ? guild.OfflineDemeritHours : DefaultOfflineDemeritHours;
            return new SleepAlertCheck(hoursSleeping >= alertAt, alertAt, threshold, OfflineBased: true);
        }

        public static SiloReminderCheck EvaluateSiloReminder(Guild guild, int silosOwned, int permitLevel,
            double hoursSinceJoined, bool firstSent, bool secondSent) {
            var maxSilos = Research.GetMaxSilos(permitLevel);
            var missing = Math.Max(maxSilos - silosOwned, 0);

            if(!guild.SiloRemindersEnabled || missing == 0)
                return new SiloReminderCheck(SiloReminderStage.None, maxSilos, missing, 0);

            var firstHours = guild.SiloReminderFirstHours >= 1 ? guild.SiloReminderFirstHours : DefaultSiloReminderFirstHours;
            var secondHours = guild.SiloReminderSecondHours >= 1 ? guild.SiloReminderSecondHours : DefaultSiloReminderSecondHours;

            // Second is checked before first on purpose. If the bot was down past both thresholds
            // the player gets one second reminder, not a first followed a minute later by a second.
            // Same ordering as the join reminders in ThreadsCoopStatusUpdater.ProcessCoop.cs.
            if(!secondSent && hoursSinceJoined >= secondHours)
                return new SiloReminderCheck(SiloReminderStage.Second, maxSilos, missing, secondHours);
            if(!firstSent && hoursSinceJoined >= firstHours)
                return new SiloReminderCheck(SiloReminderStage.First, maxSilos, missing, firstHours);

            return new SiloReminderCheck(SiloReminderStage.None, maxSilos, missing, 0);
        }
    }

    public record SleepDemeritCheck(bool ShouldDemerit, int NextDemeritAtHours, bool OfflineBased);
    public record SleepAlertCheck(bool ShouldAlert, double AlertAtHours, double DemeritAtHours, bool OfflineBased);
    public enum SiloReminderStage { None, First, Second }
    public record SiloReminderCheck(SiloReminderStage Stage, int MaxSilos, int MissingSilos, int ThresholdHours);
}
