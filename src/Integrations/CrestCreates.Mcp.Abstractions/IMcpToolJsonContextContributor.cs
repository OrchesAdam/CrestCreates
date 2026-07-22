using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
    /// Contribute JsonTypeInfo entries to the builder and add source-generated
    /// JsonSerializerContext instances to the MCP JSON resolver chain.
    /// Each entry maps a binding key to a JsonTypeInfo for input/output resolution.
    /// </summary>
    void Contribute(McpJsonContextBuilder builder, JsonSerializerOptions options);
}
