using EGG9000.Common.Database.Entities;

using Microsoft.Extensions.Logging;

using System;

namespace EGG9000.Common.Helpers {
    public static class GradeSync {
        public static bool ShouldUpdateGrade(Ei.Contract.Types.PlayerGrade current, Ei.Contract.Types.PlayerGrade fetched, bool guardUnset) {
            if(fetched == current) return false;
            if(guardUnset && fetched == Ei.Contract.Types.PlayerGrade.GradeUnset) return false;
            return true;
        }

        public static bool ApplyGradeChange(DBUser user, EggIncAccount account, Ei.Contract.Types.PlayerGrade fetched,
            bool setPromotionTime, bool guardUnset, ILogger logger) {
            if(!ShouldUpdateGrade(account.LastGrade, fetched, guardUnset)) return false;
            logger.LogInformation("Updating grade for {User} ({Account}) from {Old} to {New}", user.DiscordUsername, account.Name, account.LastGrade, fetched);
            if(setPromotionTime) account.PromotionTime = DateTimeOffset.UtcNow;
            account.LastGrade = fetched;
            user.UpdateAccounts();
            return true;
        }
    }
}
