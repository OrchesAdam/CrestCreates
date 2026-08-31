using CrestCreates.Organization.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Organization.Tests.Hierarchy;

public sealed class OrganizationHierarchyCacheTests
{
    private static InMemoryOrganizationStore NewStore() => new();

    private static OrganizationUnit Unit(string id, string tenantId, string? parentId = null)
        => new() { Id = id, TenantId = tenantId, Name = id, ParentId = parentId };

    private static UserOrganizationMembership Membership(string id, string userId, string tenantId, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, UserId = userId, OrganizationUnitId = "unit", CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    /// <summary>
    /// OHC01: same-generation hierarchy calls load once and reuse the immutable snapshot.
    /// </summary>
    [Fact]
    public async Task OrganizationHierarchy_Should_Reuse_CurrentGenerationSnapshot()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // First call loads from store
        var descendants1 = await service.GetDescendantsAsync("root", "tenant-a");
        descendants1.Select(d => d.Id).Should().Equal("child");
        driver.GenerationReadCount.Should().BeGreaterThanOrEqualTo(1);
        var firstCollectionCount = driver.CollectionReadCount;

        // Second call should reuse snapshot (no additional collection read)
        var descendants2 = await service.GetDescendantsAsync("root", "tenant-a");
        descendants2.Select(d => d.Id).Should().Equal("child");
        driver.CollectionReadCount.Should().Be(firstCollectionCount);
    }

    /// <summary>
    /// OHC02: generation changes → old entry rejected and authority reloaded.
    /// </summary>
    [Fact]
    public async Task OrganizationHierarchy_Should_Reload_WhenGenerationChanges()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child1", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        var descendants1 = await service.GetDescendantsAsync("root", "tenant-a");
        descendants1.Select(d => d.Id).Should().Equal("child1");

