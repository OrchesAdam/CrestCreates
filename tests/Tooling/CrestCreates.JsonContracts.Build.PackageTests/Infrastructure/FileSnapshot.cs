namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public sealed record FileSnapshot(
    string Path,
    DateTime LastWriteTimeUtc,
    byte[] Content);
