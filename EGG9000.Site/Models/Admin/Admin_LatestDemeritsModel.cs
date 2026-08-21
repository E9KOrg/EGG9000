using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public record Admin_LatestDemeritsModel(
        List<Demerit> Demerits,
        string GuildName,
        int Count,
        bool Limited
    );
}
