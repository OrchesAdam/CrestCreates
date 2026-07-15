using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Mcp.AotFixture;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

try
{
    var fields = new[] { new SchemaFieldDescriptor { Name = "value", FieldType = "string" } };
    var inputSchema = new SchemaDescriptor { Id = "fixture.input", Name = "Input", Version = 1, Fields = fields };
    var outputSchema = new SchemaDescriptor { Id = "fixture.output", Name = "Output", Version = 1, Fields = fields };
    var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
    schemas.Build([new Provider<SchemaDescriptor>([inputSchema, outputSchema])]);

    var capability = new CapabilityDescriptor
    {
        Id = "fixture.echo",
        Name = "Echo",
        Version = 1,
        State = DescriptorState.Active,
        CapabilityKind = CapabilityKind.Command,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(inputSchema.Id, 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>(outputSchema.Id, 1)
    };
    var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
    capabilities.Build([new Provider<CapabilityDescriptor>([capability])]);

    var tools = new McpToolRegistry(new RegistryValidationEngine<McpToolDescriptor>([new McpToolDescriptorValidator()]));
    tools.Build(DescriptorProviderRegistry.GetProviders<McpToolDescriptor>());
    var snapshot = new McpToolRuntimeSnapshotBuilder(
        tools,
        capabilities,
        schemas,
        new McpJsonSchemaProjector(),
        new McpToolSchemaParityValidator(),
        new DefaultCanonicalHashComputer(),
        new McpJsonOptions
        {
            SerializerOptions = new JsonSerializerOptions { TypeInfoResolver = McpFixtureJsonContext.Default }
        }).Build();
    var invoker = new McpToolInvoker(
        new McpToolRuntimeSnapshotProvider(snapshot),
        new DefaultMcpToolExposurePolicy(),
        new FixtureDispatcher(),
        new DefaultMcpIdempotencyKeyBuilder(),
        new SchemaValidator(),
        new McpToolResultMapper());
    using var arguments = JsonDocument.Parse("{\"value\":\"trimmed\"}");
    var outcome = await invoker.InvokeAsync(
        "fixture.echo",
        arguments.RootElement,
        new McpToolCallContext(new McpToolHostContext("fixture", "test"), "logical", "request"));
    if (outcome.IsError || outcome.StructuredContent?.GetProperty("value").GetString() != "trimmed")
        return 2;
    Console.WriteLine("MCP_NATIVEAOT_OK");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

internal sealed class Provider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T>
    where T : IDescriptor
{
    public IReadOnlyList<T> GetDescriptors() => descriptors;
}

internal sealed class FixtureDispatcher : ICapabilityDispatcher
{
    public Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var context = new CapabilityExecutionContext { ServiceProvider = null!, Input = input };
        configureContext?.Invoke(context);
        var typed = (FixtureInput)input!;
        return Task.FromResult(CapabilityExecutionResult.Success(
            new FixtureOutput { Value = typed.Value },
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
