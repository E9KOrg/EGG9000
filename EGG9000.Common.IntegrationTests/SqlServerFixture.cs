using DotNet.Testcontainers.Builders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace EGG9000.Common.IntegrationTests;

[TestClass]
public static class SqlServerFixture {
    private static MsSqlContainer? _container;

    public static string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server container not started.");

    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext _) {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
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
