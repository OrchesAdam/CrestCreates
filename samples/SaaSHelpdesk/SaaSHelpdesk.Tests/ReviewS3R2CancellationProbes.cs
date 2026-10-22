using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Metadata;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

public sealed class ReviewS3R2CancellationProbes : BaseTest
{
    public ReviewS3R2CancellationProbes(Fixtures.HelpdeskWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData("kernel-deadline")]
    [InlineData("kernel-caller")]
    [InlineData("direct-caller")]
    [InlineData("third-token")]
    public async Task ReviewR2_real_kernel_cancellation_retains_its_source(string mode)
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        using var caller = new CancellationTokenSource();
        using var third = new CancellationTokenSource();
        var descriptor = new CapabilityDescriptor
        {
            Id = "review.r2.cancellation", Name = "ReviewR2Cancellation", Version = 1,
            CapabilityKind = CapabilityKind.Command, State = DescriptorState.Active,
        };
        var invoker = new KernelCancellationInvoker(services, mode, caller, third);
        var resolver = new CapabilityHandlerResolver();
        resolver.Register(descriptor.Id, invoker);
        var pipeline = new CapabilityPipeline(services,
            new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([])),
            resolver, new CapabilityPipelineBuilder(),
            services.GetRequiredService<IDescriptorStableHashBuilder>(),
            services.GetRequiredService<ICapabilityExceptionClassifier>());

        var result = await pipeline.ExecuteAsync(descriptor, ct: caller.Token);

        invoker.Calls.Should().Be(1);
        invoker.Exception.Should().NotBeNull();
        invoker.Exception!.CancellationToken.IsCancellationRequested.Should().BeTrue();
        invoker.Context!.CancellationToken.Should().Be(caller.Token);
        result.IsSuccess.Should().BeFalse();
        services.GetRequiredService<IUnitOfWorkManager>().CurrentOrNull.Should().BeNull();
        if (mode.StartsWith("kernel-", StringComparison.Ordinal))
        {
            invoker.HadRealTransaction.Should().BeTrue("the real EF kernel must have entered a transaction");
            invoker.Exception.CancellationToken.Should().NotBe(caller.Token,
                "the kernel owns the actual execution token and combines the caller source");
        }
        caller.IsCancellationRequested.Should().Be(mode is "kernel-caller" or "direct-caller");
        var expected = mode switch
        {
            "kernel-deadline" => CapabilityFailureKind.ManagedDeadline,
            "kernel-caller" or "direct-caller" => CapabilityFailureKind.Cancelled,
            _ => CapabilityFailureKind.NonCaller,
        };
        result.FailureFacts.Should().NotBeNull();
        result.FailureFacts!.Kind.Should().Be(expected,
            $"source must survive formal execution; context K.CanBeCanceled={invoker.Context.ManagedDeadlineToken.CanBeCanceled}, "
            + $"L.CanBeCanceled={invoker.Context.ExecutionToken.CanBeCanceled}");
    }

    private sealed class KernelCancellationInvoker(
        IServiceProvider services, string mode, CancellationTokenSource caller, CancellationTokenSource third)
        : ICapabilityContextAwareHandlerInvoker
    {
        public int Calls { get; private set; }
        public bool HadRealTransaction { get; private set; }
        public OperationCanceledException? Exception { get; private set; }
        public CapabilityExecutionContext? Context { get; private set; }
        public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            Calls++;
            Context = context;
            try
            {
                if (mode.StartsWith("kernel-", StringComparison.Ordinal))
                {
                    return await services.GetRequiredService<IUnitOfWorkManager>().ExecuteAsync<object?>(async token =>
                    {
                        HadRealTransaction = services.GetRequiredService<HelpdeskDbContext>()
                            .Database.CurrentTransaction is not null;
                        if (mode == "kernel-caller") caller.CancelAfter(100);
                        await Task.Delay(Timeout.Infinite, token);
                        return null;
                    }, new UnitOfWorkOptions
                    {
                        Timeout = mode == "kernel-deadline" ? TimeSpan.FromMilliseconds(300) : TimeSpan.FromSeconds(10),
                    }, ct);
                }
                if (mode == "direct-caller")
                {
                    caller.CancelAfter(100);
                    await Task.Delay(Timeout.Infinite, ct);
                }
                else
                {
                    third.CancelAfter(100);
                    await Task.Delay(Timeout.Infinite, third.Token);
                }
                return null;
            }
            catch (OperationCanceledException ex)
            {
                Exception = ex;
                throw;
            }
        }
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException("The real pipeline must invoke the context-aware entry.");
    }
}
