using Testcontainers.PostgreSql;

namespace EGG9000.Test.Integration;

[TestClass]
public static class PostgresFixture {
    private static PostgreSqlContainer? _container;

    public static string ConnectionString {
        get {
            return _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Postgres container not started.");
        }
    }

    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext _) {
        if(!RequiresDockerAttribute.DockerOnPath)
            return;
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .Build();
        await _container.StartAsync(_.CancellationToken);
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup() {
        if(_container is not null) {
            await _container.DisposeAsync();
        }
    }
}
