using System.Text.Json.Serialization;

namespace CrestCreates.Mcp.Abstractions;

/// <summary>
/// Contributes JsonTypeInfo entries to the MCP tool JSON serializer context.
/// Each assembly with MCP tool DTOs registers one contributor.
/// </summary>
public interface IMcpToolJsonContextContributor
{
    /// <summary>
    /// Unique contributor identifier. Must be stable and unique across all contributors.
    /// Duplicate IDs cause startup failure.
    /// </summary>
    string ContributorId { get; }

    /// <summary>
    /// The set of root CLR types owned by this contributor.
    /// No two contributors may claim the same binding root type.
    /// </summary>
    IReadOnlySet<Type> BindingRootTypes { get; }

    /// <summary>
    /// Contribute JsonTypeInfo entries to the binding builder.
    /// Each entry maps a binding key to a JsonTypeInfo for input/output resolution.
    /// </summary>
    void Contribute(McpJsonContextBuilder builder);

    /// <summary>
    /// Creates a standalone source-generated JsonSerializerContext for this contributor.
    /// The context must NOT be created with the shared JsonSerializerOptions —
    /// doing so would overwrite the application's TypeInfoResolver. The caller
    /// is responsible for adding the returned context to the shared options' resolver chain.
    /// </summary>
    JsonSerializerContext CreateContext();
}
