using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Mcp.Memory.Security;

/// <summary>
/// Centralized MCP Origin and Principal construction.
/// All fields are validated with RequireIdentity/RequirePositiveVersion.
/// Handlers never construct Origin/Principal directly.
/// </summary>
internal sealed class McpMemoryArtifactOriginFactory
{
    public AgentMemoryAccessPrincipal CreatePrincipal(CapabilityExecutionContext context)
    {
        return new AgentMemoryAccessPrincipal
        {
            TenantId = RequireIdentity(context.TenantId, nameof(context.TenantId)),
            UserId = RequireIdentity(context.UserId, nameof(context.UserId)),
            CallerKind = AgentMemoryCallerKind.Mcp,
            CallerId = RequireItem(context, McpCapabilityContextItemNames.HostId),
            SecurityContextId = RequireItem(context, McpCapabilityContextItemNames.SessionId),
        };
    }

    public AgentMemoryArtifactOrigin CreateInvocationOrigin(CapabilityExecutionContext context)
    {
        return new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.McpInvocation,
            OperationId = RequireItem(context, McpCapabilityContextItemNames.InvocationId),
            BindingHash = ComputeInvocationBindingHash(context),
        };
    }

    public AgentMemoryArtifactOrigin CreateSessionOperationOrigin(
        AgentMemoryAccessPrincipal principal, string sessionOperationId)
    {
        return new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.McpSessionOperation,
            OperationId = RequireIdentity(sessionOperationId, nameof(sessionOperationId)),
            BindingHash = ComputeSessionBindingHash(principal, sessionOperationId),
        };
    }

    private static CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash ComputeInvocationBindingHash(
        CapabilityExecutionContext context)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var components = new[]
        {
            RequireIdentity(context.TenantId, nameof(context.TenantId)),
            RequireIdentity(context.UserId, nameof(context.UserId)),
            RequireItem(context, McpCapabilityContextItemNames.HostId),
            RequireItem(context, McpCapabilityContextItemNames.SessionId),
            RequireItem(context, McpCapabilityContextItemNames.InvocationId),
            RequireItem(context, McpCapabilityContextItemNames.RequestId),
            RequireItem(context, McpCapabilityContextItemNames.ToolDescriptorId),
            RequirePositiveVersion(GetItem(context, McpCapabilityContextItemNames.ToolDescriptorVersion),
                nameof(McpCapabilityContextItemNames.ToolDescriptorVersion)).ToString(),
            RequireItem(context, McpCapabilityContextItemNames.CapabilityId),
            RequirePositiveVersion(GetItem(context, McpCapabilityContextItemNames.CapabilityVersion),
                nameof(McpCapabilityContextItemNames.CapabilityVersion)).ToString(),
        };
        var raw = System.Text.Encoding.UTF8.GetBytes($"mcp-binding|{string.Join('|', components)}");
        return new CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash
        {
            Value = Convert.ToHexString(sha256.ComputeHash(raw)).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "mcp-invocation-binding",
            Scope = "TenantVisible",
            Purpose = "SourceBinding",
            ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "mcp-invocation-binding-v1",
        };
    }

    private static CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash ComputeSessionBindingHash(
        AgentMemoryAccessPrincipal principal, string sessionOperationId)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var raw = System.Text.Encoding.UTF8.GetBytes(
            $"mcp-session|{principal.TenantId}|{principal.UserId}|{principal.CallerId}|{principal.SecurityContextId}|{sessionOperationId}");
        return new CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash
        {
            Value = Convert.ToHexString(sha256.ComputeHash(raw)).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "mcp-session-binding",
            Scope = "TenantVisible",
            Purpose = "SourceBinding",
            ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "mcp-session-binding-v1",
        };
    }

    private static string RequireItem(CapabilityExecutionContext context, string key)
    {
        context.Items.TryGetValue(key, out var value);
        return RequireIdentity(value as string, key);
    }

    private static object? GetItem(CapabilityExecutionContext context, string key)
    {
        context.Items.TryGetValue(key, out var value);
        return value;
    }

    private static string RequireIdentity(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Trusted identity field '{fieldName}' is required for MCP memory security.");
        return value;
    }

    private static int RequirePositiveVersion(object? value, string fieldName)
    {
        if (value is int intVal && intVal > 0) return intVal;
        throw new InvalidOperationException(
            $"Trusted identity field '{fieldName}' must be a positive integer.");
    }
}
