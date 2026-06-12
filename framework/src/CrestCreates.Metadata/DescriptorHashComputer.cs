using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorHashComputer
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null
    };

    public static string ComputeContractHash(IDescriptor descriptor)
    {
        var contractFields = ExtractContractFields(descriptor);
        var json = JsonSerializer.Serialize(contractFields, CanonicalOptions);
        return ComputeSha256(json);
    }

    public static string ComputeDefinitionHash(IDescriptor descriptor)
    {
        var json = JsonSerializer.Serialize(descriptor, descriptor.GetType(), CanonicalOptions);
        return ComputeSha256(json);
    }

    private static object ExtractContractFields(IDescriptor descriptor)
    {
        return descriptor switch
        {
            SchemaDescriptor s => new
            {
                s.Id,
                s.Name,
                s.Version,
                s.ChangeKind,
                s.State,
                s.SupersededById,
                Fields = s.Fields.Select(f => new
                {
                    f.Name,
                    f.FieldType,
                    f.IsRequired,
                    f.IsNullable,
                    f.MaxLength,
                    f.MinLength,
                    f.MaxValue,
                    f.MinValue,
                    f.Pattern,
                    f.IsCollection,
                    f.CollectionElementType
                }).OrderBy(f => f.Name).ToArray(),
                References = s.References.Select(r => new { r.Id, r.Version }).OrderBy(r => r.Id).ToArray()
            },
            CapabilityDescriptor c => new
            {
                c.Id,
                c.Name,
                c.Version,
                c.CapabilityKind,
                c.State,
                c.SupersededById,
                InputSchema = c.InputSchema.HasValue ? new { c.InputSchema.Value.Id, c.InputSchema.Value.Version } : null,
                OutputSchema = c.OutputSchema.HasValue ? new { c.OutputSchema.Value.Id, c.OutputSchema.Value.Version } : null,
                c.Permissions,
                c.RiskLevel,
                SemanticTags = c.SemanticTags.OrderBy(t => t).ToArray()
            },
            EventDescriptor e => new
            {
                e.Id,
                e.Name,
                e.Version,
                e.State,
                e.SupersededById,
                PayloadSchema = new { e.PayloadSchema.Id, e.PayloadSchema.Version },
                e.Category,
                e.Semantic,
                e.Importance,
                e.ChangeKind
            },
            FormDescriptor f => new
            {
                f.Id,
                f.Name,
                f.Version,
                f.State,
                f.SupersededById,
                Schema = new { f.Schema.Id, f.Schema.Version },
                Fields = f.Fields.Select(fd => new
                {
                    fd.SchemaFieldName,
                    fd.IsReadOnly,
                    fd.Order,
                    fd.Group,
                    fd.ControlType,        // NEW — changes interaction contract
                    fd.IsRequiredOverride,  // NEW — changes interaction contract
                    fd.OptionsSource        // NEW — changes interaction contract
                }).OrderBy(fd => fd.SchemaFieldName).ToArray()
            },
            HumanTaskDescriptor h => new
            {
                h.Id,
                h.Name,
                h.Version,
                h.State,
                h.SupersededById,
                Interaction = new { h.Interaction.Id, h.Interaction.Version },
                InputSchema = h.InputSchema == null ? null : new { h.InputSchema.Value.Id, h.InputSchema.Value.Version },
                OutputSchema = h.OutputSchema == null ? null : new { h.OutputSchema.Value.Id, h.OutputSchema.Value.Version },
                h.AssigneeStrategy,
                h.Permissions,
                Outcomes = h.Outcomes.Select(o => new
                {
                    o.Condition,
                    Capability = o.Capability == null ? null : new { o.Capability.Value.Id, o.Capability.Value.Version }
                }).OrderBy(o => o.Condition.ToString()).ToArray()
            },
            WorkflowDescriptor w => new
            {
                w.Id,
                w.Name,
                w.Version,
                w.State,
                w.SupersededById,
                VariableSchema = w.VariableSchema == null ? null : new { w.VariableSchema.Value.Id, w.VariableSchema.Value.Version },
                w.DefaultVariableScope,
                Steps = w.Steps.Select(s => new
                {
                    s.Id,
                    TargetKind = s.Target.GetType().Name,
                    s.OnError,
                    Transitions = s.Transitions.OrderBy(t => t).ToArray()
                }).OrderBy(s => s.Id).ToArray()
            },
            _ => descriptor
        };
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
