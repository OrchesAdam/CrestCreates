using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Generated;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.Mcp;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Mcp.E2E.Tests;

public sealed class McpToolProjectionE2ETests
{
    [Fact]
    public async Task Generated_projection_enters_real_capability_pipeline_and_serializes_exact_types()
    {
        var inputSchema = Schema("e2e.input");
        var outputSchema = Schema("e2e.output");
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
        var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        DescriptorProviderRegistry.Register<SchemaDescriptor>(new Provider<SchemaDescriptor>([inputSchema, outputSchema]));
        DescriptorProviderRegistry.Register<CapabilityDescriptor>(new Provider<CapabilityDescriptor>([capability]));
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
        builder.Services.AddCapabilityRuntime();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
                GeneratedCapabilityHandlerModule.Instance));
        GeneratedHandlerRegistry.RegisterServices(builder.Services);
        builder.Services.AddAccountability();
        builder.Services.AddSingleton<InMemoryAuditSink>();
        builder.Services.AddSingleton<IAuditSink>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryAuditSink>());
        builder.Services.AddCrestMcpToolProjection(options =>
            options.SerializerOptions.TypeInfoResolver = E2EJsonContext.Default);
        using var host = builder.Build();
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();
        using var arguments = JsonDocument.Parse("{\"value\":\"hello\"}");

        var outcome = await invoker.InvokeAsync(
            "e2e.echo",
            arguments.RootElement,
            new McpToolCallContext(new McpToolHostContext("e2e", "test"), "logical-1", "request-1"));

        outcome.IsError.Should().BeFalse();
        outcome.StructuredContent!.Value.GetProperty("value").GetString().Should().Be("hello");
        EchoHandler.LastInput.Should().BeOfType<EchoInput>().Which.Value.Should().Be("hello");
        host.Services.GetRequiredService<McpToolRuntimeSnapshotProvider>()
            .GetRequired().Find("e2e.echo")!.Capability.Version.Should().Be(2);

        using var missingArguments = JsonDocument.Parse("{}");
        var validationOutcome = await invoker.InvokeAsync(
            "e2e.echo",
            missingArguments.RootElement,
            new McpToolCallContext(new McpToolHostContext("e2e", "test"), "logical-2", "request-2"));
        validationOutcome.IsError.Should().BeTrue();
        ((McpToolTextContent)validationOutcome.Content.Single()).Text.Should().Contain("Field 'value': required.");
        EchoHandler.InvocationCount.Should().Be(1);
        host.Services.GetRequiredService<InMemoryAuditSink>().GetRecords()
            .Should().HaveCount(2);
    }

    private static SchemaDescriptor Schema(string id) => new()
    {
        Id = id,
        Name = id,
        Version = 1,
        Fields = [new SchemaFieldDescriptor { Name = "value", FieldType = "string", IsRequired = true }]
    };

    private sealed class Provider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }

}

[CapabilityName("e2e.echo")]
internal sealed class EchoHandler : ICapabilityHandler<EchoInput, EchoOutput>
{
    public static EchoInput? LastInput { get; private set; }
    public static int InvocationCount { get; private set; }

    public Task<EchoOutput> ExecuteAsync(EchoInput input, CancellationToken ct)
    {
        LastInput = input;
        InvocationCount++;
        return Task.FromResult(new EchoOutput { Value = input.Value });
    }
}
