using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft.Tests.Persistence;

public sealed class DescriptorDraftStoreContractTests
{
    [Theory]
    [InlineData(DescriptorPayloadVariant.Schema)]
    [InlineData(DescriptorPayloadVariant.Form)]
    [InlineData(DescriptorPayloadVariant.Capability)]
    [InlineData(DescriptorPayloadVariant.HumanTask)]
    [InlineData(DescriptorPayloadVariant.Event)]
    [InlineData(DescriptorPayloadVariant.WorkflowCapabilityTarget)]
    [InlineData(DescriptorPayloadVariant.WorkflowHumanTaskTarget)]
    [InlineData(DescriptorPayloadVariant.WorkflowSubWorkflowTarget)]
    public async Task DescriptorDraftPayloadVariant_Should_RoundTripCompleteSnapshot(DescriptorPayloadVariant variant)
    {
        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(variant);

        await driver.Store.SaveAsync(draft);

        (await driver.Store.GetAsync(draft.TenantId, draft.DraftId))
            .Should().BeEquivalentTo(draft);
    }

    [Fact]
    public async Task DescriptorDraft_Save_Should_CaptureSnapshot()
    {
        var driver = NewDriver();
        var metadata = new Dictionary<string, string> { ["state"] = "before" };
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { Metadata = metadata };
        await driver.Store.SaveAsync(draft);
        metadata["state"] = "after";
        metadata["new"] = "mutated";

        var stored = await driver.Store.GetAsync(draft.TenantId, draft.DraftId);
        stored.Should().NotBeSameAs(draft);
        stored!.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["state"] = "before" });
    }

    [Fact]
    public async Task DescriptorDraft_Read_Should_ReturnDetachedSnapshot()
    {
        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Form) with
        {
            Metadata = new Dictionary<string, string> { ["state"] = "saved" }
        };
        await driver.Store.SaveAsync(draft);

        var first = (await driver.Store.GetAsync(draft.TenantId, draft.DraftId))!;
        ((Dictionary<string, string>)first.Metadata!)["state"] = "mutated";

        (await driver.Store.GetAsync(draft.TenantId, draft.DraftId))!.Metadata!["state"]
            .Should().Be("saved");
    }

    [Fact]
    public async Task DescriptorDraft_SameIdInTwoTenants_Should_NotCollide()
    {
        var driver = NewDriver();
        var first = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { TenantId = "tenant-a", DraftId = "same" };
        var second = first with { TenantId = "tenant-b" };
        await driver.Store.SaveAsync(first);
        await driver.Store.SaveAsync(second);

        (await driver.Store.GetAsync("tenant-a", "same"))!.TenantId.Should().Be("tenant-a");
        (await driver.Store.GetAsync("tenant-b", "same"))!.TenantId.Should().Be("tenant-b");
    }

    [Theory]
    [InlineData(DraftQueryVariant.DescriptorKind)]
    [InlineData(DraftQueryVariant.Operation)]
    [InlineData(DraftQueryVariant.AuthorKind)]
    [InlineData(DraftQueryVariant.Status)]
    [InlineData(DraftQueryVariant.CreatedFrom)]
    [InlineData(DraftQueryVariant.CreatedTo)]
    [InlineData(DraftQueryVariant.Combined)]
    public async Task DescriptorDraftQueryVariant_Should_PreserveSemantics(DraftQueryVariant variant)
    {
        var driver = NewDriver();
        var first = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with
        {
            DraftId = "query-a",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            Status = DescriptorDraftStatus.Created
        };
        var second = first with
        {
            DraftId = "query-b",
            CreatedAt = DateTimeOffset.UnixEpoch.AddDays(1),
            Operation = DescriptorDraftOperation.Update,
            AuthorKind = DescriptorDraftAuthorKind.System,
            Status = DescriptorDraftStatus.Reviewed
        };
        await driver.Store.SaveAsync(first);
        await driver.Store.SaveAsync(second);
        var query = variant switch
        {
            DraftQueryVariant.DescriptorKind => new DraftQuery { DescriptorKind = DescriptorKind.Schema },
            DraftQueryVariant.Operation => new DraftQuery { Operation = DescriptorDraftOperation.Update },
            DraftQueryVariant.AuthorKind => new DraftQuery { AuthorKind = DescriptorDraftAuthorKind.System },
            DraftQueryVariant.Status => new DraftQuery { Status = DescriptorDraftStatus.Reviewed },
            DraftQueryVariant.CreatedFrom => new DraftQuery { CreatedFrom = DateTimeOffset.UnixEpoch.AddDays(1) },
            DraftQueryVariant.CreatedTo => new DraftQuery { CreatedTo = DateTimeOffset.UnixEpoch },
            DraftQueryVariant.Combined => new DraftQuery
            {
                DescriptorKind = DescriptorKind.Schema,
                Operation = DescriptorDraftOperation.Update,
                AuthorKind = DescriptorDraftAuthorKind.System,
                Status = DescriptorDraftStatus.Reviewed,
                CreatedFrom = DateTimeOffset.UnixEpoch.AddDays(1),
                CreatedTo = DateTimeOffset.UnixEpoch.AddDays(1)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };

        var result = await driver.Store.ListAsync(first.TenantId, query);
        result.Should().HaveCount(variant == DraftQueryVariant.DescriptorKind ? 2 : 1);
    }

    [Fact]
    public async Task DescriptorDraft_List_Should_OrderByDraftIdOrdinal()
    {
        var driver = NewDriver();
        foreach (var id in new[] { "z", "a", "A" })
            await driver.Store.SaveAsync(driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { DraftId = id });

        (await driver.Store.ListAsync("tenant-1")).Select(value => value.DraftId)
            .Should().Equal("A", "a", "z");
    }

    [Fact]
    public async Task DescriptorDraft_Save_Should_ReplaceCompleteSnapshot()
    {
        var driver = NewDriver();
        var first = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema);
        await driver.Store.SaveAsync(first);
        await driver.Store.SaveAsync(first with { Intent = "replacement", Status = DescriptorDraftStatus.Reviewed });

        var stored = await driver.Store.GetAsync(first.TenantId, first.DraftId);
        stored!.Intent.Should().Be("replacement");
        stored.Status.Should().Be(DescriptorDraftStatus.Reviewed);
    }

    [Theory]
    [InlineData(DraftValidatorOwnedInvalidVariant.DraftIdBlank, EvidenceVectorKey.Empty)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DraftIdBlank, EvidenceVectorKey.Whitespace)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DescriptorIdBlank, EvidenceVectorKey.Null)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DescriptorIdBlank, EvidenceVectorKey.Empty)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DescriptorIdBlank, EvidenceVectorKey.Whitespace)]
    [InlineData(DraftValidatorOwnedInvalidVariant.AuthorIdBlank, EvidenceVectorKey.Null)]
    [InlineData(DraftValidatorOwnedInvalidVariant.AuthorIdBlank, EvidenceVectorKey.Empty)]
    [InlineData(DraftValidatorOwnedInvalidVariant.AuthorIdBlank, EvidenceVectorKey.Whitespace)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch, EvidenceVectorKey.Unknown)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch, EvidenceVectorKey.DynamicApiEndpoint)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch, EvidenceVectorKey.McpTool)]
    [InlineData(DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch, EvidenceVectorKey.AgentTool)]
    public async Task DraftValidatorOwnedInvalidVariant_Should_RemainDurableAndDiagnosable(
        DraftValidatorOwnedInvalidVariant variant,
        EvidenceVectorKey key)
    {
        var driver = NewDriver();
        var draft = variant == DraftValidatorOwnedInvalidVariant.DraftIdBlank
            ? driver.CreateValidatorOwnedInvalid(variant) with
            {
                DraftId = key == EvidenceVectorKey.Empty ? string.Empty : "   "
            }
            : driver.CreateValidatorOwnedInvalid(variant, key);

        await driver.Store.SaveAsync(draft);
        var stored = await driver.Store.GetAsync(draft.TenantId, draft.DraftId);
        stored.Should().NotBeNull();
        driver.Validator.Validate(stored!).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task DescriptorDraft_TimeFilter_Should_PreserveHundredNanosecondBoundaries()
    {
        var driver = NewDriver();
        var at = DateTimeOffset.UnixEpoch.AddTicks(1);
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { CreatedAt = at };
        await driver.Store.SaveAsync(draft);

        (await driver.Store.ListAsync(draft.TenantId, new DraftQuery { CreatedFrom = at, CreatedTo = at }))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task DescriptorDraft_TimeFilter_Should_CompareUtcTicksNotOffset()
    {
        var driver = NewDriver();
        var value = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var equivalent = value.ToOffset(TimeSpan.FromHours(5));
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { CreatedAt = value };
        await driver.Store.SaveAsync(draft);

        (await driver.Store.ListAsync(draft.TenantId, new DraftQuery { CreatedFrom = equivalent, CreatedTo = equivalent }))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task DescriptorDraft_CreatedAt_Should_PreserveOriginalOffsetAndTicks()
    {
        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with
        {
            CreatedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5))
        };
        await driver.Store.SaveAsync(draft);

        (await driver.Store.GetAsync(draft.TenantId, draft.DraftId))!.CreatedAt
            .Should().Be(draft.CreatedAt);
    }

    [Theory]
    [InlineData(IdentityValidationVector.DraftNullInstance)]
    [InlineData(IdentityValidationVector.DraftNullTenantId)]
    [InlineData(IdentityValidationVector.DraftNullDraftId)]
    [InlineData(IdentityValidationVector.DraftNullPayload)]
    [InlineData(IdentityValidationVector.DraftGetNullTenantId)]
    [InlineData(IdentityValidationVector.DraftGetNullDraftId)]
    [InlineData(IdentityValidationVector.DraftListNullTenantId)]
    public async Task IdentityValidationVector_Should_FailBeforeMutation(IdentityValidationVector variant)
    {
        var driver = NewDriver();
        Func<Task> act = variant switch
        {
            IdentityValidationVector.DraftNullInstance =>
                () => driver.Store.SaveAsync(null!),
            IdentityValidationVector.DraftNullTenantId =>
                () => driver.Store.SaveAsync(driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { TenantId = null! }),
            IdentityValidationVector.DraftNullDraftId =>
                () => driver.Store.SaveAsync(driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { DraftId = null! }),
            IdentityValidationVector.DraftNullPayload =>
                () => driver.Store.SaveAsync(driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { Payload = null! }),
            IdentityValidationVector.DraftGetNullTenantId =>
                async () => await driver.Store.GetAsync(null!, "draft-id"),
            IdentityValidationVector.DraftGetNullDraftId =>
                async () => await driver.Store.GetAsync("tenant-1", null!),
            IdentityValidationVector.DraftListNullTenantId =>
                async () => await driver.Store.ListAsync(null!),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(PersistedEnumSurface.DraftDescriptorKind)]
    [InlineData(PersistedEnumSurface.DraftOperation)]
    [InlineData(PersistedEnumSurface.DraftAuthorKind)]
    [InlineData(PersistedEnumSurface.DraftStatus)]
    public async Task PersistedEnumSurface_Should_FailBeforeMutation(PersistedEnumSurface surface)
    {
        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema);
        var invalid = surface switch
        {
            PersistedEnumSurface.DraftDescriptorKind => draft with { DescriptorKind = (DescriptorKind)999 },
            PersistedEnumSurface.DraftOperation => draft with { Operation = (DescriptorDraftOperation)999 },
            PersistedEnumSurface.DraftAuthorKind => draft with { AuthorKind = (DescriptorDraftAuthorKind)999 },
            PersistedEnumSurface.DraftStatus => draft with { Status = (DescriptorDraftStatus)999 },
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        await ((Func<Task>)(() => driver.Store.SaveAsync(invalid))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task UnsupportedDraftPayload_Should_FailBeforeMutation()
    {
        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with
        {
            Payload = new UnsupportedTestPayload()
        };
        await ((Func<Task>)(() => driver.Store.SaveAsync(draft))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(StoreMethodSurface.DraftSave)]
    [InlineData(StoreMethodSurface.DraftGet)]
    [InlineData(StoreMethodSurface.DraftList)]
    public async Task PreCancelledStoreMethod_Should_ExitBeforeQueryOrMutation(StoreMethodSurface surface)
    {
        var driver = NewDriver();
        var ct = new CancellationToken(canceled: true);
        Func<Task> act = surface switch
        {
            StoreMethodSurface.DraftSave =>
                () => driver.Store.SaveAsync(driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema), ct),
            StoreMethodSurface.DraftGet =>
                async () => await driver.Store.GetAsync("tenant-1", "draft-id", ct),
            StoreMethodSurface.DraftList =>
                async () => await driver.Store.ListAsync("tenant-1", ct: ct),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static InMemoryDescriptorDraftStoreContractDriver NewDriver()
        => new();

    private sealed record UnsupportedTestPayload : DescriptorDraftPayload
    {
        public override DescriptorKind DescriptorKind => DescriptorKind.Unknown;
        public override IDescriptor GetDescriptor() => throw new NotImplementedException();
        public override DescriptorDraftPayload Snapshot() => this;
    }
}
