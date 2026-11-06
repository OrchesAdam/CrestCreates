using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

// semantic-string-guard: allow

namespace CrestCreates.Draft.Tests;

public class DraftRecordTests
{
    [Fact]
    public void DraftRecord_Defaults_Status_To_Active()
    {
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "employee.create",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01",
            PayloadJson = "{\"name\":\"Tom\"}"
        };

        draft.Status.Should().Be(DraftStatus.Active);
    }

    [Fact]
    public void DraftRecord_DraftType_Is_Not_A_DescriptorKind()
    {
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "agent.plan",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };

        draft.DraftType.Should().Be("agent.plan");
    }

    [Fact]
    public void DraftRecord_References_Schema_Not_Capability()
    {
        var schemaRef = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "employee.create",
            Schema = schemaRef,
            TenantId = "tenant_01"
        };

        draft.Schema.Id.Should().Be("schema_01");
        draft.Schema.Version.Should().Be(3);
    }

    [Fact]
    public void DraftRecord_PayloadJson_Defaults_To_EmptyJson()
    {
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };

        draft.PayloadJson.Should().Be("{}");
    }
}