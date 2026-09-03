using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Domain.Entities;
using CrestCreates.Sample.AssetManagement.Persistence;

namespace CrestCreates.Sample.AssetManagement.Tests;

public sealed class AssetAcceptanceTests
{
    [Fact]
    public async Task Asset_Should_RoundTrip_Through_Production_Persistence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"asset-test-{Guid.NewGuid():N}.db");
        var asset = CreateAsset("tenant-a", "LAPTOP-001");
        try
        {
            await using (var store = new TestStore(new SqliteAssetStore($"Data Source={path}")))
            {
                await store.Value.InitializeAsync();
                await store.Value.AddAsync(asset);
            }
            await using var reopened = new TestStore(new SqliteAssetStore($"Data Source={path}"));
            var loaded = await reopened.Value.GetAsync("tenant-a", asset.Id);
            loaded.Should().NotBeNull();
            loaded!.AssetTag.Should().Be(asset.AssetTag);
            loaded.Name.Should().Be(asset.Name);
            loaded.TenantId.Should().Be("tenant-a");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Asset_Query_Should_Be_Deterministic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"asset-test-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new TestStore(new SqliteAssetStore($"Data Source={path}"));
            await store.Value.InitializeAsync();
            await store.Value.AddAsync(CreateAsset("tenant-a", "LAPTOP-002"));
            await store.Value.AddAsync(CreateAsset("tenant-a", "LAPTOP-001"));
            var first = await store.Value.ListAsync("tenant-a");
            var second = await store.Value.ListAsync("tenant-a");
            first.Select(asset => asset.AssetTag).Should().Equal("LAPTOP-001", "LAPTOP-002");
            second.Select(asset => asset.Id).Should().Equal(first.Select(asset => asset.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Tenant_Should_Isolate_SameLogicalAssetId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"asset-test-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new TestStore(new SqliteAssetStore($"Data Source={path}"));
            await store.Value.InitializeAsync();
            var id = Guid.NewGuid();
            await store.Value.AddAsync(CreateAsset("tenant-a", "SHARED", id));
            await store.Value.AddAsync(CreateAsset("tenant-b", "SHARED", id));
            (await store.Value.GetAsync("tenant-a", id))!.TenantId.Should().Be("tenant-a");
            (await store.Value.GetAsync("tenant-b", id))!.TenantId.Should().Be("tenant-b");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AssetAssignment_Should_Respect_LifecycleRules()
    {
        var asset = CreateAsset("tenant-a", "LAPTOP-001");
        var organizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        asset.Assign("user-1", organizationId, "manager-1");
        asset.Status.Should().Be(AssetStatus.Assigned);
        asset.AssignedUserId.Should().Be("user-1");
        asset.Return("manager-1");
        asset.Status.Should().Be(AssetStatus.Available);
        asset.ActiveAssignmentId.Should().BeNull();
        asset.Assign("user-2", organizationId, "manager-1");
        asset.ActiveAssignmentId.Should().NotBeNull();
    }

    private static Asset CreateAsset(string tenantId, string tag, Guid? id = null)
        => new(id ?? Guid.NewGuid(), tenantId, tag, "Engineering laptop", "Golden test asset", "Equipment", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Shanghai", "manager-1");

    private sealed class TestStore(SqliteAssetStore value) : IAsyncDisposable
    {
        public SqliteAssetStore Value { get; } = value;
        public ValueTask DisposeAsync()
        {
            Value.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
