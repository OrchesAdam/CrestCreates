using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Sanitization;

public static class AuditProtectedFactComparer
{
    public static bool AreEqual(AuditEnvelope left, AuditEnvelope right)
    {
        if (left.Actor is null || right.Actor is null
            || left.Action is null || right.Action is null
            || left.Target is null || right.Target is null
            || left.Outcome is null || right.Outcome is null
            || left.Runtime is null || right.Runtime is null
            || left.Descriptors is null || right.Descriptors is null
            || left.Evidence.IsDefault || right.Evidence.IsDefault
            || left.Runtime.References.IsDefault || right.Runtime.References.IsDefault)
            return false;

        return left.ContractVersion == right.ContractVersion
            && string.Equals(left.AuditId, right.AuditId, StringComparison.Ordinal)
            && left.OccurredAt == right.OccurredAt
            && string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
            && string.Equals(left.CorrelationId, right.CorrelationId, StringComparison.Ordinal)
            && string.Equals(left.CausationId, right.CausationId, StringComparison.Ordinal)
            && string.Equals(left.ParentAuditId, right.ParentAuditId, StringComparison.Ordinal)
            && string.Equals(left.PreviousAuditId, right.PreviousAuditId, StringComparison.Ordinal)
            && ActorEquals(left.Actor, right.Actor)
            && left.Action == right.Action
            && string.Equals(left.Target.Kind, right.Target.Kind, StringComparison.Ordinal)
            && string.Equals(left.Target.Id, right.Target.Id, StringComparison.Ordinal)
            && string.Equals(left.Target.Version, right.Target.Version, StringComparison.Ordinal)
            && string.Equals(left.Outcome.Status, right.Outcome.Status, StringComparison.Ordinal)
            && string.Equals(left.Outcome.Code, right.Outcome.Code, StringComparison.Ordinal)
            && RuntimeEquals(left.Runtime, right.Runtime)
            && left.Descriptors == right.Descriptors
            && left.Evidence.SequenceEqual(right.Evidence);
    }

    private static bool ActorEquals(AuditActor left, AuditActor right)
        => string.Equals(left.Kind, right.Kind, StringComparison.Ordinal)
            && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && Equals(left.InitiatedBy, right.InitiatedBy)
            && Equals(left.OnBehalfOf, right.OnBehalfOf)
            && string.Equals(left.DelegationId, right.DelegationId, StringComparison.Ordinal)
            && string.Equals(left.ImpersonationId, right.ImpersonationId, StringComparison.Ordinal);

    private static bool RuntimeEquals(AuditRuntimeContext left, AuditRuntimeContext right)
        => string.Equals(left.InvocationSource, right.InvocationSource, StringComparison.Ordinal)
            && string.Equals(left.ExecutionId, right.ExecutionId, StringComparison.Ordinal)
            && string.Equals(left.RequestId, right.RequestId, StringComparison.Ordinal)
            && string.Equals(left.TraceId, right.TraceId, StringComparison.Ordinal)
            && string.Equals(left.SpanId, right.SpanId, StringComparison.Ordinal)
            && left.Duration == right.Duration
            && left.References.SequenceEqual(right.References);

}
