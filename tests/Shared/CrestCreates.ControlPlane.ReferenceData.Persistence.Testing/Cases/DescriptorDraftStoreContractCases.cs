using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

/// <summary>
/// Runner-free Draft store contract primitives. Concrete runners provide the
/// same store through <see cref="IDescriptorDraftStoreContractDriver"/>.
/// </summary>
public static class DescriptorDraftStoreContractCases
{
    public static async Task RunFrozenSemanticsAsync(
        IDescriptorDraftStoreContractDriver driver,
        RequiredRunner runner,
        Action<EvidenceTuple> record)
    {
        foreach (var tuple in ControlPlaneReferenceDataCaseManifest.EvidenceTuplesFor(CaseId.D01, runner))
        {
            record(tuple);
            var draft = driver.CreatePayloadVariant(Enum.Parse<DescriptorPayloadVariant>(tuple.Variant));
            await driver.Store.SaveAsync(draft);
            var stored = await driver.Store.GetAsync(draft.TenantId, draft.DraftId)
                ?? throw new InvalidOperationException("The saved draft was not readable.");
            if (stored.Payload.GetType() != draft.Payload.GetType()
                || stored.DescriptorKind != draft.DescriptorKind
                || stored.CreatedAt != draft.CreatedAt)
                throw new InvalidOperationException($"Draft payload variant {tuple.Variant} was not round-tripped completely.");
        }

        var schema = driver.CreatePayloadVariant(DescriptorPayloadVariant.Schema);
        record(Tuple(CaseId.D02, "Draft", "Draft", runner));
        var metadata = new Dictionary<string, string> { ["state"] = "before" };
        await driver.Store.SaveAsync(schema with { Metadata = metadata });
        metadata["state"] = "after";
        var captured = await driver.Store.GetAsync(schema.TenantId, schema.DraftId)
            ?? throw new InvalidOperationException("The saved draft was not readable.");
        if (captured.Metadata!["state"] != "before")
            throw new InvalidOperationException("Draft Save must capture a detached snapshot.");

        var detached = driver.CreatePayloadVariant(DescriptorPayloadVariant.Form);
        record(Tuple(CaseId.D03, "Draft", "Draft", runner));
        await driver.Store.SaveAsync(detached);
        var first = await driver.Store.GetAsync(detached.TenantId, detached.DraftId);
        var second = await driver.Store.GetAsync(detached.TenantId, detached.DraftId);
        if (first is null || second is null || ReferenceEquals(first, second))
            throw new InvalidOperationException("Draft reads must return detached snapshots.");

        record(Tuple(CaseId.D04, "Draft", "Draft", runner));
        var tenantA = schema with { DraftId = "same", TenantId = "tenant-a" };
        var tenantB = tenantA with { TenantId = "tenant-b" };
        await driver.Store.SaveAsync(tenantA);
        await driver.Store.SaveAsync(tenantB);
        if ((await driver.Store.GetAsync("tenant-a", "same"))?.TenantId != "tenant-a"
            || (await driver.Store.GetAsync("tenant-b", "same"))?.TenantId != "tenant-b")
            throw new InvalidOperationException("Draft identity must include tenant scope.");

        foreach (var tuple in ControlPlaneReferenceDataCaseManifest.EvidenceTuplesFor(CaseId.D05, runner))
        {
            record(tuple);
            var firstDraft = schema with
            {
                TenantId = $"tenant-d05-{tuple.Variant}",
                DraftId = $"{tuple.Variant}-a", CreatedAt = DateTimeOffset.UnixEpoch,
                Operation = DescriptorDraftOperation.Create, BaseVersion = null, ProposedVersion = null,
                Status = DescriptorDraftStatus.Created
            };
            var secondDraft = firstDraft with
            {
                DraftId = $"{tuple.Variant}-b", CreatedAt = DateTimeOffset.UnixEpoch.AddDays(1),
                Operation = DescriptorDraftOperation.Update, AuthorKind = DescriptorDraftAuthorKind.System,
                Status = DescriptorDraftStatus.Reviewed, BaseVersion = null, ProposedVersion = null
            };
            await driver.Store.SaveAsync(firstDraft);
            await driver.Store.SaveAsync(secondDraft);
            var query = tuple.Variant switch
            {
                nameof(DraftQueryVariant.DescriptorKind) => new DraftQuery { DescriptorKind = DescriptorKind.Schema },
                nameof(DraftQueryVariant.Operation) => new DraftQuery { Operation = DescriptorDraftOperation.Update },
                nameof(DraftQueryVariant.AuthorKind) => new DraftQuery { AuthorKind = DescriptorDraftAuthorKind.System },
                nameof(DraftQueryVariant.Status) => new DraftQuery { Status = DescriptorDraftStatus.Reviewed },
                nameof(DraftQueryVariant.CreatedFrom) => new DraftQuery { CreatedFrom = DateTimeOffset.UnixEpoch.AddDays(1) },
                nameof(DraftQueryVariant.CreatedTo) => new DraftQuery { CreatedTo = DateTimeOffset.UnixEpoch },
                _ => new DraftQuery { DescriptorKind = DescriptorKind.Schema, Operation = DescriptorDraftOperation.Update,
                    AuthorKind = DescriptorDraftAuthorKind.System, Status = DescriptorDraftStatus.Reviewed,
                    CreatedFrom = DateTimeOffset.UnixEpoch.AddDays(1), CreatedTo = DateTimeOffset.UnixEpoch.AddDays(1) }
            };
            var result = await driver.Store.ListAsync(firstDraft.TenantId, query);
            var ids = result.Select(d => d.DraftId).ToArray();
            if (!ids.Contains(firstDraft.DraftId) && tuple.Variant == nameof(DraftQueryVariant.DescriptorKind))
                throw new InvalidOperationException($"Draft query variant {tuple.Variant} did not return its matching draft.");
            var expectedId = tuple.Variant == nameof(DraftQueryVariant.CreatedTo)
                ? firstDraft.DraftId
                : secondDraft.DraftId;
            if (!ids.Contains(expectedId))
                throw new InvalidOperationException($"Draft query variant {tuple.Variant} did not return its matching update draft.");
            if (tuple.Variant is nameof(DraftQueryVariant.Operation) or nameof(DraftQueryVariant.AuthorKind)
                or nameof(DraftQueryVariant.Status) or nameof(DraftQueryVariant.CreatedFrom)
                or nameof(DraftQueryVariant.Combined))
                if (ids.Contains(firstDraft.DraftId))
                    throw new InvalidOperationException($"Draft query variant {tuple.Variant} returned the non-matching draft.");
        }

        record(Tuple(CaseId.D06, "Draft", "Draft", runner));
        var orderingSchema = schema with { TenantId = "tenant-d06" };
        foreach (var id in new[] { "z", "a", "A" })
            await driver.Store.SaveAsync(orderingSchema with { DraftId = id });
        if (!((await driver.Store.ListAsync(orderingSchema.TenantId)).Select(d => d.DraftId).SequenceEqual(new[] { "A", "a", "z" })))
            throw new InvalidOperationException("Draft list must use ordinal DraftId ordering.");

        record(Tuple(CaseId.D07, "Draft", "Draft", runner));
        await driver.Store.SaveAsync(schema);
        await driver.Store.SaveAsync(schema with { Intent = "replacement", Status = DescriptorDraftStatus.Reviewed });
        var replacement = await driver.Store.GetAsync(schema.TenantId, schema.DraftId);
        if (replacement?.Intent != "replacement" || replacement.Status != DescriptorDraftStatus.Reviewed)
            throw new InvalidOperationException("Draft Save must replace the complete snapshot.");

        foreach (var tuple in ControlPlaneReferenceDataCaseManifest.EvidenceTuplesFor(CaseId.D08, runner))
        {
            record(tuple);
            var variant = Enum.Parse<DraftValidatorOwnedInvalidVariant>(tuple.Variant);
            var invalid = variant == DraftValidatorOwnedInvalidVariant.DraftIdBlank
                ? driver.CreateValidatorOwnedInvalid(variant) with { DraftId = tuple.Key == EvidenceVectorKey.Empty ? string.Empty : "   " }
                : driver.CreateValidatorOwnedInvalid(variant, tuple.Key);
            await driver.Store.SaveAsync(invalid);
            var stored = await driver.Store.GetAsync(invalid.TenantId, invalid.DraftId)
                ?? throw new InvalidOperationException("Validator-owned invalid draft was not durable.");
            if (driver.Validator.Validate(stored).IsValid)
                throw new InvalidOperationException($"Invalid draft {tuple.Variant}/{tuple.Key} was reported valid.");
        }

        record(Tuple(CaseId.D11, "Draft", "Draft", runner));
        var boundary = schema with { DraftId = "d11", CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(1) };
        await driver.Store.SaveAsync(boundary);
        if ((await driver.Store.ListAsync(boundary.TenantId, new DraftQuery { CreatedFrom = boundary.CreatedAt, CreatedTo = boundary.CreatedAt })).Count != 1)
            throw new InvalidOperationException("Draft time filtering must preserve hundred-nanosecond boundaries.");

        record(Tuple(CaseId.D12, "Draft", "Draft", runner));
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var offset = schema with { DraftId = "d12", CreatedAt = instant };
        await driver.Store.SaveAsync(offset);
        var equivalent = instant.ToOffset(TimeSpan.FromHours(5));
        if ((await driver.Store.ListAsync(offset.TenantId, new DraftQuery { CreatedFrom = equivalent, CreatedTo = equivalent })).Count != 1)
            throw new InvalidOperationException("Draft time filtering must compare UTC ticks.");

        record(Tuple(CaseId.D13, "Draft", "Draft", runner));
        var original = schema with { DraftId = "d13", CreatedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5)) };
        await driver.Store.SaveAsync(original);
        if ((await driver.Store.GetAsync(original.TenantId, original.DraftId))?.CreatedAt != original.CreatedAt)
            throw new InvalidOperationException("Draft CreatedAt must preserve its original offset and ticks.");
    }

    public static async Task SaveReadDetachedAsync(IDescriptorDraftStore store, Draft draft)
    {
        await store.SaveAsync(draft);
        var first = await store.GetAsync(draft.TenantId, draft.DraftId)
            ?? throw new InvalidOperationException("The saved draft was not readable.");
        var second = await store.GetAsync(draft.TenantId, draft.DraftId)
            ?? throw new InvalidOperationException("The saved draft was not readable twice.");
        if (ReferenceEquals(first, second))
            throw new InvalidOperationException("Draft reads must return detached snapshots.");
    }

    private static EvidenceTuple Tuple(string caseId, string surface, string variant, RequiredRunner runner)
        => ControlPlaneReferenceDataCaseManifest.EvidenceTuplesFor(caseId, runner)
            .Single(tuple => tuple.Surface == surface && tuple.Variant == variant);
}