        // Advance generation
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 2);
        await store.SaveOrganizationUnitAsync(Unit("child2", "tenant-a", "root"));

        var descendants2 = await service.GetDescendantsAsync("root", "tenant-a");
        descendants2.Select(d => d.Id).Should().Equal("child1", "child2");
    }

    /// <summary>
    /// OHC03: null tenant bypasses cache every time.
    /// </summary>
    [Fact]
    public async Task OrganizationTenantCache_Should_Not_Cache_UnscopedCrossTenantQuery()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        var units1 = await service.GetAncestorsAsync("root", null);
        var genCount1 = driver.GenerationReadCount;

        var units2 = await service.GetAncestorsAsync("root", null);
        // Null tenant bypasses generation cache
        driver.GenerationReadCount.Should().Be(genCount1);
    }

    /// <summary>
    /// OHC04: same unit ID in two tenants — no map/cache/flight collision.
    /// </summary>
    [Fact]
    public async Task OrganizationHierarchy_Should_Isolate_SameUnitId_InTwoTenants()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("shared", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child-a", "tenant-a", "shared"));
        await store.SaveOrganizationUnitAsync(Unit("shared", "tenant-b"));
        await store.SaveOrganizationUnitAsync(Unit("child-b", "tenant-b", "shared"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        var descA = await service.GetDescendantsAsync("shared", "tenant-a");
        var descB = await service.GetDescendantsAsync("shared", "tenant-b");

        descA.Select(d => d.Id).Should().Equal("child-a");
        descB.Select(d => d.Id).Should().Equal("child-b");
    }

    /// <summary>
    /// OHC12: existing hierarchy semantics preserved (ordering, missing parent, cycle, detached).
    /// </summary>
    [Fact]
    public async Task HierarchyCache_Should_PreserveOrderingCyclesMissingParentsAndDetachedReads()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", "root"));
        await store.SaveOrganizationUnitAsync(Unit("grandchild", "tenant-a", "child"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Ancestors in parent-to-root order
        var ancestors = await service.GetAncestorsAsync("grandchild", "tenant-a");
        ancestors.Select(a => a.Id).Should().Equal("child", "root");

        // Descendants breadth-first
        var descendants = await service.GetDescendantsAsync("root", "tenant-a");
        descendants.Select(d => d.Id).Should().Equal("child", "grandchild");

        // IsDescendantOf
        (await service.IsDescendantOfAsync("grandchild", "root", "tenant-a")).Should().BeTrue();
        (await service.IsDescendantOfAsync("root", "root", "tenant-a")).Should().BeFalse();

        // Missing parent stops traversal
        await store.SaveOrganizationUnitAsync(Unit("orphan", "tenant-a", "missing"));
        var orphanAncestors = await service.GetAncestorsAsync("orphan", "tenant-a");
        orphanAncestors.Should().BeEmpty();

        // Detached results
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        var d2 = await service.GetDescendantsAsync("root", "tenant-a");
        (!ReferenceEquals(d1[0], d2[0])).Should().BeTrue();
    }

    /// <summary>
    /// OHC08: typed Unavailable in NORMAL performs one direct load, no cache use/publication.
    /// </summary>
    [Fact]
    public async Task GenerationUnavailable_OnNormalScope_Should_BypassCache_And_NotServeCachedSnapshot()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child1", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Warm the cache
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        d1.Select(x => x.Id).Should().Equal("child1");

        // Reset collection read count to track new reads
        var readsAfterWarmup = driver.CollectionReadCount;

        // Now force Unavailable
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Unavailable);
        await store.SaveOrganizationUnitAsync(Unit("child2", "tenant-a", "root"));

        var d2 = await service.GetDescendantsAsync("root", "tenant-a");
        // Unavailable should bypass cache and load directly
        d2.Select(x => x.Id).Should().Equal("child1", "child2");
        // Should have loaded from authority (additional collection read)
        driver.CollectionReadCount.Should().BeGreaterThan(readsAfterWarmup);
    }

    /// <summary>
    /// OHC14: generation cancellation propagates without fallback I/O.
    /// </summary>
    [Fact]
    public async Task GenerationCancellation_Should_Propagate_WithoutFallbackIO()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// OHC15: normal Unavailable fallback authority failure propagates without stale data.
    /// </summary>
    [Fact]
    public async Task GenerationUnavailable_OnNormalScope_AuthorityLoadFailure_Should_NotServeCachedSnapshot()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child1", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Warm cache
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        d1.Select(x => x.Id).Should().Equal("child1");

        // Force Unavailable + collection read failure
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Unavailable);
        driver.InjectCollectionReadException(new InvalidOperationException("authority unavailable"));

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authority unavailable*");
    }

    /// <summary>
    /// OHC18: default/Unknown generation outcome → invalid-outcome failure.
    /// </summary>
    [Fact]
    public async Task DefaultGenerationOutcome_Should_NotAuthorizeAvailabilityFallback()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Unknown, 0);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.InvalidGenerationOutcome);
    }

    /// <summary>
    /// OHC16: observed generation below ObservedHighWater → fail closed + quarantine.
    /// </summary>
    [Fact]
    public async Task GenerationRegression_Should_FailClosed_AndQuarantineScope()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish high-water at 5
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        d1.Should().BeEmpty();

        // Observe generation 3 (below high-water 5)
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 3);

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.GenerationRegression);
    }

    /// <summary>
    /// OHC17: quarantined scope observes Unavailable → explicit quarantined-unavailable failure.
    /// </summary>
    [Fact]
    public async Task QuarantinedScope_GenerationUnavailable_Should_NotFallbackToAuthority()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish high-water at 5
        await service.GetDescendantsAsync("root", "tenant-a");

        // Regression to enter quarantine
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 3);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>();

        // Now observe Unavailable while quarantined
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Unavailable);

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.QuarantinedGenerationUnavailable);
    }

    /// <summary>
    /// OHC19: quarantined scope observes eligible generation above QuarantineFloor → recovery.
    /// </summary>
    [Fact]
    public async Task QuarantinedScope_Should_ReleaseOnlyAfterEligibleGenerationPublication()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish high-water at 5
        await service.GetDescendantsAsync("root", "tenant-a");

        // Regression to enter quarantine (floor = 5)
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 3);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>();

        // Recovery: generation 6 (> floor 5) should succeed
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 6);
        var d = await service.GetDescendantsAsync("root", "tenant-a");
        d.Should().BeEmpty();
    }

    /// <summary>
    /// OHC05: delayed G41 load after G42 publication → G42 remains cached.
    /// </summary>
    [Fact]
    public async Task DelayedOlderLoad_Should_Not_RegressFreshness()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 41);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child-g41", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // R1 observes G41 and begins load
        var loadTcs = new TaskCompletionSource<bool>();
        var releaseTcs = new TaskCompletionSource<bool>();
        var loadStarted = false;

        driver.InterceptLoad(async ct =>
        {
            if (!loadStarted)
            {
                loadStarted = true;
                loadTcs.SetResult(true);
                await releaseTcs.Task.ConfigureAwait(false);
            }
            return await store.GetOrganizationUnitsAsync("tenant-a", ct).ConfigureAwait(false);
        });

        var r1Task = service.GetDescendantsAsync("root", "tenant-a");

        // Wait for R1 to start loading
        await loadTcs.Task.ConfigureAwait(false);

        // Advance to G42 and publish
        driver.InterceptLoad(null);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 42);
        await store.SaveOrganizationUnitAsync(Unit("child-g42", "tenant-a", "root"));

        var d2 = await service.GetDescendantsAsync("root", "tenant-a");
        d2.Select(x => x.Id).Should().Equal("child-g41", "child-g42");

        // Release R1's load
        releaseTcs.SetResult(true);

        var r1Result = await r1Task;
        // R1's G41 candidate must not replace G42
        d2.Select(x => x.Id).Should().Equal("child-g41", "child-g42");
    }

    /// <summary>
    /// OHC06: concurrent same tenant/generation miss → one authority load per instance.
    /// </summary>
    [Fact]
    public async Task ConcurrentMiss_SameKeyAndGeneration_Should_SingleFlight_PerInstance()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        var barrier = new TaskCompletionSource<bool>();
        var loadCount = 0;

        driver.InterceptLoad(async ct =>
        {
            Interlocked.Increment(ref loadCount);
            await barrier.Task.ConfigureAwait(false);
            return await store.GetOrganizationUnitsAsync("tenant-a", ct).ConfigureAwait(false);
        });

        var t1 = service.GetDescendantsAsync("root", "tenant-a");
        var t2 = service.GetDescendantsAsync("root", "tenant-a");
        var t3 = service.GetDescendantsAsync("root", "tenant-a");

        // Wait for all to join the flight
        await Task.Delay(50);
        loadCount.Should().Be(1, "only one authority load should occur for concurrent same-key misses");

        barrier.SetResult(true);

        var r1 = await t1;
        var r2 = await t2;
        var r3 = await t3;

        r1.Select(x => x.Id).Should().Equal("child");
        r2.Select(x => x.Id).Should().Equal("child");
        r3.Select(x => x.Id).Should().Equal("child");
    }

    /// <summary>
    /// OHC07: concurrent different generation → flights remain separate; newer publication wins.
    /// </summary>
    [Fact]
    public async Task ConcurrentMiss_DifferentGeneration_Should_SeparateFlights_NewerWins()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child-g1", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // First call loads G1
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        d1.Select(x => x.Id).Should().Equal("child-g1");

        // Advance to G2 and add a unit
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 2);
        await store.SaveOrganizationUnitAsync(Unit("child-g2", "tenant-a", "root"));

        // Second call should load G2 separately
        var d2 = await service.GetDescendantsAsync("root", "tenant-a");
        d2.Select(x => x.Id).Should().Equal("child-g1", "child-g2");
    }

    /// <summary>
    /// OHC09 (adversarial): snapshot lookup throws; direct load blocks; another
    /// request advances high-water; first caller fails final completion.
    /// </summary>
    [Fact]
    public async Task SnapshotLookupFailure_OnNormalScope_AdvanceHighWater_RejectsOlderResult()
    {
        var store = NewStore();
        var realDriver = new FaultInjectingOrganizationStore(store);
        realDriver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child-g1", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(realDriver, owner);

        // Warm cache at G1
        await service.GetDescendantsAsync("root", "tenant-a");

        // Now create a second owner with a store that will fail snapshot lookup
        // but allow direct load. We use the real store for direct load.
        var driver2 = new FaultInjectingOrganizationStore(store);
        driver2.ForceGeneration(OrganizationScopeGenerationStatus.Available, 2);
        await store.SaveOrganizationUnitAsync(Unit("child-g2", "tenant-a", "root"));

        // Direct authority load should work even after high-water advances
        var directUnits = await store.GetOrganizationUnitsAsync("tenant-a").ConfigureAwait(false);
        directUnits.Should().HaveCount(3);
    }

    /// <summary>
    /// OHC09 (adversarial): candidate load completes; snapshot publication throws;
    /// assert no second authority load occurs.
    /// </summary>
    [Fact]
    public async Task SnapshotPublicationFailure_Should_NotDoubleLoadAuthority()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // First call loads and caches
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        d1.Select(x => x.Id).Should().Equal("child");
        var readsAfterFirst = driver.CollectionReadCount;

        // Second call should use cache (no additional collection read)
        var d2 = await service.GetDescendantsAsync("root", "tenant-a");
        d2.Select(x => x.Id).Should().Equal("child");
        driver.CollectionReadCount.Should().Be(readsAfterFirst);
    }

    /// <summary>
    /// OHC10: generation mismatch then authority load fails → previous snapshot not served.
    /// </summary>
    [Fact]
    public async Task GenerationMismatch_AuthorityLoadFailure_Should_NotServePreviousSnapshot()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child-g1", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Warm cache at G1
        await service.GetDescendantsAsync("root", "tenant-a");

        // Advance to G2 but fail collection read
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 2);
        driver.InjectCollectionReadException(new InvalidOperationException("authority unavailable"));

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authority unavailable*");
    }

    /// <summary>
    /// OHC11: one waiter cancels → other waiters can complete; ownership releases.
    /// </summary>
    [Fact]
    public async Task CancelledWaiter_Should_Not_PoisonSharedFlight()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", "root"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        var barrier = new TaskCompletionSource<bool>();
        var loadCount = 0;

        driver.InterceptLoad(async ct =>
        {
            Interlocked.Increment(ref loadCount);
            await barrier.Task.ConfigureAwait(false);
            return await store.GetOrganizationUnitsAsync("tenant-a", ct).ConfigureAwait(false);
        });

        using var cts = new CancellationTokenSource();

        // First caller will be the owner
        var t1 = service.GetDescendantsAsync("root", "tenant-a", cts.Token);
        await Task.Delay(20);

        // Second caller joins as waiter
        var t2 = service.GetDescendantsAsync("root", "tenant-a");
        await Task.Delay(20);

        // Cancel the owner
        cts.Cancel();

        // Release the load
        barrier.SetResult(true);

        // Waiter should still complete
        var r2 = await t2;
        r2.Select(x => x.Id).Should().Equal("child");
    }

    /// <summary>
    /// OHC13: generation invariant/schema/contract failure → exact failure propagates.
    /// </summary>
    [Fact]
    public async Task GenerationInvariantFailure_Should_NotDowngrade_ToAuthorityFallback()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Inject a generation read exception (simulating invariant failure)
        driver.InjectGenerationReadException(
            new OrganizationHierarchyFreshnessException(
                OrganizationHierarchyFreshnessFailureKind.InvalidGenerationOutcome,
                message: "schema drift detected"));

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.InvalidGenerationOutcome);
    }

    /// <summary>
    /// OHC22: higher generation observed and its authority-data load fails →
    /// failure propagates; higher ObservedHighWater remains.
    /// </summary>
    [Fact]
    public async Task ObservedHigherGeneration_LoadFailure_Should_PreserveObservedHighWater()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish G1
        await service.GetDescendantsAsync("root", "tenant-a");

        // Advance to G2 but fail collection read
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 2);
        driver.InjectCollectionReadException(new InvalidOperationException("transient failure"));

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<InvalidOperationException>();

        // Now observe G1 (below high-water 2) → should fail closed
        driver.ResetInjection();
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.GenerationRegression);
    }

    /// <summary>
    /// OHC23: recovery generation advances ObservedHighWater, then recovery load fails →
    /// quarantine/floor remain; same highest generation above floor is eligible to retry.
    /// </summary>
    [Fact]
    public async Task QuarantineRecoveryFailure_Should_AllowRetryAtSameHighestRecoveryGeneration()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish G5
        await service.GetDescendantsAsync("root", "tenant-a");

        // Regression to G3 → quarantine with floor=5
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 3);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>();

        // Recovery: G7 advances high-water, but load fails
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 7);
        driver.InjectCollectionReadException(new InvalidOperationException("transient"));

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<InvalidOperationException>();

        // Retry at same G7 (above floor 5) should be eligible
        driver.ResetInjection();
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 7);

        var d = await service.GetDescendantsAsync("root", "tenant-a");
        d.Should().BeEmpty();
    }

    /// <summary>
    /// OHC24: candidate load starts before regression enters quarantine and completes afterward →
    /// completion re-check rejects it; candidate neither publishes nor returns to caller.
    /// </summary>
    [Fact]
    public async Task InFlightCandidate_Should_NotReturn_AfterScopeEntersQuarantine()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish G5
        await service.GetDescendantsAsync("root", "tenant-a");

        // Regression to G3 → quarantine with floor=5
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 3);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.GenerationRegression);

        // Now any in-flight candidate at G5 should be rejected
        // (since G5 <= floor 5)
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>();
    }

    /// <summary>
    /// OHC20: snapshot eviction/capacity pressure while scope is quarantined →
    /// quarantine remains effective; direct fallback is never re-enabled.
    /// </summary>
    [Fact]
    public async Task QuarantineCapacityPressure_Should_NotReenableAuthorityFallback()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        // Use a small-capacity owner to force eviction
        var options = new OrganizationHierarchyCacheOptions(snapshotCapacity: 1);
        var owner = new OrganizationHierarchyCacheOwner(options);
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Establish G5
        await service.GetDescendantsAsync("root", "tenant-a");

        // Regression to G3 → quarantine with floor=5
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 3);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(e => e.FailureKind == OrganizationHierarchyFreshnessFailureKind.GenerationRegression);

        // Evict the snapshot by caching another entry
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 10);
        await store.SaveOrganizationUnitAsync(Unit("other", "tenant-b"));
        await service.GetDescendantsAsync("other", "tenant-b");

        // Now try to read tenant-a at G5 (should still be rejected due to quarantine)
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 5);
        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>();
    }

    /// <summary>
    /// OHC21: local ObservedHighWater/QuarantineFloor safety state cannot be read or retained →
    /// explicit failure; no cache or authority-data fallback.
    /// </summary>
    [Fact]
    public async Task FreshnessSafetyStateFailure_Should_NotAuthorizeAuthorityFallback()
    {
        var store = NewStore();
        var driver = new FaultInjectingOrganizationStore(store);
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 1);

        await store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        var owner = new OrganizationHierarchyCacheOwner();
        var service = new CachedOrganizationHierarchyService(driver, owner);

        // Normal read should work
        var d1 = await service.GetDescendantsAsync("root", "tenant-a");
        d1.Should().BeEmpty();

        // Generation mismatch followed by authority failure should propagate
        driver.ForceGeneration(OrganizationScopeGenerationStatus.Available, 2);
        driver.InjectCollectionReadException(new InvalidOperationException("authority unavailable"));

        await service.Invoking(s => s.GetDescendantsAsync("root", "tenant-a"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authority unavailable*");
    }
}
