using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// AoT-safe parser for DescriptorActivationReviewDecision from untyped HumanTask completion results.
/// Uses the pre-generated JsonSerializerContext instead of reflection-based deserialization.
/// Falls back to manual field extraction when TenantId/CorrelationId are absent from the JSON
/// (these are populated by the event handler from the HumanTask instance).
/// </summary>
public static class DescriptorActivationReviewDecisionParser
{
    public static bool TryParseReviewDecision(
        object? result,
        out DescriptorActivationReviewDecision? decision,
        out string? error)
    {
        decision = null;
        error = null;

        if (result is null)
        {
            error = "HumanTask completion result is null — cannot parse activation review decision.";
            return false;
        }

        if (result is DescriptorActivationReviewDecision typed)
        {
            decision = typed;
            return true;
        }

        if (result is JsonElement jsonElement)
        {
            // Try AoT-safe deserialization via JsonSerializerContext.
            // This succeeds when the JSON includes all required fields including
            // TenantId/CorrelationId.
            try
            {
                decision = jsonElement.Deserialize(
                    AgentControlPlaneToolJsonSerializerContext.Default.DescriptorActivationReviewDecision);
                if (decision is not null)
                    return true;
            }
            catch (JsonException)
            {
                // Expected if TenantId/CorrelationId are absent.
                // Fall through to manual extraction below.
            }

            // Fallback: manually extract fields with optional TenantId/CorrelationId.
            // CanonicalHash sub-objects are deserialized via the context.
            return TryParseFromJsonElement(jsonElement, out decision, out error);
        }

        error = $"HumanTask completion result type '{result.GetType().Name}' is not a valid DescriptorActivationReviewDecision.";
        return false;
    }

    private static bool TryParseFromJsonElement(
        JsonElement jsonElement,
        out DescriptorActivationReviewDecision? decision,
        out string? error)
    {
        decision = null;
        error = null;

        try
        {
            // Required fields
            if (!TryGetString(jsonElement, "activationRequestId", out var activationRequestId)
                && !TryGetString(jsonElement, "ActivationRequestId", out activationRequestId))
            {
                error = "Missing required field 'ActivationRequestId' in activation review decision JSON.";
                return false;
            }
            if (!TryGetString(jsonElement, "decision", out var decisionStr)
                && !TryGetString(jsonElement, "Decision", out decisionStr))
            {
                error = "Missing required field 'Decision' in activation review decision JSON.";
                return false;
            }
            if (!TryGetString(jsonElement, "actorKind", out var actorKindStr)
                && !TryGetString(jsonElement, "ActorKind", out actorKindStr))
            {
                error = "Missing required field 'ActorKind' in activation review decision JSON.";
                return false;
            }
            if (!TryGetString(jsonElement, "actorId", out var actorId)
                && !TryGetString(jsonElement, "ActorId", out actorId))
            {
                error = "Missing required field 'ActorId' in activation review decision JSON.";
                return false;
            }
            TryGetString(jsonElement, "reason", out var reason);
            TryGetString(jsonElement, "Reason", out reason);
            reason ??= string.Empty;

            if (!TryGetDateTimeOffset(jsonElement, "decidedAt", out var decidedAt)
                && !TryGetDateTimeOffset(jsonElement, "DecidedAt", out decidedAt))
            {
                error = "Missing or invalid field 'DecidedAt' in activation review decision JSON.";
                return false;
            }

            // CanonicalHash sub-objects — deserialize via context for AoT safety
            if (!TryGetProperty(jsonElement, "boundEvidenceHash", out var evidenceHashElement)
                && !TryGetProperty(jsonElement, "BoundEvidenceHash", out evidenceHashElement))
            {
                error = "Missing required field 'BoundEvidenceHash' in activation review decision JSON.";
                return false;
            }
            var boundEvidenceHash = evidenceHashElement!.Value.Deserialize(
                AgentControlPlaneToolJsonSerializerContext.Default.CanonicalHash);
            if (boundEvidenceHash is null)
            {
                error = "Failed to deserialize 'BoundEvidenceHash' CanonicalHash.";
                return false;
            }

            if (!TryGetProperty(jsonElement, "boundEnvelopeHash", out var envelopeHashElement)
                && !TryGetProperty(jsonElement, "BoundEnvelopeHash", out envelopeHashElement))
            {
                error = "Missing required field 'BoundEnvelopeHash' in activation review decision JSON.";
                return false;
            }
            var boundEnvelopeHash = envelopeHashElement!.Value.Deserialize(
                AgentControlPlaneToolJsonSerializerContext.Default.CanonicalHash);
            if (boundEnvelopeHash is null)
            {
                error = "Failed to deserialize 'BoundEnvelopeHash' CanonicalHash.";
                return false;
            }

            // Optional TenantId/CorrelationId — set defaults if absent.
            // The event handler enriches these from the HumanTask instance.
            TryGetString(jsonElement, "tenantId", out var tenantId);
            TryGetString(jsonElement, "TenantId", out tenantId);
            tenantId ??= string.Empty;

            TryGetString(jsonElement, "correlationId", out var correlationId);
            TryGetString(jsonElement, "CorrelationId", out correlationId);
            correlationId ??= string.Empty;

            // Enums
            if (!Enum.TryParse<DescriptorActivationReviewOutcome>(decisionStr, ignoreCase: true, out var outcome))
            {
                error = $"Invalid Decision value '{decisionStr}' in activation review decision JSON.";
                return false;
            }
            if (!Enum.TryParse<DescriptorActivationActorKind>(actorKindStr, ignoreCase: true, out var actorKind))
            {
                error = $"Invalid ActorKind value '{actorKindStr}' in activation review decision JSON.";
                return false;
            }

            decision = new DescriptorActivationReviewDecision
            {
                ActivationRequestId = activationRequestId!,
                TenantId = tenantId,
                CorrelationId = correlationId,
                Decision = outcome,
                ActorKind = actorKind,
                ActorId = actorId!,
                Reason = reason,
                DecidedAt = decidedAt,
                BoundEvidenceHash = boundEvidenceHash,
                BoundEnvelopeHash = boundEnvelopeHash
            };

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse activation review decision from JsonElement: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return value is not null;
        }
        value = null;
        return false;
    }

    private static bool TryGetDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        if (element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(prop.GetString(), out value))
        {
            return true;
        }
        value = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement? value)
    {
        if (element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind != JsonValueKind.Null)
        {
            value = prop;
            return true;
        }
        value = null;
        return false;
    }
}
