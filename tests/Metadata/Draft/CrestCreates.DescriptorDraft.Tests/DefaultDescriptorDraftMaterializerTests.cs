using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft.Tests;

public class DefaultDescriptorDraftMaterializerTests
{
    private static IReadOnlyList<IDescriptor> Empty => Array.Empty<IDescriptor>();

    private static IReadOnlyList<IDescriptor> With(SchemaDescriptor desc) => new List<IDescriptor> { desc };

    private static Draft CreateCreateDraft(string id = "schema1", int version = 1)
    {
        var desc = new SchemaDescriptor { Id = id, Name = "Test", Version = version, State = DescriptorState.Active };
        return new Draft { TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema, DescriptorId = id, Operation = DescriptorDraftOperation.Create, AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1", CreatedAt = DateTimeOffset.UtcNow, Payload = new SchemaDescriptorDraftPayload(desc), ProposedVersion = version.ToString() };
    }

    private static Draft CreateUpdateDraft(string id = "schema1", int baseVer = 1, int proposedVer = 2)
    {
        var desc = new SchemaDescriptor { Id = id, Name = "Updated", Version = proposedVer, State = DescriptorState.Active };
        return new Draft { TenantId = "t1", DraftId = "d2", DescriptorKind = DescriptorKind.Schema, DescriptorId = id, Operation = DescriptorDraftOperation.Update, AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1", CreatedAt = DateTimeOffset.UtcNow, Payload = new SchemaDescriptorDraftPayload(desc), BaseVersion = baseVer.ToString(), ProposedVersion = proposedVer.ToString() };
    }

    [Fact] public void Create_Adds_Descriptor()
    {
        var m = new DefaultDescriptorDraftMaterializer();
        var r = m.Materialize(CreateCreateDraft(), Empty);
        r.IsMaterialized.Should().BeTrue();
        r.ProposedInventory.Should().HaveCount(1);
        r.ProposedInventory[0].Id.Should().Be("schema1");
    }

    [Fact] public void Create_Fails_On_Existing()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "X", Version = 1, State = DescriptorState.Active };
        var r = new DefaultDescriptorDraftMaterializer().Materialize(CreateCreateDraft(), With(existing));
        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "CREATE_DESCRIPTOR_EXISTS");
    }

    [Fact] public void Update_Replaces_Descriptor()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var r = new DefaultDescriptorDraftMaterializer().Materialize(CreateUpdateDraft(), With(existing));
        r.IsMaterialized.Should().BeTrue();
        r.ProposedInventory.Should().HaveCount(1);
        (r.ProposedInventory[0] as SchemaDescriptor)!.Name.Should().Be("Updated");
    }

    [Fact] public void Update_Fails_On_Missing()
    {
        var r = new DefaultDescriptorDraftMaterializer().Materialize(CreateUpdateDraft("nonexistent"), Empty);
        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "UPDATE_BASE_NOT_FOUND");
    }

    [Fact] public void Does_Not_Mutate_Source()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var original = With(existing);
        new DefaultDescriptorDraftMaterializer().Materialize(CreateUpdateDraft(), original);
        original.Should().HaveCount(1);
        original[0].Should().Be(existing, "source inventory must not be mutated");
    }

    [Fact] public void Create_DifferentVersion_NotDuplicate()
    {
        // Inventory has schema1 v1; creating schema1 v2 should succeed (different version)
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var draft = CreateCreateDraft("schema1", version: 2);
        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));
        r.IsMaterialized.Should().BeTrue();
        r.ProposedInventory.Should().HaveCount(2, "v1 and v2 should both exist");
    }

    [Fact] public void Update_WrongBaseVersion_Fails()
    {
        // Inventory has schema1 v1; updating with baseVersion=2 should fail
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var draft = CreateUpdateDraft("schema1", baseVer: 2, proposedVer: 3);
        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));
        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "UPDATE_BASE_NOT_FOUND");
    }

    [Fact] public void Update_OnlyReplaces_MatchedVersion()
    {
        // Inventory has schema1 v1 and v2; updating v1 → v3 should only replace v1
        var v1 = new SchemaDescriptor { Id = "schema1", Name = "V1", Version = 1, State = DescriptorState.Active };
        var v2 = new SchemaDescriptor { Id = "schema1", Name = "V2", Version = 2, State = DescriptorState.Active };
        var inventory = new List<IDescriptor> { v1, v2 };
        var draft = CreateUpdateDraft("schema1", baseVer: 1, proposedVer: 3);
        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, inventory);
        r.IsMaterialized.Should().BeTrue();
        r.ProposedInventory.Should().HaveCount(2);
        (r.ProposedInventory[0] as SchemaDescriptor)!.Name.Should().Be("Updated");
        (r.ProposedInventory[0] as SchemaDescriptor)!.Version.Should().Be(3);
        (r.ProposedInventory[1] as SchemaDescriptor)!.Version.Should().Be(2, "v2 should be untouched");
    }

    [Fact]
    public void Update_ToExistingProposedVersion_Fails()
    {
        var baseItem = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var conflictingItem = new SchemaDescriptor { Id = "schema1", Name = "Existing V2", Version = 2, State = DescriptorState.Active };
        var inventory = new List<IDescriptor> { baseItem, conflictingItem };
        var draft = CreateUpdateDraft("schema1", baseVer: 1, proposedVer: 2);

        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, inventory);

        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "UPDATE_DESCRIPTOR_EXISTS");
    }

    // --- Descriptor Reference Isolation Tests ---

    [Fact]
    public void Materialize_Does_Not_Share_Descriptor_References_With_CurrentInventory()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var draft = CreateUpdateDraft();

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));

        result.IsMaterialized.Should().BeTrue();
        result.ProposedInventory[0].Should().NotBeSameAs(existing);
    }

    [Fact]
    public void Create_Does_Not_Share_Existing_Descriptor_Reference_With_CurrentInventory()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Existing", Version = 1, State = DescriptorState.Active };
        var draft = CreateCreateDraft(id: "schema2", version: 1);

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));

        result.IsMaterialized.Should().BeTrue();
        var proposedExisting = result.ProposedInventory
            .OfType<SchemaDescriptor>()
            .Single(x => x.Id == "schema1");
        proposedExisting.Should().NotBeSameAs(existing);
    }

    [Fact]
    public void Update_Does_Not_Share_NonReplaced_Descriptor_Reference_With_CurrentInventory()
    {
        var v1 = new SchemaDescriptor { Id = "schema1", Name = "V1", Version = 1, State = DescriptorState.Active };
        var v2 = new SchemaDescriptor { Id = "schema2", Name = "V2", Version = 1, State = DescriptorState.Active };
        var inventory = new List<IDescriptor> { v1, v2 };
        var draft = CreateUpdateDraft();

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, inventory);

        result.IsMaterialized.Should().BeTrue();
        var proposedV2 = result.ProposedInventory
            .OfType<SchemaDescriptor>()
            .Single(x => x.Id == "schema2");
        proposedV2.Should().NotBeSameAs(v2);
    }

    [Fact]
    public void Create_Does_Not_Insert_Original_Payload_Descriptor_Reference()
    {
        var draft = CreateCreateDraft();
        var payloadDescriptor = draft.Payload.GetDescriptor();

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, Empty);

        result.IsMaterialized.Should().BeTrue();
        result.ProposedInventory[0].Should().NotBeSameAs(payloadDescriptor);
    }

    [Fact]
    public void Update_Does_Not_Insert_Original_Payload_Descriptor_Reference()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var draft = CreateUpdateDraft();
        var payloadDescriptor = draft.Payload.GetDescriptor();

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));

        result.IsMaterialized.Should().BeTrue();
        result.ProposedInventory[0].Should().NotBeSameAs(payloadDescriptor);
    }

    [Fact]
    public void Update_Replaces_Descriptor_Using_Cloned_Replacement()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var draft = CreateUpdateDraft();

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));

        result.IsMaterialized.Should().BeTrue();
        var proposedSchema = result.ProposedInventory[0] as SchemaDescriptor;
        proposedSchema.Should().NotBeNull();
        proposedSchema!.Id.Should().Be("schema1");
        proposedSchema.Version.Should().Be(2);
        proposedSchema.Name.Should().Be("Updated");
        proposedSchema.Should().NotBeSameAs(existing);
    }

    // --- Collection State Isolation Tests ---

    [Fact]
    public void Create_Does_Not_Share_Collection_State_With_CurrentInventory()
    {
        var sourceFields = new List<SchemaFieldDescriptor>
        {
            new() { Name = "Title", FieldType = "string" }
        };
        var existing = new SchemaDescriptor
        {
            Id = "schema1", Name = "Existing", Version = 1,
            State = DescriptorState.Active,
            Fields = sourceFields
        };
        var draft = CreateCreateDraft(id: "schema2", version: 1);

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));

        sourceFields.Add(new SchemaFieldDescriptor { Name = "Injected", FieldType = "string" });

        result.IsMaterialized.Should().BeTrue();
        var proposedExisting = result.ProposedInventory
            .OfType<SchemaDescriptor>()
            .Single(x => x.Id == "schema1");
        proposedExisting.Fields.Should().HaveCount(1);
        proposedExisting.Fields.Should().NotContain(f => f.Name == "Injected");
    }

    [Fact]
    public void Create_Does_Not_Share_Collection_State_With_DraftPayloadDescriptor()
    {
        var sourceFields = new List<SchemaFieldDescriptor>
        {
            new() { Name = "Title", FieldType = "string" }
        };
        var desc = new SchemaDescriptor
        {
            Id = "schema1", Name = "New", Version = 1,
            State = DescriptorState.Active,
            Fields = sourceFields
        };
        var draft = new Draft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "schema1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(desc),
            ProposedVersion = "1"
        };

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, Empty);

        sourceFields.Add(new SchemaFieldDescriptor { Name = "Injected", FieldType = "string" });

        result.IsMaterialized.Should().BeTrue();
        var proposedSchema = result.ProposedInventory
            .OfType<SchemaDescriptor>()
            .Single(x => x.Id == "schema1");
        proposedSchema.Fields.Should().HaveCount(1);
        proposedSchema.Fields.Should().NotContain(f => f.Name == "Injected");
    }

    [Fact]
    public void Update_Does_Not_Share_Collection_State_With_CurrentInventory()
    {
        // Put mutable collection on v2 (NOT the one being replaced)
        var sourceFields = new List<SchemaFieldDescriptor>
        {
            new() { Name = "Title", FieldType = "string" }
        };
        var v1 = new SchemaDescriptor
        {
            Id = "schema1", Name = "Old", Version = 1,
            State = DescriptorState.Active
        };
        var v2 = new SchemaDescriptor
        {
            Id = "schema2", Name = "Other", Version = 1,
            State = DescriptorState.Active,
            Fields = sourceFields
        };
        var inventory = new List<IDescriptor> { v1, v2 };
        var draft = CreateUpdateDraft();

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, inventory);

        // Mutate the source list after materialization
        sourceFields.Add(new SchemaFieldDescriptor { Name = "Injected", FieldType = "string" });

        result.IsMaterialized.Should().BeTrue();
        // v2 is NOT replaced by the update — its Fields should be defensively copied
        var proposedV2 = result.ProposedInventory
            .OfType<SchemaDescriptor>()
            .Single(x => x.Id == "schema2");
        proposedV2.Fields.Should().HaveCount(1);
        proposedV2.Fields.Should().NotContain(f => f.Name == "Injected");
    }

    [Fact]
    public void Update_Does_Not_Share_Collection_State_With_DraftPayloadDescriptor()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active };
        var sourceFields = new List<SchemaFieldDescriptor>
        {
            new() { Name = "Title", FieldType = "string" }
        };
        var desc = new SchemaDescriptor
        {
            Id = "schema1", Name = "Updated", Version = 2,
            State = DescriptorState.Active,
            Fields = sourceFields
        };
        var draft = new Draft
        {
            TenantId = "t1", DraftId = "d2", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "schema1", Operation = DescriptorDraftOperation.Update,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(desc),
            BaseVersion = "1", ProposedVersion = "2"
        };

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));

        sourceFields.Add(new SchemaFieldDescriptor { Name = "Injected", FieldType = "string" });

        result.IsMaterialized.Should().BeTrue();
        var proposedSchema = result.ProposedInventory
            .OfType<SchemaDescriptor>()
            .Single(x => x.Id == "schema1" && x.Version == 2);
        proposedSchema.Fields.Should().HaveCount(1);
        proposedSchema.Fields.Should().NotContain(f => f.Name == "Injected");
    }

    [Fact]
    public void FormFieldDescriptor_Metadata_Is_Defensively_Copied()
    {
        var sourceMetadata = new Dictionary<string, string>
        {
            ["role"] = "admin"
        };
        var sourceFields = new List<FormFieldDescriptor>
        {
            new()
            {
                SchemaFieldName = "title",
                Label = "Title",
                Metadata = sourceMetadata
            }
        };
        var desc = new FormDescriptor
        {
            Id = "form1", Name = "TestForm", Version = 1,
            State = DescriptorState.Active,
            Fields = sourceFields
        };
        var draft = new Draft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Form,
            DescriptorId = "form1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new FormDescriptorDraftPayload(desc),
            ProposedVersion = "1"
        };

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft, Empty);

        sourceMetadata["injected"] = "evil";

        result.IsMaterialized.Should().BeTrue();
        var proposedForm = result.ProposedInventory
            .OfType<FormDescriptor>()
            .Single(x => x.Id == "form1");
        proposedForm.Fields[0].Metadata.Should().NotContainKey("injected");
        proposedForm.Fields[0].Metadata.Should().HaveCount(1);
    }

    [Fact]
    public void GeneratedEventDescriptor_In_CurrentInventory_Is_Snapshotted()
    {
        var sourceProducers = new List<string> { "svc-order" };
        var generatedEvent = new GeneratedEventDescriptor
        {
            Id = "evt_test", Name = "TestEvent", Version = 1,
            State = DescriptorState.Active,
            Producers = sourceProducers
        };
        var draft = CreateCreateDraft(id: "schema2", version: 1);

        var result = new DefaultDescriptorDraftMaterializer().Materialize(draft,
            new List<IDescriptor> { generatedEvent });

        // Mutate the source list after materialization
        sourceProducers.Add("svc-injected");

        result.IsMaterialized.Should().BeTrue();
        var proposedEvent = result.ProposedInventory
            .OfType<GeneratedEventDescriptor>()
            .Single(x => x.Id == "evt_test");
        proposedEvent.Producers.Should().HaveCount(1);
        proposedEvent.Producers.Should().NotContain("svc-injected");
    }
}
