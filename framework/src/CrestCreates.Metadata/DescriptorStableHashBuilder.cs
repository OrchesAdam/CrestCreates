using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// AoT-safe descriptor stable hash builder using deterministic string concatenation
/// (SHA-256). No <see cref="System.Text.Json.JsonSerializer"/> — all field extraction
/// is explicit and switch-based, compatible with NativeAOT trimming.
/// </summary>
public sealed class DescriptorStableHashBuilder : IDescriptorStableHashBuilder
{
    /// <inheritdoc />
    public DescriptorStableHashes Build(IDescriptor descriptor)
    {
        var contractHash = ComputeContractHash(descriptor);
        var definitionHash = ComputeDefinitionHash(descriptor);
        return new DescriptorStableHashes(contractHash, definitionHash);
    }

    // ── Contract Hash ──────────────────────────────────────────

    private static string ComputeContractHash(IDescriptor descriptor)
    {
        var sb = new StringBuilder();
        AppendContractFields(sb, descriptor);
        return ComputeSha256(sb.ToString());
    }

    private static void AppendContractFields(StringBuilder sb, IDescriptor descriptor)
    {
        switch (descriptor)
        {
            case SchemaDescriptor s:
                AppendField(sb, s.Id);
                AppendField(sb, s.Name);
                AppendField(sb, s.Version);
                AppendField(sb, (int)s.ChangeKind);
                AppendField(sb, (int)s.State);
                AppendField(sb, s.SupersededById);
                // Fields: ordered by name
                foreach (var f in s.Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    AppendField(sb, f.Name);
                    AppendField(sb, f.FieldType);
                    AppendField(sb, f.IsRequired);
                    AppendField(sb, f.IsNullable);
                    AppendField(sb, f.MaxLength);
                    AppendField(sb, f.MinLength);
                    AppendField(sb, f.MaxValue);
                    AppendField(sb, f.MinValue);
                    AppendField(sb, f.Pattern);
                    AppendField(sb, f.IsCollection);
                    AppendField(sb, f.CollectionElementType);
                }
                // References: ordered by id
                foreach (var r in s.References.OrderBy(r => r.Id, StringComparer.Ordinal))
                {
                    AppendField(sb, r.Id);
                    AppendField(sb, r.Version);
                }
                break;

            case CapabilityDescriptor c:
                AppendField(sb, c.Id);
                AppendField(sb, c.Name);
                AppendField(sb, c.Version);
                AppendField(sb, (int)c.CapabilityKind);
                AppendField(sb, (int)c.State);
                AppendField(sb, c.SupersededById);
                AppendOptionalRef(sb, c.InputSchema);
                AppendOptionalRef(sb, c.OutputSchema);
                foreach (var p in c.Permissions.OrderBy(p => p, StringComparer.Ordinal))
                    AppendField(sb, p);
                AppendField(sb, (int)c.RiskLevel);
                foreach (var t in c.SemanticTags.OrderBy(t => t, StringComparer.Ordinal))
                    AppendField(sb, t);
                break;

            case EventDescriptor e:
                AppendField(sb, e.Id);
                AppendField(sb, e.Name);
                AppendField(sb, e.Version);
                AppendField(sb, (int)e.State);
                AppendField(sb, e.SupersededById);
                AppendField(sb, e.PayloadSchema.Id);
                AppendField(sb, e.PayloadSchema.Version);
                AppendField(sb, (int)e.Category);
                AppendField(sb, (int)e.Semantic);
                AppendField(sb, (int)e.Importance);
                AppendField(sb, (int)e.ChangeKind);
                break;

            case FormDescriptor f:
                AppendField(sb, f.Id);
                AppendField(sb, f.Name);
                AppendField(sb, f.Version);
                AppendField(sb, (int)f.State);
                AppendField(sb, f.SupersededById);
                AppendField(sb, f.Schema.Id);
                AppendField(sb, f.Schema.Version);
                foreach (var fd in f.Fields.OrderBy(fd => fd.SchemaFieldName, StringComparer.Ordinal))
                {
                    AppendField(sb, fd.SchemaFieldName);
                    AppendField(sb, fd.IsReadOnly);
                    AppendField(sb, fd.Order);
                    AppendField(sb, fd.Group);
                    AppendField(sb, fd.ControlType);
                    AppendField(sb, fd.IsRequiredOverride);
                    AppendField(sb, fd.OptionsSource);
                }
                break;

            case HumanTaskDescriptor h:
                AppendField(sb, h.Id);
                AppendField(sb, h.Name);
                AppendField(sb, h.Version);
                AppendField(sb, (int)h.State);
                AppendField(sb, h.SupersededById);
                AppendField(sb, h.Interaction.Id);
                AppendField(sb, h.Interaction.Version);
                AppendOptionalRef(sb, h.InputSchema);
                AppendOptionalRef(sb, h.OutputSchema);
                AppendField(sb, (int)h.AssigneeStrategy);
                AppendField(sb, h.Permissions);
                foreach (var o in h.Outcomes.OrderBy(o => o.Condition.ToString(), StringComparer.Ordinal))
                {
                    AppendField(sb, (int)o.Condition);
                    AppendOptionalRef(sb, o.Capability);
                }
                break;

            case WorkflowDescriptor w:
                AppendField(sb, w.Id);
                AppendField(sb, w.Name);
                AppendField(sb, w.Version);
                AppendField(sb, (int)w.State);
                AppendField(sb, w.SupersededById);
                AppendOptionalRef(sb, w.VariableSchema);
                AppendField(sb, (int)w.DefaultVariableScope);
                foreach (var s in w.Steps)
                {
                    AppendField(sb, s.Id);
                    AppendField(sb, (int)s.OnError);
                    AppendTargetRef(sb, s.Target);
                    foreach (var t in s.Transitions.OrderBy(t => t, StringComparer.Ordinal))
                        AppendField(sb, t);
                }
                break;

            default:
                // Unknown descriptor kind — include identity fields only
                AppendField(sb, descriptor.Id);
                AppendField(sb, descriptor.Name);
                break;
        }
    }

