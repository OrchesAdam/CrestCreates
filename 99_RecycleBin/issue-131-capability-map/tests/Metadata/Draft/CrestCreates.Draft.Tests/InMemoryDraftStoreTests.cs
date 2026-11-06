using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Draft.Tests;

public class InMemoryDraftStoreTests
{
    [Fact]
    public async Task SaveAsync_Persists_Draft()
    {
        var store = new InMemoryDraftStore();
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test.type",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01",
            PayloadJson = "{\"name\":\"test\"}"
        };

        var saved = await store.SaveAsync(draft);

        saved.DraftId.Should().Be("draft_01");
        saved.UpdatedAt.Should().BeAfter(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task GetAsync_Returns_Saved_Draft()
    {
        var store = new InMemoryDraftStore();
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test.type",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };
        await store.SaveAsync(draft);

        var retrieved = await store.GetAsync("draft_01");

        retrieved.Should().NotBeNull();
        retrieved!.DraftType.Should().Be("test.type");
    }

    [Fact]
    public async Task GetAsync_Missing_Returns_Null()
    {
        var store = new InMemoryDraftStore();
        var result = await store.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_Removes_Draft()
    {
        var store = new InMemoryDraftStore();
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test.type",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };
        await store.SaveAsync(draft);
        await store.DeleteAsync("draft_01");

        var result = await store.GetAsync("draft_01");
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_Filters_By_TenantId()
    {
        var store = new InMemoryDraftStore();
        await store.SaveAsync(new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        });
        await store.SaveAsync(new DraftRecord
        {
            DraftId = "d2", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_B"
        });

        var results = await store.QueryAsync(new DraftQuery { TenantId = "tenant_A" });
        results.Should().HaveCount(1);
        results[0].DraftId.Should().Be("d1");
    }
}
