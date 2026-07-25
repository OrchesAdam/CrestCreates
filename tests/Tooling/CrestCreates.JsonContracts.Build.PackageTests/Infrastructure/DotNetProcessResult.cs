namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public sealed record DotNetProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
