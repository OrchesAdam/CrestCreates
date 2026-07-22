using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

/// <summary>
/// Resolves the transitive closure of referenced schemas for a root schema.
/// Walks schema.References and schema.Fields[].ObjectSchema transitively.
/// Detects circular references via visited set.
/// </summary>
public sealed class McpToolSchemaClosureResolver
{
    private readonly ISchemaRegistry _schemas;

    public McpToolSchemaClosureResolver(ISchemaRegistry schemas)
    {
        _schemas = schemas;
    }

    /// <summary>
    /// Resolve the full closure of referenced schemas for a root schema.
    /// Returns all transitively referenced schemas (not including the root itself).
    /// </summary>
    public IReadOnlyList<SchemaDescriptor> Resolve(SchemaDescriptor? root)
    {
        if (root is null) return Array.Empty<SchemaDescriptor>();

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SchemaDescriptor>();
        Walk(root, visited, result);
        return result;
    }

    private void Walk(SchemaDescriptor schema, HashSet<string> visited, List<SchemaDescriptor> result)
    {
        // Walk top-level References
        if (schema.References is { Count: > 0 })
        {
            foreach (var reference in schema.References)
            {
                var key = $"{reference.Id}:{reference.Version}";
                if (!visited.Add(key)) continue; // Already visited or circular

                var resolved = _schemas.GetByVersion(reference.Id, reference.Version);
                if (resolved is not null)
                {
                    result.Add(resolved);
                    Walk(resolved, visited, result);
                }
            }
        }

        // Walk Fields[].ObjectSchema (nested object references)
        if (schema.Fields is { Count: > 0 })
        {
            foreach (var field in schema.Fields)
            {
                if (field.ObjectSchema is not null)
                {
                    var key = $"{field.ObjectSchema.Value.Id}:{field.ObjectSchema.Value.Version}";
                    if (!visited.Add(key)) continue;

                    var resolved = _schemas.GetByVersion(
                        field.ObjectSchema.Value.Id,
                        field.ObjectSchema.Value.Version);
                    if (resolved is not null)
                    {
                        result.Add(resolved);
                        Walk(resolved, visited, result);
                    }
                }
            }
        }
    }
}
