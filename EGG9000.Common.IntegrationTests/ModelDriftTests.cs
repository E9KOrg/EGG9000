using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public class ModelDriftTests {
    // Compares the entity model against the latest committed migration snapshot. No DB connection
    // is made, so a placeholder connection string is sufficient. Fails when an entity was changed
    // without adding a migration.
    private const string PlaceholderConnection =
        "Server=localhost;Database=drift;User Id=sa;Password=Doesnt_Matter1;TrustServerCertificate=true";

    [TestMethod]
    public void Entities_HaveNoPendingModelChanges() {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(PlaceholderConnection, o => o.MigrationsAssembly("EGG9000.Common"))
            .Options;
        using var ctx = new ApplicationDbContext(options);
        Assert.IsFalse(ctx.Database.HasPendingModelChanges(),
            "Entity model differs from the latest migration. Add a migration for the schema change "
            + "(dotnet ef migrations add <Name> --project EGG9000.Common).");
    }
}
