using System;
using System.Text.Json;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Hand-written canonical JSON writer for <see cref="InteractionTarget"/> discriminated union.
/// The SG cannot generate this automatically because InteractionTarget is an abstract record
/// with 3 sealed subtypes (CapabilityTarget, HumanTaskTarget, SubWorkflowTarget).
///
/// Canonical JSON output:
///   CapabilityTarget  → { "Kind": "Capability", "Id": ..., "Version": ... }
///   HumanTaskTarget   → { "Kind": "HumanTask",  "Id": ..., "Version": ... }
///   SubWorkflowTarget → { "Kind": "Workflow",   "Id": ..., "Version": ... }
///
/// This writer is used by WorkflowStepCanonicalHashWriter when writing the Target field.
/// </summary>
internal static class InteractionTargetCanonicalHashWriter
{
    /// <summary>
    /// Writes the full envelope (metadata + payload). Kept for backward compat;
    /// new generated code uses WriteContractPayload/WriteDefinitionPayload.
    /// </summary>
    public static void WriteContractEnvelope(Utf8JsonWriter w, InteractionTarget target, string scopeString, string algorithmVersion, string contractVersion)
    {
        WriteCorePayload(w, target);
    }

    /// <summary>
    /// Writes the full envelope (metadata + payload). Kept for backward compat;
    /// new generated code uses WriteContractPayload/WriteDefinitionPayload.
    /// </summary>
    public static void WriteDefinitionEnvelope(Utf8JsonWriter w, InteractionTarget target, string scopeString, string algorithmVersion, string contractVersion)
    {
        WriteCorePayload(w, target);
    }

    /// <summary>
    /// Writes only the payload fields (Kind, Id, Version) without envelope metadata.
    /// Called by SG-generated writer code for sub-structure fields.
    /// </summary>
    public static void WriteContractPayload(Utf8JsonWriter w, InteractionTarget target)
    {
        WriteCorePayload(w, target);
    }

    /// <summary>
    /// Writes only the payload fields (Kind, Id, Version) without envelope metadata.
    /// Called by SG-generated writer code for sub-structure fields.
    /// </summary>
    public static void WriteDefinitionPayload(Utf8JsonWriter w, InteractionTarget target)
    {
        WriteCorePayload(w, target);
    }

    private static void WriteCorePayload(Utf8JsonWriter w, InteractionTarget target)
    {
        switch (target)
        {
            case CapabilityTarget cap:
                WriteCapabilityTarget(w, cap);
                break;
            case HumanTaskTarget ht:
                WriteHumanTaskTarget(w, ht);
                break;
            case SubWorkflowTarget sw:
                WriteSubWorkflowTarget(w, sw);
                break;
            default:
                throw new InvalidOperationException($"Unknown InteractionTarget subtype: {target.GetType().Name}");
        }
    }

    private static void WriteCapabilityTarget(Utf8JsonWriter w, CapabilityTarget target)
    {
        w.WriteStartObject();
        w.WriteString("Kind", "Capability");
        w.WriteString("Id", target.Capability.Id);
        w.WriteNumber("Version", target.Capability.Version);
        w.WriteEndObject();
    }

    private static void WriteHumanTaskTarget(Utf8JsonWriter w, HumanTaskTarget target)
    {
        w.WriteStartObject();
        w.WriteString("Kind", "HumanTask");
        w.WriteString("Id", target.HumanTask.Id);
        w.WriteNumber("Version", target.HumanTask.Version);
        w.WriteEndObject();
    }

    private static void WriteSubWorkflowTarget(Utf8JsonWriter w, SubWorkflowTarget target)
    {
        w.WriteStartObject();
        w.WriteString("Kind", "Workflow");
        w.WriteString("Id", target.SubWorkflow.Id);
        w.WriteNumber("Version", target.SubWorkflow.Version);
        w.WriteEndObject();
    }
}
