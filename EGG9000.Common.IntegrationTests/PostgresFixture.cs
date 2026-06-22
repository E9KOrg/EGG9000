using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace EGG9000.Common.IntegrationTests;

[TestClass]
public static class PostgresFixture {
    private static PostgreSqlContainer? _container;

    public static string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Postgres container not started.");

    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext _) {
        // The PostgreSql module applies its own pg_isready-based readiness wait, so no explicit
        // WithWaitStrategy is needed. Testcontainers 4.12 takes the image in the constructor and
        // removed both the parameterless builder and UntilPortIsAvailable.
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .Build();
        await _container.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup() {
        if (_container is not null) {
            await _container.DisposeAsync();
        }
    }
}
