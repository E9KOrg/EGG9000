using DotNet.Testcontainers.Builders;
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
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
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
