using EGG9000.Common.JsonData;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarmsModel(
        DBUser User,
        List<DBContract> Contracts,
        List<Demerit> Demerits,
        List<Merit> Merits,
        List<UserSnapShot> SnapShots,
        List<UserCoopXref> UnjoinedCoops,
        List<Coop> JoinedCoops,
        List<EpicResearchItem> EpicResearchConfig,
        List<(string EggIncId, Ei.MyContracts MyContracts)> Scoring,
        Guild DBGuild,
        Dictionary<string, List<DBContract>> UncompletedPEContracts,
        List<DBCustomEgg> CustomEggs,
        bool IsSelf,
        FrozenSet<Ei.Contract> CachedContracts,
        Dictionary<string, (int Earned, int Max)> SeasonPEByEggIncId,
        Dictionary<string, List<MissingSeasonalPe>> MissingSeasonalPEByEggIncId
    ) {
        public EggIncAccount AccountAt(int index) => User.EggIncAccounts[index];
    }

    public record MissingSeasonalPe(
        string SeasonName,
        double CurrentCxp,
        double GoalCxp,
        int PeAmount,
        DateTimeOffset StartTime
    );


    public record MyFarms_Partial_BaseModel(
        EggIncAccount account,
        int index
    );
}