using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public class InMemoryDescriptorDraftStoreTests
{
    private static Draft CreateDraft(string tenantId = "t1", string draftId = "d1",
        DescriptorKind kind = DescriptorKind.Schema, DescriptorDraftOperation op = DescriptorDraftOperation.Create)
    {
        var payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema1", Name = "Test Schema" });
        return new Draft
        {
            TenantId = tenantId,
            DraftId = draftId,
            DescriptorKind = kind,
            DescriptorId = "schema1",
            Operation = op,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = payload
        };
    }

    [Fact]
    public async Task Save_And_Get_Returns_Cloned_Draft()
    {
        var store = new InMemoryDescriptorDraftStore();
        var draft = CreateDraft();
        await store.SaveAsync(draft);

        var retrieved = await store.GetAsync("t1", "d1");
        retrieved.Should().NotBeNull();
        retrieved!.DraftId.Should().Be("d1");

        var mutated = retrieved with { Intent = "mutated" };
        var reRetrieved = await store.GetAsync("t1", "d1");
        reRetrieved!.Intent.Should().BeNull("snapshot-on-read prevents external mutation");
    }

    [Fact]
    public async Task List_Filters_By_Tenant()
    {
        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(CreateDraft("t1", "d1"));
        await store.SaveAsync(CreateDraft("t2", "d2"));
        await store.SaveAsync(CreateDraft("t1", "d3"));

        var t1Drafts = await store.ListAsync("t1");
        t1Drafts.Should().HaveCount(2);
        t1Drafts.Should().OnlyContain(d => d.TenantId == "t1");
    }

    [Fact]
    public async Task List_Filters_By_DescriptorKind()
    {
        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(CreateDraft("t1", "d1", DescriptorKind.Schema));
        await store.SaveAsync(CreateDraft("t1", "d2", DescriptorKind.Form));

        var query = new DraftQuery { DescriptorKind = DescriptorKind.Schema };
        var results = await store.ListAsync("t1", query);
        results.Should().HaveCount(1);
        results[0].DescriptorKind.Should().Be(DescriptorKind.Schema);
    }

    [Fact]
    public async Task Get_Missing_Returns_Null()
    {
        var store = new InMemoryDescriptorDraftStore();
        var result = await store.GetAsync("t1", "nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeepClone_Isolates_Nested_Collections()
    {
        // Create a descriptor with a mutable list backing Fields
        var mutableFields = new List<SchemaFieldDescriptor>
        {
            new() { Name = "field1", FieldType = "string" }
        };
        var descriptor = new SchemaDescriptor
        {
            Id = "schema1", Name = "Test", Version = 1,
            State = DescriptorState.Active, ContractHash = "abc", DefinitionHash = "def",
            Fields = mutableFields
        };
        var draft = new Draft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "schema1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(descriptor)
        };

        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(draft);

        // Mutate the original backing list after save
        mutableFields.Add(new SchemaFieldDescriptor { Name = "field2", FieldType = "int" });

        // Store's copy should still have only 1 field (deep clone snapshot)
        var retrieved = await store.GetAsync("t1", "d1");
        var retrievedSchema = (SchemaDescriptor)retrieved!.Payload.GetDescriptor();
        retrievedSchema.Fields.Should().HaveCount(1,
            "deep clone prevents post-save mutation of backing list");
    }

    [Fact]
    public async Task DeepClone_Isolates_FormField_Metadata_Dictionary()
    {
        var metadata = new Dictionary<string, string>
        {
            ["placeholder"] = "email"
        };
        var form = new FormDescriptor
        {
            Id = "form1",
            Name = "Test Form",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema1", 1),
            Fields = new[]
            {
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Email",
                    Metadata = metadata
                }
            }
        };
        var draft = new Draft
        {
            TenantId = "t1",
            DraftId = "d2",
            DescriptorKind = DescriptorKind.Form,
            DescriptorId = "form1",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new FormDescriptorDraftPayload(form)
        };

        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(draft);

        metadata["placeholder"] = "mutated";

        var retrieved = await store.GetAsync("t1", "d2");
        var retrievedForm = (FormDescriptor)retrieved!.Payload.GetDescriptor();
        retrievedForm.Fields[0].Metadata["placeholder"].Should().Be("email",
            "deep clone prevents post-save mutation of the metadata dictionary");
    }

    [Fact]
    public async Task DeepClone_Isolates_WorkflowStep_Transitions_List()
    {
        var transitions = new List<string> { "approve" };
        var workflow = new WorkflowDescriptor
        {
            Id = "wf1",
            Name = "Test Workflow",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Name = "Step 1",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap1", 1)
                    },
                    Transitions = transitions
                }
            }
        };
        var draft = new Draft
        {
            TenantId = "t1",
            DraftId = "d3",
            DescriptorKind = DescriptorKind.Workflow,
            DescriptorId = "wf1",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new WorkflowDescriptorDraftPayload(workflow)
        };

        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(draft);

        transitions.Add("reject");

        var retrieved = await store.GetAsync("t1", "d3");
        var retrievedWorkflow = (WorkflowDescriptor)retrieved!.Payload.GetDescriptor();
        retrievedWorkflow.Steps[0].Transitions.Should().BeEquivalentTo(new[] { "approve" },
            "deep clone prevents post-save mutation of the transitions list");
    }
}
