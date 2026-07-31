using System.Collections.Immutable;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolInvoker : IMcpToolInvoker
{
    private readonly McpToolRuntimeSnapshotProvider _snapshotProvider;
    private readonly IMcpToolExposurePolicy _exposurePolicy;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly IMcpIdempotencyKeyBuilder _idempotencyKeys;
    private readonly ISchemaValidator _schemaValidator;
    private readonly McpToolResultMapper _results;

    public McpToolInvoker(
        McpToolRuntimeSnapshotProvider snapshotProvider,
        IMcpToolExposurePolicy exposurePolicy,
        ICapabilityDispatcher dispatcher,
        IMcpIdempotencyKeyBuilder idempotencyKeys,
        ISchemaValidator schemaValidator,
        McpToolResultMapper results)
    {
        _snapshotProvider = snapshotProvider;
        _exposurePolicy = exposurePolicy;
        _dispatcher = dispatcher;
        _idempotencyKeys = idempotencyKeys;
        _schemaValidator = schemaValidator;
        _results = results;
    }

    public async ValueTask<McpToolInvocationOutcome> InvokeAsync(
        string toolName,
        JsonElement? arguments,
        McpToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
            throw new McpInvalidRequestException("MCP_INVALID_CALL_CONTEXT", "MCP call context is required.");
        if (string.IsNullOrWhiteSpace(toolName))
            throw new McpInvalidRequestException("MCP_INVALID_TOOL_NAME", "Tool name is required.");
        McpToolDiscoveryService.ValidateHost(context.Host);
        if (string.IsNullOrWhiteSpace(context.InvocationId) || string.IsNullOrWhiteSpace(context.RequestId))
            throw new McpInvalidRequestException("MCP_INVALID_CALL_CONTEXT", "Invalid call context.");

        var entry = _snapshotProvider.GetRequired().Find(toolName) ?? throw new McpUnknownToolException();
        McpToolExposureDecision exposure;
        try
        {
            exposure = await _exposurePolicy.EvaluateAsync(
                new McpToolExposureContext(
                    context.Host,
                    entry.Descriptor,
                    entry.Capability,
                    McpToolExposurePhase.Invocation),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new McpToolContractViolationException(
                "MCP_TOOL_EXPOSURE_POLICY_FAILURE",
                "The server could not evaluate tool exposure.",
                exception);
        }
        if (!exposure.IsAllowed)
            throw new McpUnknownToolException();

        var normalized = NormalizeArguments(arguments);
        if (HasDuplicateProperties(normalized))
            return _results.MapInputError("DUPLICATE_ARGUMENT", "Tool arguments contain duplicate properties.");
        if (entry.InputSchema is not null
            && TryFindUnknownArgument(entry.InputSchema, normalized, out var unknownArgument))
            return _results.MapInputError(
                "UNKNOWN_ARGUMENT",
                $"Tool arguments contain an unknown property '{unknownArgument}'.");
        if (entry.Binding.Contract.InputType is null && normalized.EnumerateObject().Any())
            return _results.MapInputError("INVALID_ARGUMENTS", "This tool does not accept arguments.");

        object? input;
        try
        {
            input = await entry.Binding.Contract.BindInputAsync(
                normalized,
                entry.Binding.InputTypeInfo,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return _results.MapInputError("INVALID_ARGUMENTS", "Tool arguments are invalid.");
        }

        var execution = await _dispatcher.DispatchAsync(
            entry.Capability,
            InvocationSource.Mcp,
            input,
            executionContext => ConfigureExecutionContext(executionContext, entry, context, normalized),
            cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess)
            return _results.MapFailure(execution);

        return await MapSuccessAsync(entry, execution.Output, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<McpToolInvocationOutcome> MapSuccessAsync(
        McpToolRuntimeEntry entry,
        object? output,
        CancellationToken cancellationToken)
    {
        if (entry.OutputSchema is null)
        {
            if (output is not null)
                throw new McpToolContractViolationException(
                    "MCP_TOOL_UNEXPECTED_OUTPUT",
                    "The tool produced an invalid server result.");
            return _results.MapVoidSuccess();
        }
        if (output is null)
            throw new McpToolContractViolationException(
                "MCP_TOOL_MISSING_OUTPUT",
                "The tool produced an invalid server result.");

        JsonElement? serialized;
        try
        {
            serialized = await entry.Binding.Contract.SerializeOutputAsync(
                output,
                entry.Binding.OutputTypeInfo,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or NotSupportedException)
        {
            throw new McpToolContractViolationException(
                "MCP_TOOL_OUTPUT_TYPE_MISMATCH",
                "The tool produced an invalid server result.",
                exception);
        }
        if (!serialized.HasValue)
            throw new McpToolContractViolationException(
                "MCP_TOOL_MISSING_OUTPUT",
                "The tool produced an invalid server result.");

        var validation = _schemaValidator.Validate(entry.OutputSchema, serialized.Value, entry.OutputSchemaClosure ?? Array.Empty<SchemaDescriptor>(), rejectUnknownProperties: true);
        if (!validation.IsValid)
            throw new McpToolContractViolationException(
                "MCP_TOOL_OUTPUT_SCHEMA_VIOLATION",
                "The tool produced an invalid server result.");
        return _results.MapStructuredSuccess(serialized.Value);
    }

    private void ConfigureExecutionContext(
        CapabilityExecutionContext execution,
        McpToolRuntimeEntry entry,
        McpToolCallContext call,
        JsonElement arguments)
    {
        execution.CausationId = call.RequestId;
        execution.AccountabilityActor = new AuditActor { Kind = "unknown", Id = "unknown" };
        var references = ImmutableArray.CreateBuilder<AuditRuntimeReference>();
        references.Add(new AuditRuntimeReference("mcp-request", call.RequestId));
        references.Add(new AuditRuntimeReference("mcp-invocation", call.InvocationId));
        if (!string.IsNullOrWhiteSpace(call.SessionId))
            references.Add(new AuditRuntimeReference("mcp-session", call.SessionId));
        references.Add(new AuditRuntimeReference("mcp-host", call.Host.HostId));
        execution.AccountabilityRuntimeReferences = references.ToImmutable();
        execution.IdempotencyKey = _idempotencyKeys.Build(entry, call);
        execution.InputJson = arguments.Clone();
        execution.Items[McpCapabilityContextItemNames.ToolDescriptorId] = entry.Descriptor.Id;
        execution.Items[McpCapabilityContextItemNames.ToolDescriptorVersion] = entry.Descriptor.Version;
        execution.Items[McpCapabilityContextItemNames.ToolName] = entry.Descriptor.ToolName;
        execution.Items[McpCapabilityContextItemNames.RequestId] = call.RequestId;
        execution.Items[McpCapabilityContextItemNames.SessionId] = call.SessionId;
        execution.Items[McpCapabilityContextItemNames.HostId] = call.Host.HostId;
        execution.Items[McpCapabilityContextItemNames.InvocationId] = call.InvocationId;
        execution.Items[McpCapabilityContextItemNames.CapabilityId] = entry.Capability.Id;
        execution.Items[McpCapabilityContextItemNames.CapabilityVersion] = entry.Capability.Version;
    }

    private static JsonElement NormalizeArguments(JsonElement? arguments)
    {
        if (!arguments.HasValue)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
        if (arguments.Value.ValueKind != JsonValueKind.Object)
            throw new McpInvalidRequestException("MCP_ARGUMENTS_NOT_OBJECT", "Tool arguments must be an object.");
        return arguments.Value.Clone();
    }

    private static bool HasDuplicateProperties(JsonElement arguments)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return arguments.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool TryFindUnknownArgument(
        SchemaDescriptor schema,
        JsonElement arguments,
        out string? unknownArgument)
    {
        var fields = schema.Fields
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!fields.Contains(property.Name))
            {
                unknownArgument = property.Name;
                return true;
            }
        }

        unknownArgument = null;
        return false;
    }
}
