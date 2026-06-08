using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

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
                InputSchema = new { c.InputSchema.Id, c.InputSchema.Version },
                OutputSchema = new { c.OutputSchema.Id, c.OutputSchema.Version },
                c.Permission,
                c.RiskLevel,
                SemanticTags = c.SemanticTags.OrderBy(t => t).ToArray()
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
