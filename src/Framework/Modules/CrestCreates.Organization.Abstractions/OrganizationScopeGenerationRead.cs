namespace CrestCreates.Organization.Abstractions;

public readonly record struct OrganizationScopeGenerationRead
{
    public OrganizationScopeGenerationStatus Status { get; }

    public long Generation { get; }

    private OrganizationScopeGenerationRead(OrganizationScopeGenerationStatus status, long generation)
    {
        Status = status;
        Generation = generation;
    }

    public static OrganizationScopeGenerationRead Available(long generation)
    {
        if (generation < 0)
            throw new ArgumentException("Generation must be non-negative.", nameof(generation));
        return new OrganizationScopeGenerationRead(OrganizationScopeGenerationStatus.Available, generation);
    }

    public static OrganizationScopeGenerationRead Unavailable { get; } = new(OrganizationScopeGenerationStatus.Unavailable, 0);
}
