namespace SaaSHelpdesk.Tests.Fixtures;

/// <summary>
/// Test hook that counts how many times <see cref="CrestCreates.Data.EFCore.HostMigrationAndSeedRunner.RunAsync"/>
/// is invoked during application startup. Used to verify startup side effects
/// execute exactly once.
/// </summary>
public sealed class MigrationSeedCallCounter
{
    public int Count { get; private set; }

    public void Increment()
    {
        Count++;
    }
}
