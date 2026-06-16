using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft.Tests;

public class DefaultDescriptorDraftValidatorTests
{
    private static Draft CreateDraft(
        string draftId = "d1",
        DescriptorKind kind = DescriptorKind.Schema,
        string descriptorId = "schema1",
        DescriptorDraftOperation op = DescriptorDraftOperation.Create,
        string? baseVersion = null,
        string? proposedVersion = "1")
    {
        var version = int.TryParse(proposedVersion, out var v) ? v : 0;
        var descriptor = new SchemaDescriptor { Id = descriptorId, Name = "Test", Version = version };
        return new Draft
        {
            TenantId = "t1", DraftId = draftId, DescriptorKind = kind,
            DescriptorId = descriptorId, Operation = op,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(descriptor),
            BaseVersion = baseVersion, ProposedVersion = proposedVersion
        };
    }

    [Fact]
    public void Rejects_Empty_DraftId()
    {
        var draft = CreateDraft(draftId: "");
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "DRAFT_ID_EMPTY");
    }

    [Fact]
    public void Rejects_Kind_Payload_Mismatch()
    {
        var descriptor = new SchemaDescriptor { Id = "s1", Name = "Test" };
        var payload = new FormDescriptorDraftPayload(new Form.Abstractions.FormDescriptor { Id = "s1", Name = "Test" });
        var draft = new Draft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "s1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow, Payload = payload
        };
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "KIND_PAYLOAD_MISMATCH");
    }

    [Fact]
    public void Rejects_Payload_DescriptorId_Mismatch()
    {
        var descriptor = new SchemaDescriptor { Id = "payloadId", Name = "Test" };
        var draft = new Draft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "differentId",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(descriptor)
        };
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "PAYLOAD_ID_MISMATCH");
    }

    [Fact]
    public void Rejects_Create_With_BaseVersion()
    {
        var draft = CreateDraft(op: DescriptorDraftOperation.Create, baseVersion: "1");
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "CREATE_BASE_VERSION_MUST_BE_EMPTY");
    }

    [Fact]
    public void Rejects_Update_Without_BaseVersion()
    {
        var draft = CreateDraft(op: DescriptorDraftOperation.Update, baseVersion: null);
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "UPDATE_BASE_VERSION_REQUIRED");
    }

    [Fact]
    public void Valid_Draft_Passes_All_Checks()
    {
        var draft = CreateDraft();
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_ProposedVersion_Mismatch()
    {
        var draft = CreateDraft(proposedVersion: "3"); // payload has Version=3 from parse, descriptor has Version=3 from helper
        // Modify descriptor version to mismatch
        var mismatchDesc = new SchemaDescriptor { Id = "schema1", Name = "Test", Version = 2 };
        var mismatchDraft = new Draft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "schema1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(mismatchDesc),
            ProposedVersion = "3"
        };
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(mismatchDraft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "PROPOSED_VERSION_MISMATCH");
    }

    [Fact]
    public void Rejects_ProposedVersion_NotInteger()
    {
        var draft = CreateDraft(proposedVersion: "v3");
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "PROPOSED_VERSION_NOT_INTEGER");
    }

    [Fact]
    public void Rejects_ProposedVersion_Missing_ForCreate()
    {
        var draft = CreateDraft(proposedVersion: null);
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "PROPOSED_VERSION_MISSING");
    }
}
