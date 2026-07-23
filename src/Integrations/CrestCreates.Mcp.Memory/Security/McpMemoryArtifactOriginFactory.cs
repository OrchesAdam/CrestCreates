using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Capability.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Mcp.Memory.Security;

internal sealed record McpInvocationBindingComponents
{
    [JsonPropertyName("tenantId")] public required string TenantId { get; init; }
    [JsonPropertyName("userId")] public required string UserId { get; init; }
    [JsonPropertyName("hostId")] public required string HostId { get; init; }
    [JsonPropertyName("sessionId")] public required string SessionId { get; init; }
    [JsonPropertyName("invocationId")] public required string InvocationId { get; init; }
    [JsonPropertyName("requestId")] public required string RequestId { get; init; }
    [JsonPropertyName("toolDescriptorId")] public required string ToolDescriptorId { get; init; }
    [JsonPropertyName("toolDescriptorVersion")] public required int ToolDescriptorVersion { get; init; }
    [JsonPropertyName("capabilityId")] public required string CapabilityId { get; init; }
    [JsonPropertyName("capabilityVersion")] public required int CapabilityVersion { get; init; }
}

internal sealed record McpSessionBindingComponents
{
    [JsonPropertyName("tenantId")] public required string TenantId { get; init; }
    [JsonPropertyName("userId")] public required string UserId { get; init; }
    [JsonPropertyName("hostId")] public required string HostId { get; init; }
    [JsonPropertyName("securityContextId")] public required string SecurityContextId { get; init; }
    [JsonPropertyName("sessionOperationId")] public required string SessionOperationId { get; init; }
}

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
        var components = new McpInvocationBindingComponents
        {
            TenantId = RequireIdentity(context.TenantId, nameof(context.TenantId)),
            UserId = RequireIdentity(context.UserId, nameof(context.UserId)),
            HostId = RequireItem(context, McpCapabilityContextItemNames.HostId),
            SessionId = RequireItem(context, McpCapabilityContextItemNames.SessionId),
            InvocationId = RequireItem(context, McpCapabilityContextItemNames.InvocationId),
            RequestId = RequireItem(context, McpCapabilityContextItemNames.RequestId),
            ToolDescriptorId = RequireItem(context, McpCapabilityContextItemNames.ToolDescriptorId),
            ToolDescriptorVersion = RequirePositiveVersion(GetItem(context, McpCapabilityContextItemNames.ToolDescriptorVersion),
                nameof(McpCapabilityContextItemNames.ToolDescriptorVersion)),
            CapabilityId = RequireItem(context, McpCapabilityContextItemNames.CapabilityId),
            CapabilityVersion = RequirePositiveVersion(GetItem(context, McpCapabilityContextItemNames.CapabilityVersion),
                nameof(McpCapabilityContextItemNames.CapabilityVersion)),
        };
        var json = JsonSerializer.Serialize(components, McpMemoryBindingJsonContext.Default.McpInvocationBindingComponents);
        var raw = System.Text.Encoding.UTF8.GetBytes(json);
        return new CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash
        {
            Value = Convert.ToHexString(sha256.ComputeHash(raw)).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "mcp-invocation-binding",
            Scope = "TenantVisible",
            Purpose = "SourceBinding",
            ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "mcp-invocation-binding-v2",
        };
    }

    private static CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash ComputeSessionBindingHash(
        AgentMemoryAccessPrincipal principal, string sessionOperationId)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var components = new McpSessionBindingComponents
        {
            TenantId = principal.TenantId,
            UserId = principal.UserId,
            HostId = principal.CallerId,
            SecurityContextId = principal.SecurityContextId,
            SessionOperationId = sessionOperationId,
        };
        var json = JsonSerializer.Serialize(components, McpMemoryBindingJsonContext.Default.McpSessionBindingComponents);
        var raw = System.Text.Encoding.UTF8.GetBytes(json);
        return new CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash
        {
            Value = Convert.ToHexString(sha256.ComputeHash(raw)).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "mcp-session-binding",
            Scope = "TenantVisible",
            Purpose = "SourceBinding",
            ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "mcp-session-binding-v2",
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
