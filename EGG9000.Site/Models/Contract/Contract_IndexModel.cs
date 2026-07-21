using System.Collections.Generic;
using EGG9000.Common.Database.Entities;

namespace EGG9000.Site.Models.Contract {
    public record Contract_IndexModel(List<GuildContract> Contracts, Guild DbGuild);
}
