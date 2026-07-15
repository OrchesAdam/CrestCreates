using System.Text.Json;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
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
        CapabilityHandlerResolverProvider.Register("e2e.echo", new EchoHandlerInvoker());

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
        builder.Services.AddCapabilityRuntime();
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
        EchoHandlerInvoker.Input.Should().BeOfType<EchoInput>().Which.Value.Should().Be("hello");
        EchoHandlerInvoker.Source.Should().Be(InvocationSource.Mcp);
        EchoHandlerInvoker.InputJson!.Value.GetProperty("value").GetString().Should().Be("hello");
        EchoHandlerInvoker.IdempotencyKey.Should().StartWith("mcp:v1:");
        host.Services.GetRequiredService<McpToolRuntimeSnapshotProvider>()
            .GetRequired().Find("e2e.echo")!.Capability.Version.Should().Be(2);
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

    private sealed class EchoHandlerInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public static object? Input { get; private set; }
        public static InvocationSource Source { get; private set; }
        public static JsonElement? InputJson { get; private set; }
        public static string? IdempotencyKey { get; private set; }

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException("Context-aware handler path is required.");

        public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            Input = context.Input;
            Source = context.InvocationSource;
            InputJson = context.InputJson;
            IdempotencyKey = context.IdempotencyKey;
            return Task.FromResult<object?>(new EchoOutput { Value = ((EchoInput)context.Input!).Value });
        }
    }
}
