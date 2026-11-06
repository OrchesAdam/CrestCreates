using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Draft.Tests;

public class TenantIsolatedDraftStoreTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
    }

    [Fact]
    public async Task SaveAsync_OverridesTenantId()
    {
        var inner = new InMemoryDraftStore();
        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_A" };
        var store = new TenantIsolatedDraftStore(inner, tenantCtx);

        var draft = new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_B"
        };

        var saved = await store.SaveAsync(draft);
        saved.TenantId.Should().Be("tenant_A");
    }

    [Fact]
    public async Task GetAsync_FiltersByTenant()
    {
        var inner = new InMemoryDraftStore();
        await inner.SaveAsync(new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        });

        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_B" };
        var store = new TenantIsolatedDraftStore(inner, tenantCtx);

        var result = await store.GetAsync("d1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_AddsTenantFilter()
    {
        var inner = new InMemoryDraftStore();
        await inner.SaveAsync(new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        });
        await inner.SaveAsync(new DraftRecord
        {
            DraftId = "d2", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_B"
        });

        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_A" };
        var store = new TenantIsolatedDraftStore(inner, tenantCtx);

        var results = await store.QueryAsync(new DraftQuery());
        results.Should().HaveCount(1);
        results[0].DraftId.Should().Be("d1");
    }

    [Fact]
    public async Task Passthrough_WhenNoTenantContext()
    {
        var inner = new InMemoryDraftStore();
        var store = new TenantIsolatedDraftStore(inner, null);

        var draft = new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        };

        var saved = await store.SaveAsync(draft);
        saved.TenantId.Should().Be("tenant_A");
    }
}