using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityDispatcherTests
{
    private sealed class TestCapabilityProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestCapabilityProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    [Fact]
    public async Task DispatchAsync_ByDescriptor_SetsInvocationSource()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "test.echo", Name = "Test Echo", Version = 1,
            CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active
        };

        InvocationSource? capturedSource = null;
        var pipelineMock = new Mock<ICapabilityPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, Action<CapabilityExecutionContext>?, CancellationToken>(
                (name, input, configure, ct) =>
                {
                    var ctx = new CapabilityExecutionContext();
                    configure?.Invoke(ctx);
                    capturedSource = ctx.InvocationSource;
                })
            .ReturnsAsync(CapabilityExecutionResult.Success(null, TimeSpan.Zero));

        var dispatcher = new CapabilityDispatcher(
            resolver: null!,
            pipeline: pipelineMock.Object);

        await dispatcher.DispatchAsync(descriptor, InvocationSource.Workflow);

        capturedSource.Should().Be(InvocationSource.Workflow);
    }

    [Fact]
    public async Task DispatchAsync_ByDescriptor_PopulatesTenantIdFromContext()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "test.echo", Name = "Test Echo", Version = 1,
            CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active
        };

        string? capturedTenantId = null;
        var pipelineMock = new Mock<ICapabilityPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, Action<CapabilityExecutionContext>?, CancellationToken>(
                (name, input, configure, ct) =>
                {
                    var ctx = new CapabilityExecutionContext();
                    configure?.Invoke(ctx);
                    capturedTenantId = ctx.TenantId;
                })
            .ReturnsAsync(CapabilityExecutionResult.Success(null, TimeSpan.Zero));

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.CurrentTenantId).Returns("tenant_42");

        var dispatcher = new CapabilityDispatcher(
            resolver: null!,
            pipeline: pipelineMock.Object,
            tenantContext: tenantContextMock.Object);

        await dispatcher.DispatchAsync(descriptor, InvocationSource.Http);

        capturedTenantId.Should().Be("tenant_42");
    }

    [Fact]
    public async Task DispatchAsync_ByDescriptor_PopulatesUserIdFromCurrentUser()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "test.echo", Name = "Test Echo", Version = 1,
            CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active
        };

        string? capturedUserId = null;
        var pipelineMock = new Mock<ICapabilityPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, Action<CapabilityExecutionContext>?, CancellationToken>(
                (name, input, configure, ct) =>
                {
                    var ctx = new CapabilityExecutionContext();
                    configure?.Invoke(ctx);
                    capturedUserId = ctx.UserId;
                })
            .ReturnsAsync(CapabilityExecutionResult.Success(null, TimeSpan.Zero));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.Id).Returns("user_77");

        var dispatcher = new CapabilityDispatcher(
            resolver: null!,
            pipeline: pipelineMock.Object,
            currentUser: currentUserMock.Object);

        await dispatcher.DispatchAsync(descriptor, InvocationSource.Agent);

        capturedUserId.Should().Be("user_77");
    }

    [Fact]
    public async Task DispatchAsync_ByString_ResolvesAndDispatches()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "test.echo", Name = "Test Echo", Version = 1,
            CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active
        };

        var resolverMock = new Mock<ICapabilityResolver>();
        resolverMock.Setup(r => r.Resolve(It.IsAny<CapabilityRef>())).Returns(descriptor);

        string? calledWithName = null;
        var pipelineMock = new Mock<ICapabilityPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, Action<CapabilityExecutionContext>?, CancellationToken>(
                (name, input, configure, ct) => { calledWithName = name; })
            .ReturnsAsync(CapabilityExecutionResult.Success(null, TimeSpan.Zero));

        var dispatcher = new CapabilityDispatcher(
            resolver: resolverMock.Object,
            pipeline: pipelineMock.Object);

        var result = await dispatcher.DispatchAsync("test.echo", InvocationSource.Internal);

        calledWithName.Should().Be("test.echo");
        result.IsSuccess.Should().BeTrue();
    }
}
