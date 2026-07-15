using System.Text.Json;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Mcp.AotFixture;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

try
{
    var fields = new[] { new SchemaFieldDescriptor { Name = "value", FieldType = "string" } };
    var inputSchema = new SchemaDescriptor { Id = "fixture.input", Name = "Input", Version = 1, Fields = fields };
    var outputSchema = new SchemaDescriptor { Id = "fixture.output", Name = "Output", Version = 1, Fields = fields };
    var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
    DescriptorProviderRegistry.Register<SchemaDescriptor>(
        new Provider<SchemaDescriptor>([inputSchema, outputSchema]));

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
    DescriptorProviderRegistry.Register<CapabilityDescriptor>(
        new Provider<CapabilityDescriptor>([capability]));

    CapabilityHandlerResolverProvider.Register("fixture.echo", new FixtureHandlerInvoker());

    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton<ISchemaRegistry>(schemas);
    builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
    builder.Services.AddCapabilityRuntime();
    builder.Services.AddCrestMcpToolProjection(options =>
        options.SerializerOptions.TypeInfoResolver = McpFixtureJsonContext.Default);

    using var host = builder.Build();
    await host.StartAsync();
    using var scope = host.Services.CreateScope();
    var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

    using var arguments = JsonDocument.Parse("{\"value\":\"trimmed\"}");
    var outcome = await invoker.InvokeAsync(
        "fixture.echo",
        arguments.RootElement,
        new McpToolCallContext(new McpToolHostContext("fixture", "test"), "logical", "request"));

    if (outcome.IsError
        || outcome.StructuredContent?.GetProperty("value").GetString() != "trimmed"
        || FixtureHandlerInvoker.LastSource != InvocationSource.Mcp
        || FixtureHandlerInvoker.LastInputJson?.GetProperty("value").GetString() != "trimmed"
        || string.IsNullOrWhiteSpace(FixtureHandlerInvoker.LastIdempotencyKey))
        return 2;

    Console.WriteLine("MCP_NATIVEAOT_PIPELINE_OK");
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

internal sealed class FixtureHandlerInvoker : ICapabilityContextAwareHandlerInvoker
{
    public static InvocationSource LastSource { get; private set; }
    public static JsonElement? LastInputJson { get; private set; }
    public static string? LastIdempotencyKey { get; private set; }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException("The context-aware handler path is required.");

    public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        LastSource = context.InvocationSource;
        LastInputJson = context.InputJson;
        LastIdempotencyKey = context.IdempotencyKey;
        var typed = (FixtureInput)context.Input!;
        return Task.FromResult<object?>(new FixtureOutput { Value = typed.Value });
    }
}
