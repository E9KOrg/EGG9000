using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public record Admin_NonServerUsersModel(
        List<DBUser> rows,
        bool incompleteCache
    );
}
