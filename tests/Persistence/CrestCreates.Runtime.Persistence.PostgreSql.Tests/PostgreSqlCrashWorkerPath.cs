namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

internal static class PostgreSqlCrashWorkerPath
{
    public static string Resolve()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
            current = current.Parent;
        if (current is null)
            throw new InvalidOperationException("Repository root not found.");

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Build configuration directory not found.");
        return Path.Combine(
            current.FullName,
            "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/bin",
            configuration,
            "net10.0",
            "CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.dll");
    }
}