    // ── Definition Hash ────────────────────────────────────────

    private static string ComputeDefinitionHash(IDescriptor descriptor)
    {
        var sb = new StringBuilder();
        AppendDefinitionFields(sb, descriptor);
        return ComputeSha256(sb.ToString());
    }

    private static void AppendDefinitionFields(StringBuilder sb, IDescriptor descriptor)
    {
        // Common identity fields for all descriptors
        AppendField(sb, descriptor.Id);
        AppendField(sb, descriptor.Name);
        AppendField(sb, (int)descriptor.State);
        AppendField(sb, descriptor.SupersededById);

        switch (descriptor)
        {
            case SchemaDescriptor s:
                AppendField(sb, s.Version);
                AppendField(sb, (int)s.ChangeKind);
                foreach (var f in s.Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
                    AppendSchemaFieldDefinition(sb, f);
                foreach (var r in s.ValidationRules.OrderBy(r => r.Name, StringComparer.Ordinal))
                {
                    AppendField(sb, r.Name);
                    AppendField(sb, r.Expression);
                    AppendField(sb, r.ErrorMessage);
                }
                foreach (var r in s.References.OrderBy(r => r.Id, StringComparer.Ordinal))
                {
                    AppendField(sb, r.Id);
                    AppendField(sb, r.Version);
                }
                break;

            case CapabilityDescriptor c:
                AppendField(sb, c.Version);
                AppendField(sb, (int)c.CapabilityKind);
                AppendOptionalRef(sb, c.InputSchema);
                AppendOptionalRef(sb, c.OutputSchema);
                foreach (var p in c.Permissions.OrderBy(p => p, StringComparer.Ordinal))
                    AppendField(sb, p);
                AppendField(sb, (int)c.RiskLevel);
                foreach (var t in c.SemanticTags.OrderBy(t => t, StringComparer.Ordinal))
                    AppendField(sb, t);
                foreach (var cat in c.Categories.OrderBy(cat => cat, StringComparer.Ordinal))
                    AppendField(sb, cat);
                foreach (var prod in c.Produces.OrderBy(p => p.Namespace, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ThenBy(p => p.Version ?? 0))
                {
                    AppendField(sb, prod.Namespace);
                    AppendField(sb, prod.Id);
                    AppendField(sb, prod.Version);
                }
                foreach (var cons in c.Consumes.OrderBy(c => c.Namespace, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ThenBy(c => c.Version ?? 0))
                {
                    AppendField(sb, cons.Namespace);
                    AppendField(sb, cons.Id);
                    AppendField(sb, cons.Version);
                }
                break;

            case EventDescriptor e:
                AppendField(sb, e.Version);
                AppendField(sb, e.PayloadSchema.Id);
                AppendField(sb, e.PayloadSchema.Version);
                AppendField(sb, (int)e.Category);
                AppendField(sb, (int)e.Semantic);
                AppendField(sb, (int)e.Importance);
                AppendField(sb, (int)e.ChangeKind);
                break;

            case FormDescriptor f:
                AppendField(sb, f.Version);
                AppendField(sb, f.Schema.Id);
                AppendField(sb, f.Schema.Version);
                AppendField(sb, f.LayoutColumns);
                foreach (var fd in f.Fields.OrderBy(fd => fd.SchemaFieldName, StringComparer.Ordinal))
                    AppendFormFieldDefinition(sb, fd);
                break;

            case HumanTaskDescriptor h:
                AppendField(sb, h.Version);
                AppendField(sb, h.Interaction.Id);
                AppendField(sb, h.Interaction.Version);
                AppendOptionalRef(sb, h.InputSchema);
                AppendOptionalRef(sb, h.OutputSchema);
            AppendField(sb, (int)h.AssigneeStrategy);
            AppendField(sb, h.Permissions);
            AppendField(sb, h.Timeout?.ToString("c", CultureInfo.InvariantCulture) ?? "");
                foreach (var o in h.Outcomes.OrderBy(o => o.Condition.ToString(), StringComparer.Ordinal))
                {
                    AppendField(sb, (int)o.Condition);
                    AppendOptionalRef(sb, o.Capability);
                }
                break;

            case WorkflowDescriptor w:
                AppendField(sb, w.Version);
                AppendOptionalRef(sb, w.VariableSchema);
                AppendField(sb, (int)w.DefaultVariableScope);
                foreach (var s in w.Steps)
                    AppendWorkflowStepDefinition(sb, s);
                break;

            default:
                break;
        }
    }

    // ── Field-level helpers (contract + definition share these) ──

    /// <summary>Escapes backslash and pipe to prevent delimiter ambiguity.</summary>
    private static string Esc(string v) => v.Replace("\\", "\\\\").Replace("|", "\\|");

    /// <summary>Sentinel for null string fields — distinguishes null from empty.</summary>
    private const string NullSentinel = "\\0";

    private static void AppendField(StringBuilder sb, string? value)
    {
        sb.Append(value is null ? NullSentinel : Esc(value));
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, int value)
    {
        sb.Append(value);
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, int? value)
    {
        sb.Append(value?.ToString(CultureInfo.InvariantCulture) ?? "");
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, double? value)
    {
        sb.Append(value?.ToString("G", CultureInfo.InvariantCulture) ?? "");
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, bool value)
    {
        sb.Append(value ? '1' : '0');
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, bool? value)
    {
        sb.Append(value.HasValue ? (value.Value ? '1' : '0') : "");
        sb.Append('|');
    }

    private static void AppendOptionalRef<T>(StringBuilder sb, VersionedDescriptorRef<T>? vr)
        where T : IVersionedDescriptor
    {
        if (vr.HasValue)
        {
            AppendField(sb, vr.Value.Id);
            AppendField(sb, vr.Value.Version);
        }
        else
        {
            sb.Append("||");
        }
    }

    private static void AppendTargetRef(StringBuilder sb, InteractionTarget target)
    {
        switch (target)
        {
            case CapabilityTarget ct:
                AppendField(sb, "Capability");
                AppendField(sb, ct.Capability.Id);
                AppendField(sb, ct.Capability.Version);
                break;
            case HumanTaskTarget ht:
                AppendField(sb, "HumanTask");
                AppendField(sb, ht.HumanTask.Id);
                AppendField(sb, ht.HumanTask.Version);
                break;
            case SubWorkflowTarget sw:
                AppendField(sb, "SubWorkflow");
                AppendField(sb, sw.SubWorkflow.Id);
                AppendField(sb, sw.SubWorkflow.Version);
                break;
            default:
                AppendField(sb, target.GetType().Name);
                break;
        }
    }

    private static void AppendSchemaFieldDefinition(StringBuilder sb, SchemaFieldDescriptor f)
    {
        AppendField(sb, f.Name);
        AppendField(sb, f.FieldType);
        AppendField(sb, f.IsRequired);
        AppendField(sb, f.IsNullable);
        AppendField(sb, f.MaxLength);
        AppendField(sb, f.MinLength);
        AppendField(sb, f.MaxValue);
        AppendField(sb, f.MinValue);
        AppendField(sb, f.Pattern);
        AppendField(sb, f.IsCollection);
        AppendField(sb, f.CollectionElementType);
    }

    private static void AppendFormFieldDefinition(StringBuilder sb, FormFieldDescriptor fd)
    {
        AppendField(sb, fd.SchemaFieldName);
        AppendField(sb, fd.Label);
        AppendField(sb, fd.Placeholder);
        AppendField(sb, fd.HelpText);
        AppendField(sb, fd.FormatHint);
        AppendField(sb, fd.Order);
        AppendField(sb, fd.Group);
        AppendField(sb, fd.IsReadOnly);
        AppendField(sb, fd.VisibilityCondition);
        AppendField(sb, fd.ControlType);
        AppendField(sb, fd.IsRequiredOverride);
        AppendField(sb, fd.ValidationMessage);
        AppendField(sb, fd.DefaultValueExpression);
        AppendField(sb, fd.OptionsSource);
        // Dictionary: sort keys for canonical ordering
        foreach (var kv in fd.Metadata.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            AppendField(sb, kv.Key);
            AppendField(sb, kv.Value);
        }
    }

    private static void AppendWorkflowStepDefinition(StringBuilder sb, WorkflowStep s)
    {
        AppendField(sb, s.Id);
        AppendField(sb, s.Name);
        AppendField(sb, s.Condition);
        AppendField(sb, s.InputMapping);
        AppendField(sb, s.OutputMapping);
        AppendField(sb, (int)s.OnError);
        // InteractionTarget subtypes — explicit switch captures all properties
        AppendTargetRef(sb, s.Target);
        foreach (var t in s.Transitions.OrderBy(t => t, StringComparer.Ordinal))
            AppendField(sb, t);
    }

    // ── Hashing ────────────────────────────────────────────────

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
