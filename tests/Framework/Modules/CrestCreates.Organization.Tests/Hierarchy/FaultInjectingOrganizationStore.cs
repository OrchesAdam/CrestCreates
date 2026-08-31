using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests.Hierarchy;

/// <summary>
/// Fault-injecting IOrganizationStore wrapper for deterministic hierarchy
/// cache/safety-state testing. Controls generation outcomes, data loads,
/// and injection of failures independently.
/// </summary>
internal sealed class FaultInjectingOrganizationStore : IOrganizationStore
{
    private readonly IOrganizationStore _inner;
    private int _generationReadCount;
    private int _collectionReadCount;

    private OrganizationScopeGenerationStatus _forcedStatus = OrganizationScopeGenerationStatus.Available;
    private long _forcedGeneration;
    private Exception? _generationReadException;
    private Exception? _collectionReadException;
    private Func<CancellationToken, ValueTask<IReadOnlyList<OrganizationUnit>>>? _interceptLoad;

    public FaultInjectingOrganizationStore(IOrganizationStore inner)
    {
        _inner = inner;
    }

    public int GenerationReadCount => _generationReadCount;
    public int CollectionReadCount => _collectionReadCount;

    public void ForceGeneration(OrganizationScopeGenerationStatus status, long generation = 0)
    {
        _forcedStatus = status;
        _forcedGeneration = generation;
    }

    public void InjectGenerationReadException(Exception exception)
    {
        _generationReadException = exception;
    }

    public void InjectCollectionReadException(Exception exception)
    {
        _collectionReadException = exception;
    }

    public void InterceptLoad(Func<CancellationToken, ValueTask<IReadOnlyList<OrganizationUnit>>>? intercept)
    {
        _interceptLoad = intercept;
    }

    public void ResetInjection()
    {
        _generationReadException = null;
        _collectionReadException = null;
        _forcedStatus = OrganizationScopeGenerationStatus.Available;
        _forcedGeneration = 0;
        _interceptLoad = null;
    }

    public async Task<OrganizationScopeGenerationRead> ReadScopeGenerationAsync(
        OrganizationScopeIdentity scope,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _generationReadCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (_generationReadException is not null)
            throw _generationReadException;

        if (_forcedStatus == OrganizationScopeGenerationStatus.Available)
            return OrganizationScopeGenerationRead.Available(_forcedGeneration);

        if (_forcedStatus == OrganizationScopeGenerationStatus.Unavailable)
            return OrganizationScopeGenerationRead.Unavailable;

        // Unknown/default
        return default;
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _collectionReadCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (_collectionReadException is not null)
            throw _collectionReadException;

        if (_interceptLoad is not null)
            return await _interceptLoad(cancellationToken).ConfigureAwait(false);

        return await _inner.GetOrganizationUnitsAsync(tenantId, cancellationToken).ConfigureAwait(false);
    }

    // Delegates for Save methods
    public Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default)
        => _inner.SaveOrganizationUnitAsync(organizationUnit, cancellationToken);
    public Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
        => _inner.GetOrganizationUnitByIdAsync(organizationUnitId, tenantId, cancellationToken);
    public Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
        => _inner.SavePositionAsync(position, cancellationToken);
    public Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
        => _inner.GetPositionByIdAsync(positionId, tenantId, cancellationToken);
    public Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
        => _inner.GetPositionsAsync(tenantId, cancellationToken);
    public Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
        => _inner.SaveMembershipAsync(membership, cancellationToken);
    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
        => _inner.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
        => _inner.GetMembershipsByOrganizationUnitAsync(organizationUnitId, tenantId, cancellationToken);
    public Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
        => _inner.SaveRoleAssignmentAsync(assignment, cancellationToken);
    public Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
        => _inner.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
}
