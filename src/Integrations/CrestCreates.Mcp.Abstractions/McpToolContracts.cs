using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Mcp;

public sealed record McpToolHostContext(
    string HostId,
    string EnvironmentName,
    string? ProfileName = null);

public sealed record McpToolDiscoveryContext(McpToolHostContext Host);

public sealed record McpToolCallContext(
    McpToolHostContext Host,
    string InvocationId,
    string RequestId,
    string? SessionId = null);

public sealed record McpToolAnnotations(
    bool ReadOnlyHint,
    bool? DestructiveHint,
    bool? IdempotentHint,
    bool? OpenWorldHint);

public sealed record McpToolContract(
    string Name,
    string? Title,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    McpToolAnnotations Annotations);

public abstract record McpToolContent;

public sealed record McpToolTextContent(string Text) : McpToolContent;

public sealed record McpToolInvocationOutcome(
    bool IsError,
    IReadOnlyList<McpToolContent> Content,
    JsonElement? StructuredContent = null,
    string? ErrorCode = null);

public interface IMcpToolDiscoveryService
{
    ValueTask<IReadOnlyList<McpToolContract>> ListAsync(
        McpToolDiscoveryContext context,
        CancellationToken cancellationToken = default);
}

public interface IMcpToolInvoker
{
    ValueTask<McpToolInvocationOutcome> InvokeAsync(
        string toolName,
        JsonElement? arguments,
        McpToolCallContext context,
        CancellationToken cancellationToken = default);
}

public enum McpToolProtocolFailureKind
{
    UnknownTool = 0,
    InvalidRequest = 1,
    InternalServer = 2
}

public class McpToolProtocolException : Exception
{
    protected McpToolProtocolException(
        McpToolProtocolFailureKind failureKind,
        string internalCode,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        FailureKind = failureKind;
        InternalCode = internalCode;
    }

    public McpToolProtocolFailureKind FailureKind { get; }

    public string InternalCode { get; }
}

public sealed class McpToolBindingContract
{
    public required string ToolDescriptorId { get; init; }

    public int ToolDescriptorVersion { get; init; }

    public Type? InputType { get; init; }

    public Type? OutputType { get; init; }

    public required Func<JsonElement, JsonTypeInfo?, CancellationToken, ValueTask<object?>> BindInputAsync { get; init; }

    public required Func<object?, JsonTypeInfo?, CancellationToken, ValueTask<JsonElement?>> SerializeOutputAsync { get; init; }
}
