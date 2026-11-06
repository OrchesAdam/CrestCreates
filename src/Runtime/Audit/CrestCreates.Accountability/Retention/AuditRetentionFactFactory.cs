using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Abstractions.Semantics;

namespace CrestCreates.Accountability.Retention;

internal static class AuditRetentionFactFactory
{
    public static AuditEnvelope Batch(
        AuditRetentionRunRecord run,
        AuditRetentionBatchReceipt receipt)
    {
        var runId = AuditRetentionSemanticNames.RunReceiptId(run.Key);
        var tags = BaseTags(run.Key, run.ReasonCode, run.PolicyHash)
            .Add("retention.cutoff", run.Cutoff.ToString("O"))
            .Add("retention.batch", receipt.BatchOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Add("retention.deleted", receipt.DeletedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new AuditEnvelope
        {
            AuditId = AuditRetentionSemanticNames.BatchFactAuditId(runId, receipt.BatchOrdinal),
            OccurredAt = receipt.CommittedAt,
            TenantId = run.Key.Scope.IsHost ? null : run.Key.Scope.TenantId,
            CorrelationId = run.CorrelationId,
            Actor = run.Actor,
            Action = new AuditAction
            {
                Kind = AuditRetentionSemanticNames.ActionKind,
                Name = AuditRetentionSemanticNames.BatchActionName
            },
            Target = new AuditTarget
            {
                Kind = AuditRetentionSemanticNames.BatchTargetKind,
                Id = AuditRetentionSemanticNames.BatchReceiptId(runId, receipt.BatchOrdinal),
                Version = receipt.BatchOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            Outcome = new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Succeeded,
                Code = receipt.DeletedCount == 0 ? "no-candidates" : "purged",
                SafeSummary = $"deleted={receipt.DeletedCount}; batch={receipt.BatchOrdinal}"
            },
            Tags = tags
        };
    }

    public static AuditEnvelope Hold(
        AuditHold hold,
        bool release)
    {
        var actor = release ? hold.ReleaseActor ?? hold.Actor : hold.Actor;
        var correlationId = release ? hold.ReleaseCorrelationId ?? hold.CorrelationId : hold.CorrelationId;
        var reasonCode = release ? hold.ReleaseReasonCode ?? hold.ReasonCode : hold.ReasonCode;
        var occurredAt = release ? hold.ReleasedAt ?? hold.CreatedAt : hold.CreatedAt;
        var tags = BaseTags(
                new AuditRetentionRunKey
                {
                    StoreId = hold.StoreId,
                    SinkId = hold.SinkId,
                    Scope = hold.Scope,
                    RequestId = hold.HoldId
                },
                reasonCode,
                policyHash: string.Empty)
            .Add("retention.hold", hold.HoldId)
            .Add("retention.audit_id", hold.AuditId)
            .Add("retention.version", hold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new AuditEnvelope
        {
            AuditId = AuditRetentionSemanticNames.HoldFactAuditId(hold.HoldId, hold.Version),
            OccurredAt = occurredAt,
            TenantId = hold.Scope.IsHost ? null : hold.Scope.TenantId,
            CorrelationId = correlationId,
            Actor = actor,
            Action = new AuditAction
            {
                Kind = AuditRetentionSemanticNames.ActionKind,
                Name = release
                    ? AuditRetentionSemanticNames.HoldReleaseActionName
                    : AuditRetentionSemanticNames.HoldPlaceActionName
            },
            Target = new AuditTarget
            {
                Kind = AuditRetentionSemanticNames.HoldTargetKind,
                Id = hold.HoldId,
                Version = hold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            Outcome = new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Succeeded,
                Code = release ? "released" : "placed"
            },
            Tags = tags
        };
    }

    private static ImmutableSortedDictionary<string, string> BaseTags(
        AuditRetentionRunKey key,
        string reasonCode,
        string policyHash)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        builder["retention.store"] = key.StoreId;
        builder["retention.sink"] = key.SinkId;
        builder["retention.scope"] = key.Scope.StableSignature();
        builder["retention.reason"] = reasonCode;
        if (policyHash.Length > 0)
            builder["retention.policy_hash"] = policyHash;
        return builder.ToImmutable();
    }
}
