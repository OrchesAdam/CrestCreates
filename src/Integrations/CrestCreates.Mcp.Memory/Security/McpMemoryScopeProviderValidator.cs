using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.Mcp.Memory.Security;

/// <summary>
/// Bootstrap validator that ensures the registered IAgentMemoryAccessScopeProvider
/// explicitly supports MCP callers. Fail-closed: unknown provider capability = reject.
/// </summary>
internal sealed class McpMemoryScopeProviderValidator : IBootstrapValidator
{
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;

    public int Order => 200;

    public McpMemoryScopeProviderValidator(IAgentMemoryAccessScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public ValidationReport Validate()
    {
        if (_scopeProvider is not IAgentMemoryAccessScopeProviderCapabilities capabilities)
        {
            return ValidationReport.FromIssues(
                new ValidationIssue(
                    SeverityLevel.Error,
                    "MCP Memory requires a scope provider that declares MCP support via IAgentMemoryAccessScopeProviderCapabilities. " +
                    "The registered provider does not implement this interface."));
        }

        if (!capabilities.Supports(AgentMemoryCallerKind.Mcp))
        {
            return ValidationReport.FromIssues(
                new ValidationIssue(
                    SeverityLevel.Error,
                    "MCP Memory requires a scope provider that supports AgentMemoryCallerKind.Mcp. " +
                    "The registered provider does not support MCP callers."));
        }

        return ValidationReport.Empty;
    }
}
