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

    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton<ISchemaRegistry>(schemas);
    builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
    builder.Services.AddScoped<FixtureEchoHandler>();
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
        || FixtureEchoHandler.LastInput?.Value != "trimmed")
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

[CapabilityName("fixture.echo")]
internal sealed class FixtureEchoHandler : ICapabilityHandler<FixtureInput, FixtureOutput>
{
    public Task<FixtureOutput> ExecuteAsync(FixtureInput input, CancellationToken ct)
    {
        LastInput = input;
        return Task.FromResult(new FixtureOutput { Value = input.Value });
    }

    public static FixtureInput? LastInput { get; private set; }
}
