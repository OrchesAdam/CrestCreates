using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Internal;
using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityEndToEndTests
{
    private sealed class TestProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    private static (CapabilityRegistry, ICapabilityPipeline, InMemoryCapabilityAuditStore, CapabilityHandlerResolver) CreateE2EPipeline(
        params CapabilityDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestProvider(descriptors.ToList())]);

        var auditStore = new InMemoryCapabilityAuditStore();
        var resolver = new CapabilityHandlerResolver();
        var builder = new CapabilityPipelineBuilder();
        builder.Use<AuditMiddleware>();

        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton<ICapabilityAuditStore>(auditStore);
        services.AddSingleton(builder);
        services.AddTransient<AuditMiddleware>();
        services.AddTransient<ILogger<AuditMiddleware>>(_ => NullLogger<AuditMiddleware>.Instance);
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        return (registry, sp.GetRequiredService<ICapabilityPipeline>(), auditStore, resolver);
    }

    [Fact]
    public async Task E2E_Execute_ReturnsSuccess_AndAuditRecorded()
    {
        var (_, pipeline, audit, resolver) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "test.echo", Name = "Echo", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        resolver.Register("test.echo", new EchoInvoker());

        var result = await pipeline.ExecuteAsync("test.echo", input: "hello",
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("ECHO: hello");
        var records = audit.GetRecords();
        records.Should().HaveCount(1);
        records[0].IsSuccess.Should().BeTrue();
        records[0].Duration.Should().BePositive();
        records[0].CorrelationId.Should().NotBeNullOrEmpty();
        records[0].ExecutionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task E2E_WithTenantAndUser_PopulatesAuditContext()
    {
        var (registry, pipeline, audit, resolver) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "test.echo", Name = "Echo", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        resolver.Register("test.echo", new EchoInvoker());

        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.CurrentTenantId).Returns("tenant_42");
        var userMock = new Mock<ICurrentUser>();
        userMock.Setup(u => u.Id).Returns("user_77");

        var dispatcher = new CapabilityDispatcher(
            new DefaultCapabilityResolver(new DefaultCapabilityVersionResolver(registry)),
            pipeline,
            tenantMock.Object,
            userMock.Object);

        await dispatcher.DispatchAsync("test.echo", InvocationSource.Workflow, input: "test");

        var records = audit.GetRecords();
        records[0].TenantId.Should().Be("tenant_42");
        records[0].UserId.Should().Be("user_77");
    }

    [Fact]
    public async Task E2E_CapabilityNotFound_ReturnsErrorCode()
    {
        var (_, pipeline, _, _) = CreateE2EPipeline();
        var result = await pipeline.ExecuteAsync("nonexistent");
        result.ErrorCode.Should().Be("CAPABILITY_NOT_FOUND");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task E2E_HandlerNotFound_ReturnsErrorCode()
    {
        var (_, pipeline, audit, _) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "missing.handler", Name = "Missing Handler", Version = 1,
                CapabilityKind = CapabilityKind.Command, State = DescriptorState.Active }
        );

        var result = await pipeline.ExecuteAsync("missing.handler");

        result.ErrorCode.Should().Be("HANDLER_NOT_FOUND");
        result.IsSuccess.Should().BeFalse();
        var records = audit.GetRecords();
        records.Should().HaveCount(1);
        records[0].IsSuccess.Should().BeFalse();
        records[0].ErrorCode.Should().Be("HANDLER_NOT_FOUND");
    }

    [Fact]
    public async Task E2E_HandlerThrows_RecordsUnhandledException()
    {
        var (_, pipeline, audit, resolver) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "throws.handler", Name = "Throws", Version = 1,
                CapabilityKind = CapabilityKind.Command, State = DescriptorState.Active }
        );
        resolver.Register("throws.handler", new ThrowingInvoker());

        var result = await pipeline.ExecuteAsync("throws.handler");

        result.ErrorCode.Should().Be("PIPELINE_ERROR");
        result.IsSuccess.Should().BeFalse();
        var records = audit.GetRecords();
        records.Should().HaveCount(1);
        records[0].ErrorCode.Should().Be("UNHANDLED_EXCEPTION");
    }

    [Fact]
    public async Task E2E_Cancelled_RecordsCancelledStatus()
    {
        var (_, pipeline, audit, resolver) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "slow.handler", Name = "Slow", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        resolver.Register("slow.handler", new SlowInvoker());
        var cts = new CancellationTokenSource();
        cts.CancelAfter(10);

        var result = await pipeline.ExecuteAsync("slow.handler", ct: cts.Token);

        result.Status.Should().Be(CapabilityExecutionStatus.TimedOut);
        var records = audit.GetRecords();
        records.Should().HaveCount(1);
        records[0].ErrorCode.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task E2E_IdDifferentFromName_PreservesBoth()
    {
        var (_, pipeline, audit, resolver) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "echo.v2", Name = "Echo Command", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        resolver.Register("echo.v2", new EchoInvoker());

        var result = await pipeline.ExecuteAsync("echo.v2");
        result.IsSuccess.Should().BeTrue();
        var records = audit.GetRecords();
        records[0].CapabilityId.Should().Be("echo.v2");
        records[0].CapabilityName.Should().Be("Echo Command");
    }

    [Fact]
    public async Task E2E_MultiVersion_ResolverReturnsActive()
    {
        var (registry, _, _, _) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "echo.v1", Name = "Echo", Version = 1, CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active },
            new CapabilityDescriptor { Id = "echo.v2", Name = "Echo", Version = 2, CapabilityKind = CapabilityKind.Query, State = DescriptorState.Deprecated }
        );
        var versionResolver = new DefaultCapabilityVersionResolver(registry);
        var resolved = versionResolver.Resolve(new CapabilityRef { Id = "echo.v1" });
        resolved.Version.Should().Be(1);
        resolved.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public async Task Legacy_NameLookup_BackwardCompatibility()
    {
        var (_, pipeline, _, resolver) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "echo.v2", Name = "Echo Command", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        resolver.Register("echo.v2", new EchoInvoker());
        var result = await pipeline.ExecuteAsync("Echo Command");
        result.IsSuccess.Should().BeTrue();
    }

    private sealed class EchoInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>($"ECHO: {input}");
    }

    private sealed class ThrowingInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException("Handler failure");
    }

    private sealed class SlowInvoker : ICapabilityHandlerInvoker
    {
        public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            await Task.Delay(5000, ct);
            return "done";
        }
    }
}
