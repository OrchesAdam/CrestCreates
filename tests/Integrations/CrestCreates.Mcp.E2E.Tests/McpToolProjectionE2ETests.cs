using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.E2E.Tests;

public sealed class McpToolProjectionE2ETests
{
    [Fact]
    public async Task Generated_projection_discovers_binds_dispatches_and_serializes_exact_types()
    {
        var tools = new McpToolRegistry(new RegistryValidationEngine<McpToolDescriptor>(
            [new McpToolDescriptorValidator()]));
        tools.Build(DescriptorProviderRegistry.GetProviders<McpToolDescriptor>());

        var inputSchema = Schema("e2e.input");
        var outputSchema = Schema("e2e.output");
        var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemas.Build([new Provider<SchemaDescriptor>([inputSchema, outputSchema])]);

        var capability = new CapabilityDescriptor
        {
            Id = "e2e.echo",
            Name = "Echo",
            Version = 2,
            State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Command,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(inputSchema.Id, 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>(outputSchema.Id, 1)
        };
        var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        capabilities.Build([new Provider<CapabilityDescriptor>([capability])]);

        var snapshot = new McpToolRuntimeSnapshotBuilder(
            tools,
            capabilities,
            schemas,
            new McpJsonSchemaProjector(),
            new McpToolSchemaParityValidator(),
            new DefaultCanonicalHashComputer(),
            new McpJsonOptions
            {
                SerializerOptions = new JsonSerializerOptions { TypeInfoResolver = E2EJsonContext.Default }
            }).Build();
        var dispatcher = new EchoDispatcher();
        var invoker = new McpToolInvoker(
            snapshot,
            new DefaultMcpToolExposurePolicy(),
            dispatcher,
            new DefaultMcpIdempotencyKeyBuilder(),
            new SchemaValidator(),
            new McpToolResultMapper());
        using var arguments = JsonDocument.Parse("{\"value\":\"hello\"}");

        var outcome = await invoker.InvokeAsync(
            "e2e.echo",
            arguments.RootElement,
            new McpToolCallContext(new McpToolHostContext("e2e", "test"), "logical-1", "request-1"));

        outcome.IsError.Should().BeFalse();
        outcome.StructuredContent!.Value.GetProperty("value").GetString().Should().Be("hello");
        dispatcher.Input.Should().BeOfType<EchoInput>().Which.Value.Should().Be("hello");
        dispatcher.Source.Should().Be(InvocationSource.Mcp);
        snapshot.Find("e2e.echo")!.Capability.Version.Should().Be(2);
    }

    private static SchemaDescriptor Schema(string id) => new()
    {
        Id = id,
        Name = id,
        Version = 1,
        Fields = [new SchemaFieldDescriptor { Name = "value", FieldType = "string" }]
    };

    private sealed class Provider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }

    private sealed class EchoDispatcher : ICapabilityDispatcher
    {
        public object? Input { get; private set; }
        public InvocationSource Source { get; private set; }

        public Task<CapabilityExecutionResult> DispatchAsync(
            CapabilityDescriptor descriptor,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            Input = input;
            Source = source;
            var context = new CapabilityExecutionContext { ServiceProvider = null!, Input = input };
            configureContext?.Invoke(context);
            var typed = (EchoInput)input!;
            return Task.FromResult(CapabilityExecutionResult.Success(
                new EchoOutput { Value = typed.Value },
                TimeSpan.Zero));
        }

        public Task<CapabilityExecutionResult> DispatchAsync(
            string capabilityId,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("String overload is forbidden.");
    }
}
