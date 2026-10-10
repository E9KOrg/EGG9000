using System.Collections.Generic;
using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database.Entities;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Models.Contract {
    public class Contract_CoopsViewModel {
        public List<Coop> Coops { get; set; }
        public GuildContract GuildContract { get; set; }
        public CoopsBreakdown CoopsBreakdown { get; set; }
        public List<UserPreFarm> UserPreFarms { get; set; }
        public uint League { get; set; }
        // Untracked guild row the page was loaded for. Settings are read from it at render time.
        // Null when the guild has no DB row.
        public Guild Guild { get; set; }

        // Label for a coop's group badge, or null when the coop has no group to show.
        // With BGs off, Coop.Group holds a group role ID. With BGs on it holds a BG number, where 0
        // (/addcoop, test coops) and uint.MaxValue (allow-all-grades marker) are not real BGs.
        public string GroupLabel(Coop coop, SocketGuild discordGuild) {
            if(Guild?.DisableBG ?? false) return discordGuild.GetRole(coop.Group)?.Name;
            var maxBoardingGroup = (ulong)BoardingGroupLaunch.MaxBoardingGroup(GuildContract.Contract.cc_only);
            return coop.Group >= 1 && coop.Group <= maxBoardingGroup ? $"BG{coop.Group}" : null;
        }
    }
}
