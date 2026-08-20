using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

internal static class DescriptorDraftStoreSemantics
{
    public static void ValidateSaveInput(DescriptorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.TenantId);
        ArgumentNullException.ThrowIfNull(draft.DraftId);
        ArgumentNullException.ThrowIfNull(draft.Payload);

        if (!IsDefined(draft.DescriptorKind))
            throw new ArgumentOutOfRangeException(nameof(draft), $"DescriptorKind value {(int)draft.DescriptorKind} is not defined.");
        if (!IsDefined(draft.Operation))
            throw new ArgumentOutOfRangeException(nameof(draft), $"Operation value {(int)draft.Operation} is not defined.");
        if (!IsDefined(draft.AuthorKind))
            throw new ArgumentOutOfRangeException(nameof(draft), $"AuthorKind value {(int)draft.AuthorKind} is not defined.");
        if (!IsDefined(draft.Status))
            throw new ArgumentOutOfRangeException(nameof(draft), $"Status value {(int)draft.Status} is not defined.");
    }

    public static void ValidateGetInput(string tenantId, string draftId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(draftId);
    }

    public static void ValidateListInput(string tenantId, DraftQuery? query)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        if (query is not null)
        {
            if (query.DescriptorKind.HasValue && !IsDefined(query.DescriptorKind.Value))
                throw new ArgumentOutOfRangeException(nameof(query), $"DescriptorKind value {(int)query.DescriptorKind.Value} is not defined.");
            if (query.Operation.HasValue && !IsDefined(query.Operation.Value))
                throw new ArgumentOutOfRangeException(nameof(query), $"Operation value {(int)query.Operation.Value} is not defined.");
            if (query.AuthorKind.HasValue && !IsDefined(query.AuthorKind.Value))
                throw new ArgumentOutOfRangeException(nameof(query), $"AuthorKind value {(int)query.AuthorKind.Value} is not defined.");
            if (query.Status.HasValue && !IsDefined(query.Status.Value))
                throw new ArgumentOutOfRangeException(nameof(query), $"Status value {(int)query.Status.Value} is not defined.");
        }
    }

    public static bool MatchesQuery(DescriptorDraft draft, DraftQuery? query)
    {
        if (query is null) return true;

        if (query.DescriptorKind.HasValue && draft.DescriptorKind != query.DescriptorKind.Value)
            return false;
        if (query.Operation.HasValue && draft.Operation != query.Operation.Value)
            return false;
        if (query.AuthorKind.HasValue && draft.AuthorKind != query.AuthorKind.Value)
            return false;
        if (query.Status.HasValue && draft.Status != query.Status.Value)
            return false;
        if (query.CreatedFrom.HasValue && draft.CreatedAt.UtcTicks < query.CreatedFrom.Value.UtcTicks)
            return false;
        if (query.CreatedTo.HasValue && draft.CreatedAt.UtcTicks > query.CreatedTo.Value.UtcTicks)
            return false;

        return true;
    }

    public static IEnumerable<DescriptorDraft> OrderDrafts(IEnumerable<DescriptorDraft> drafts)
        => drafts.OrderBy(d => d.DraftId, StringComparer.Ordinal);

    private static bool IsDefined(DescriptorKind value)
        => value is DescriptorKind.Unknown
            or DescriptorKind.Schema
            or DescriptorKind.Capability
            or DescriptorKind.Event
            or DescriptorKind.Workflow
            or DescriptorKind.Form
            or DescriptorKind.HumanTask
            or DescriptorKind.DynamicApiEndpoint
            or DescriptorKind.McpTool
            or DescriptorKind.AgentTool;

    private static bool IsDefined(DescriptorDraftOperation value)
        => value is DescriptorDraftOperation.Create
            or DescriptorDraftOperation.Update
            or DescriptorDraftOperation.Deprecate
            or DescriptorDraftOperation.Remove;

    private static bool IsDefined(DescriptorDraftAuthorKind value)
        => value is DescriptorDraftAuthorKind.Human
            or DescriptorDraftAuthorKind.Agent
            or DescriptorDraftAuthorKind.System
            or DescriptorDraftAuthorKind.Import
            or DescriptorDraftAuthorKind.Generator;

    private static bool IsDefined(DescriptorDraftStatus value)
        => value is DescriptorDraftStatus.Created
            or DescriptorDraftStatus.Invalid
            or DescriptorDraftStatus.Materialized
            or DescriptorDraftStatus.Reviewed
            or DescriptorDraftStatus.Cancelled;
}
