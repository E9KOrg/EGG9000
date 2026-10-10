using Ei;
using static Ei.Contract.Types;

namespace EGG9000.Site.Models.Admin {
    public record Admin_CoopCreatorRow(
        string EggIncId,
        PlayerGrade Grade,
        string Name,
        ContractPlayerInfo Info
    );
}
