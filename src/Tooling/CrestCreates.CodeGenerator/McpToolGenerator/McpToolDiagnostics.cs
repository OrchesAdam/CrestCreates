using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.McpToolGenerator;

internal static class McpToolDiagnostics
{
    internal static readonly DiagnosticDescriptor InvalidContainer = Create("MCP010", "Invalid MCP tool container");
    internal static readonly DiagnosticDescriptor InvalidSpec = Create("MCP011", "Invalid MCP tool spec");
    internal static readonly DiagnosticDescriptor NegativeCapabilityVersion = Create("MCP012", "CapabilityVersion cannot be negative");
    internal static readonly DiagnosticDescriptor InvalidToolName = Create("MCP002", "Invalid MCP ToolName");
    internal static readonly DiagnosticDescriptor EmptyDescription = Create("MCP004", "MCP tool description is required");
    internal static readonly DiagnosticDescriptor InvalidDescriptorVersion = Create("MCP005", "DescriptorVersion must be positive");
    internal static readonly DiagnosticDescriptor UnsupportedType = Create("MCP006", "Unsupported MCP input or output type");
    internal static readonly DiagnosticDescriptor DuplicateIdentity = Create("MCP018", "Duplicate MCP descriptor identity or ToolName");

    private static DiagnosticDescriptor Create(string id, string title)
        => new(id, title, title, "MCP", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
