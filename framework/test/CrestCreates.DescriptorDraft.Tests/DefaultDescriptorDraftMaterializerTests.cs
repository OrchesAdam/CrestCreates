using CrestCreates.DescriptorDraft.Abstractions;
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
        var desc = new SchemaDescriptor { Id = id, Name = "Test", Version = version, State = DescriptorState.Active, ContractHash = "abc", DefinitionHash = "def" };
        return new Draft { TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema, DescriptorId = id, Operation = DescriptorDraftOperation.Create, AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "u1", CreatedAt = DateTimeOffset.UtcNow, Payload = new SchemaDescriptorDraftPayload(desc), ProposedVersion = version.ToString() };
    }

    private static Draft CreateUpdateDraft(string id = "schema1", int baseVer = 1, int proposedVer = 2)
    {
        var desc = new SchemaDescriptor { Id = id, Name = "Updated", Version = proposedVer, State = DescriptorState.Active, ContractHash = "xyz", DefinitionHash = "uvw" };
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
        var existing = new SchemaDescriptor { Id = "schema1", Name = "X", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
        var r = new DefaultDescriptorDraftMaterializer().Materialize(CreateCreateDraft(), With(existing));
        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "CREATE_DESCRIPTOR_EXISTS");
    }

    [Fact] public void Update_Replaces_Descriptor()
    {
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
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
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
        var original = With(existing);
        new DefaultDescriptorDraftMaterializer().Materialize(CreateUpdateDraft(), original);
        original.Should().HaveCount(1);
        original[0].Should().Be(existing, "source inventory must not be mutated");
    }

    [Fact] public void Create_DifferentVersion_NotDuplicate()
    {
        // Inventory has schema1 v1; creating schema1 v2 should succeed (different version)
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
        var draft = CreateCreateDraft("schema1", version: 2);
        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));
        r.IsMaterialized.Should().BeTrue();
        r.ProposedInventory.Should().HaveCount(2, "v1 and v2 should both exist");
    }

    [Fact] public void Update_WrongBaseVersion_Fails()
    {
        // Inventory has schema1 v1; updating with baseVersion=2 should fail
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
        var draft = CreateUpdateDraft("schema1", baseVer: 2, proposedVer: 3);
        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, With(existing));
        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "UPDATE_BASE_NOT_FOUND");
    }

    [Fact] public void Update_OnlyReplaces_MatchedVersion()
    {
        // Inventory has schema1 v1 and v2; updating v1 → v3 should only replace v1
        var v1 = new SchemaDescriptor { Id = "schema1", Name = "V1", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
        var v2 = new SchemaDescriptor { Id = "schema1", Name = "V2", Version = 2, State = DescriptorState.Active, ContractHash = "x", DefinitionHash = "y" };
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
        var baseItem = new SchemaDescriptor { Id = "schema1", Name = "Old", Version = 1, State = DescriptorState.Active, ContractHash = "a", DefinitionHash = "b" };
        var conflictingItem = new SchemaDescriptor { Id = "schema1", Name = "Existing V2", Version = 2, State = DescriptorState.Active, ContractHash = "x", DefinitionHash = "y" };
        var inventory = new List<IDescriptor> { baseItem, conflictingItem };
        var draft = CreateUpdateDraft("schema1", baseVer: 1, proposedVer: 2);

        var r = new DefaultDescriptorDraftMaterializer().Materialize(draft, inventory);

        r.IsMaterialized.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Code == "UPDATE_DESCRIPTOR_EXISTS");
    }
}
