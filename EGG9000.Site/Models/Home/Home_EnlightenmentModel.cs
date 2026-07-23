using System.Collections.Generic;
using EGG9000.Common.Database.Entities;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Models.Home {
    public record Home_EnlightenmentModel(List<LeaderboardUser> Users, List<DBCustomEgg> CustomEggs);
}
