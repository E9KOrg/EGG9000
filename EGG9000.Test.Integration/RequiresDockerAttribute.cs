namespace EGG9000.Test.Integration;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresDockerAttribute : ConditionBaseAttribute {
    public RequiresDockerAttribute() : base(ConditionMode.Include) {
        IgnoreMessage = "Docker is not on PATH; Testcontainers cannot start Postgres.";
    }

    public override bool IsConditionMet => DockerOnPath;

    public override string GroupName => nameof(RequiresDockerAttribute);

    public static bool DockerOnPath { get; } = Probe();

    private static bool Probe() {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => exts.Select(ext => Path.Combine(dir, "docker" + ext)))
            .Any(File.Exists);
    }
}
