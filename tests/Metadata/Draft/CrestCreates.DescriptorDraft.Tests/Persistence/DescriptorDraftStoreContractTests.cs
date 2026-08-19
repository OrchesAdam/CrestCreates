using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft.Tests.Persistence;

public sealed class DescriptorDraftStoreContractTests
{
    [Fact]
    public async Task Draft_shared_contract_cases_Should_Run_All_Frozen_Surfaces()
    {
        await DescriptorDraftStoreContractCases.RunFrozenSemanticsAsync(
            NewDriver(),
            RequiredRunner.InMemory,
            ControlPlaneReferenceDataEvidenceLedger.Record);
    }

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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D01, "Draft", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(variant);

        await driver.Store.SaveAsync(draft);

        (await driver.Store.GetAsync(draft.TenantId, draft.DraftId))
            .Should().BeEquivalentTo(draft);
    }

    [Fact]
    public async Task DescriptorDraft_Save_Should_CaptureSnapshot()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D02, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D03, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D04, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D05, "Draft", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D06, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        foreach (var id in new[] { "z", "a", "A" })
            await driver.Store.SaveAsync(driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { DraftId = id });

        (await driver.Store.ListAsync("tenant-1")).Select(value => value.DraftId)
            .Should().Equal("A", "a", "z");
    }

    [Fact]
    public async Task DescriptorDraft_Save_Should_ReplaceCompleteSnapshot()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D07, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        var first = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema);
        await driver.Store.SaveAsync(first);
        await driver.Store.SaveAsync(first with { Intent = "replacement", Status = DescriptorDraftStatus.Reviewed });

        var stored = await driver.Store.GetAsync(first.TenantId, first.DraftId);
        stored!.Intent.Should().Be("replacement");
        stored.Status.Should().Be(DescriptorDraftStatus.Reviewed);
    }

    [Theory]
    [MemberData(nameof(ValidatorOwnedInvalidData))]
    public async Task DraftValidatorOwnedInvalidVariant_Should_RemainDurableAndDiagnosable(
        DraftValidatorOwnedInvalidVariant variant,
        EvidenceVectorKey key)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(
            CaseId.D08, "Draft", variant.ToString(), key, RequiredRunner.InMemory);

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

    public static IEnumerable<object[]> ValidatorOwnedInvalidData()
    {
        foreach (var tuple in ControlPlaneReferenceDataCaseManifest.EvidenceTuplesFor(CaseId.D08, RequiredRunner.InMemory))
            yield return new object[] { Enum.Parse<DraftValidatorOwnedInvalidVariant>(tuple.Variant), tuple.Key };
    }

    // ── F01 / F02 (Draft surface): concurrent blind save on the InMemory store ──

    [Fact]
    public async Task SaveSurface_ConcurrentBlindSave_Should_ExposeOneCompleteSnapshot()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(
            CaseId.F01, "Failure", nameof(SaveSurface.Draft), EvidenceVectorKey.Default, RequiredRunner.InMemory);

        var driver = NewDriver();
        var draftA = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with
        {
            DraftId = "concurrent",
            DescriptorId = "schema-1",
            AuthorId = "author-a",
            Intent = "intent-a",
            Status = DescriptorDraftStatus.Created,
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-1", Name = "Schema A" })
        };
        var draftB = draftA with
        {
            AuthorId = "author-b",
            Intent = "intent-b",
            Status = DescriptorDraftStatus.Reviewed,
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-1", Name = "Schema B" })
        };

        await Task.WhenAll(driver.Store.SaveAsync(draftA), driver.Store.SaveAsync(draftB));

        var result = await driver.Store.GetAsync(draftA.TenantId, draftA.DraftId);
        result.Should().NotBeNull();
        var schema = (SchemaDescriptorDraftPayload)result!.Payload;
        var matchesA = result.AuthorId == "author-a" && result.Intent == "intent-a"
            && result.Status == DescriptorDraftStatus.Created && schema.Descriptor.Name == "Schema A";
        var matchesB = result.AuthorId == "author-b" && result.Intent == "intent-b"
            && result.Status == DescriptorDraftStatus.Reviewed && schema.Descriptor.Name == "Schema B";
        (matchesA || matchesB).Should().BeTrue("the row must be one complete submitted snapshot");
    }

    [Fact]
    public async Task SaveSurface_ConcurrentBlindSave_Should_NotInventStaleWriterConflict()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(
            CaseId.F02, "Failure", nameof(SaveSurface.Draft), EvidenceVectorKey.Default, RequiredRunner.InMemory);

        var driver = NewDriver();
        var draft = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema) with { DraftId = "concurrent-no-occ" };
        var duplicate = draft with { };

        var ex = await Record.ExceptionAsync(() => Task.WhenAll(driver.Store.SaveAsync(draft), driver.Store.SaveAsync(duplicate)));
        ex.Should().BeNull();
        (await driver.Store.GetAsync(draft.TenantId, draft.DraftId)).Should().NotBeNull();
    }

    [Fact]
    public async Task DescriptorDraft_TimeFilter_Should_PreserveHundredNanosecondBoundaries()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D11, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D12, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.D13, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
    [InlineData(IdentityValidationVector.DraftNullInstance, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.DraftNullTenantId, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.DraftNullDraftId, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.DraftNullPayload, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.DraftGetNullTenantId, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.DraftGetNullDraftId, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.DraftListNullTenantId, EvidenceVectorKey.Null)]
    public async Task IdentityValidationVector_Should_FailBeforeMutation(IdentityValidationVector variant, EvidenceVectorKey key)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V01, "Validation", variant.ToString(), key, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V03, "Validation", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V04, "Validation", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V05, "Validation", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
