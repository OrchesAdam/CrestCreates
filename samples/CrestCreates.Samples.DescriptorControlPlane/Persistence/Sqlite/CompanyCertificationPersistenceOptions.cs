namespace CrestCreates.Samples.DescriptorControlPlane;

public enum CompanyCertificationPersistenceMode
{
    InMemory,
    Sqlite
}

public sealed class CompanyCertificationPersistenceOptions
{
    public CompanyCertificationPersistenceMode Mode { get; init; } = CompanyCertificationPersistenceMode.Sqlite;
    public string? DatabasePath { get; init; }
}
