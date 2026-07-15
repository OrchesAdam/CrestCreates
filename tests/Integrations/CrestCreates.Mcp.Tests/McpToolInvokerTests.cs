using System.Collections.Frozen;
using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolInvokerTests
{
    [Fact]
    public async Task Absent_arguments_dispatches_captured_descriptor_as_mcp_and_propagates_context()
    {
        var entry = Entry();
        var dispatcher = new CapturingDispatcher(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var invoker = Invoker(entry, dispatcher, McpToolExposureDecision.Allow);
        var call = new McpToolCallContext(
            new McpToolHostContext("host", "test"),
            "logical-call",
            "request-1",
            "session-1");

        var result = await invoker.InvokeAsync(entry.Descriptor.ToolName, null, call);

        result.IsError.Should().BeFalse();
        dispatcher.Descriptor.Should().BeSameAs(entry.Capability);
        dispatcher.Source.Should().Be(InvocationSource.Mcp);
        dispatcher.Context!.CausationId.Should().Be("request-1");
        dispatcher.Context.InputJson!.Value.GetRawText().Should().Be("{}");
        dispatcher.Context.Items[McpCapabilityContextItemNames.HostId].Should().Be("host");
        dispatcher.Context.IdempotencyKey.Should().StartWith("mcp:v1:");
    }

    [Fact]
    public async Task Denied_tool_is_indistinguishable_from_unknown_and_never_dispatches()
    {
        var entry = Entry();
        var dispatcher = new CapturingDispatcher(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var invoker = Invoker(entry, dispatcher, McpToolExposureDecision.Deny);

        var action = async () => await invoker.InvokeAsync(
            entry.Descriptor.ToolName,
            null,
            Call());

        var exception = await action.Should().ThrowAsync<McpToolProtocolException>();
        exception.Which.FailureKind.Should().Be(McpToolProtocolFailureKind.UnknownTool);
        dispatcher.Descriptor.Should().BeNull();
    }

    [Fact]
    public async Task Non_object_arguments_are_protocol_error()
    {
        var entry = Entry();
        var invoker = Invoker(entry, new CapturingDispatcher(CapabilityExecutionResult.Success(null, TimeSpan.Zero)), McpToolExposureDecision.Allow);
        using var json = JsonDocument.Parse("[]");

        var action = async () => await invoker.InvokeAsync(entry.Descriptor.ToolName, json.RootElement, Call());

        var exception = await action.Should().ThrowAsync<McpToolProtocolException>();
        exception.Which.FailureKind.Should().Be(McpToolProtocolFailureKind.InvalidRequest);
    }

    [Fact]
    public async Task Duplicate_arguments_are_tool_input_errors()
    {
        var entry = Entry();
        var dispatcher = new CapturingDispatcher(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var invoker = Invoker(entry, dispatcher, McpToolExposureDecision.Allow);
        using var json = JsonDocument.Parse("{\"name\":\"a\",\"name\":\"b\"}");

        var result = await invoker.InvokeAsync(entry.Descriptor.ToolName, json.RootElement, Call());

        result.IsError.Should().BeTrue();
        result.ErrorCode.Should().Be("DUPLICATE_ARGUMENT");
        dispatcher.Descriptor.Should().BeNull();
    }

    [Fact]
    public async Task Capability_failure_maps_to_safe_tool_error_without_structured_content()
    {
        var entry = Entry();
        var invoker = Invoker(
            entry,
            new CapturingDispatcher(CapabilityExecutionResult.Failure("UNAUTHORIZED", "internal detail", TimeSpan.Zero)),
            McpToolExposureDecision.Allow);

        var result = await invoker.InvokeAsync(entry.Descriptor.ToolName, null, Call());

        result.IsError.Should().BeTrue();
        result.ErrorCode.Should().Be("UNAUTHORIZED");
        result.StructuredContent.Should().BeNull();
        ((McpToolTextContent)result.Content.Single()).Text.Should().NotContain("internal detail");
    }

    [Fact]
    public async Task Validation_issues_are_projected_as_safe_field_hints()
    {
        var entry = Entry();
        var invoker = Invoker(
            entry,
            new CapturingDispatcher(CapabilityExecutionResult.Failure(
                "CAPABILITY_VALIDATION_FAILED",
                "internal validation details",
                TimeSpan.Zero,
                [new CapabilityExecutionIssue(SchemaValidationErrorCodes.FieldRequired.ToString(), "Name is required internally.", "name")])),
            McpToolExposureDecision.Allow);

        var result = await invoker.InvokeAsync(entry.Descriptor.ToolName, null, Call());

        ((McpToolTextContent)result.Content.Single()).Text.Should().Be("Field 'name': required.");
    }

    [Fact]
    public async Task Invalid_actual_output_schema_is_internal_contract_violation()
    {
        var outputSchema = new SchemaDescriptor
        {
            Id = "output",
            Name = "Output",
            Version = 1,
            Fields = [new SchemaFieldDescriptor { Name = "name", FieldType = "string", IsRequired = true }]
        };
        var entry = Entry(outputSchema, (_, _, _) =>
        {
            using var document = JsonDocument.Parse("{\"other\":\"value\"}");
            return ValueTask.FromResult<JsonElement?>(document.RootElement.Clone());
        });
        var invoker = Invoker(
            entry,
            new CapturingDispatcher(CapabilityExecutionResult.Success(new object(), TimeSpan.Zero)),
            McpToolExposureDecision.Allow);

        var action = async () => await invoker.InvokeAsync(entry.Descriptor.ToolName, null, Call());

        var exception = await action.Should().ThrowAsync<McpToolProtocolException>();
        exception.Which.InternalCode.Should().Be("MCP_TOOL_OUTPUT_SCHEMA_VIOLATION");
        exception.Which.FailureKind.Should().Be(McpToolProtocolFailureKind.InternalServer);
    }

    private static McpToolInvoker Invoker(
        McpToolRuntimeEntry entry,
        CapturingDispatcher dispatcher,
        McpToolExposureDecision exposure)
        => new(
            new McpToolRuntimeSnapshotProvider(new McpToolRuntimeSnapshot(new[] { entry }.ToFrozenDictionary(item => item.Descriptor.ToolName, StringComparer.Ordinal))),
            new FixedPolicy(exposure),
            dispatcher,
            new DefaultMcpIdempotencyKeyBuilder(),
            new SchemaValidator(),
            new McpToolResultMapper());

    private static McpToolCallContext Call() => new(
        new McpToolHostContext("host", "test"),
        "logical-call",
        "request");

    private static McpToolRuntimeEntry Entry(
        SchemaDescriptor? outputSchema = null,
        Func<object?, System.Text.Json.Serialization.Metadata.JsonTypeInfo?, CancellationToken, ValueTask<JsonElement?>>? serialize = null)
    {
        using var inputSchemaJson = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:orders.get",
            Name = "Get order",
            Version = 1,
            Capability = new McpCapabilityReference("orders.get", 1),
            ToolName = "orders.get",
            Description = "Gets order."
        };
        var capability = new CapabilityDescriptor { Id = "orders.get", Name = "Get order", Version = 1 };
        var binding = new McpToolBindingContract
        {
            ToolDescriptorId = descriptor.Id,
            ToolDescriptorVersion = 1,
            OutputType = outputSchema is null ? null : typeof(object),
            BindInputAsync = (json, info, ct) => ValueTask.FromResult<object?>(null),
            SerializeOutputAsync = serialize ?? ((output, info, ct) => ValueTask.FromResult<JsonElement?>(null))
        };
        return new McpToolRuntimeEntry(
            descriptor,
            capability,
            null,
            outputSchema,
            new McpToolRuntimeBinding(binding, null, null),
            new McpToolContract("orders.get", null, "Gets order.", inputSchemaJson.RootElement.Clone(), null, new McpToolAnnotations(true, null, null, null)),
            "tool-hash",
            "capability-hash",
            null,
            outputSchema is null ? null : "output-hash");
    }

    private sealed class FixedPolicy(McpToolExposureDecision decision) : IMcpToolExposurePolicy
    {
        public ValueTask<McpToolExposureDecision> EvaluateAsync(McpToolExposureContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(decision);
    }

    private sealed class CapturingDispatcher(CapabilityExecutionResult result) : ICapabilityDispatcher
    {
        public CapabilityDescriptor? Descriptor { get; private set; }
        public InvocationSource Source { get; private set; }
        public CapabilityExecutionContext? Context { get; private set; }

        public Task<CapabilityExecutionResult> DispatchAsync(
            CapabilityDescriptor descriptor,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            Descriptor = descriptor;
            Source = source;
            Context = new CapabilityExecutionContext { ServiceProvider = null!, Input = input };
            configureContext?.Invoke(Context);
            return Task.FromResult(result);
        }

        public Task<CapabilityExecutionResult> DispatchAsync(
            string capabilityId,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("String dispatcher overload must not be used.");
    }
}
