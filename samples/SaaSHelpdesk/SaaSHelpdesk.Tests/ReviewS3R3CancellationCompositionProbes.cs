using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Core.Abstractions.Threading;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

public sealed class ReviewS3R3CancellationCompositionProbes : BaseTest
{
    public ReviewS3R3CancellationCompositionProbes(Fixtures.HelpdeskWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData("single-deadline")]
    [InlineData("nested-inner-deadline")]
    [InlineData("nested-outer-deadline")]
    [InlineData("nested-caller")]
    [InlineData("nested-third")]
    [InlineData("third-after-deadline")]
    [InlineData("operation-deadline")]
    [InlineData("operation-caller")]
    public async Task ReviewR3_formal_token_composition_preserves_origin(string mode)
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        using var caller = new CancellationTokenSource();
        using var third = new CancellationTokenSource();
        var descriptor = new CapabilityDescriptor
        {
            Id = "review.r3.composition", Name = "ReviewR3Composition", Version = 1,
            CapabilityKind = CapabilityKind.Command, State = DescriptorState.Active,
        };
        var invoker = new ComposedInvoker(services, mode, caller, third);
        var resolver = new CapabilityHandlerResolver();
        resolver.Register(descriptor.Id, invoker);
        var pipeline = new CapabilityPipeline(services,
            new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([])),
            resolver, new CapabilityPipelineBuilder(),
            services.GetRequiredService<IDescriptorStableHashBuilder>(),
            services.GetRequiredService<ICapabilityExceptionClassifier>());
        var result = await pipeline.ExecuteAsync(descriptor, ct: caller.Token);

        invoker.Calls.Should().Be(1);
        invoker.BodyCalls.Should().Be(1);
        invoker.HadRealTransaction.Should().BeTrue();
        invoker.FinalException.Should().BeSameAs(invoker.OriginalException,
            "the original OCE identity must survive both frames");
        invoker.FinalException.Should().NotBeNull();
        invoker.FinalException!.CancellationToken.IsCancellationRequested.Should().BeTrue();
        services.GetRequiredService<IUnitOfWorkManager>().CurrentOrNull.Should().BeNull();
        caller.IsCancellationRequested.Should().Be(mode.EndsWith("caller", StringComparison.Ordinal));
        if (mode.StartsWith("nested-", StringComparison.Ordinal))
        {
            invoker.ReusedOwner.Should().BeTrue("Required must join the same actual transaction");
            if (mode != "nested-third")
            {
                invoker.InnerSnapshot.Should().NotBeNull();
                invoker.InnerSnapshot!.ExecutionToken.Should().Be(invoker.OriginalException!.CancellationToken,
                    "the inner frame must initially attach the metadata for the actual exception token");
            }
        }
        if (mode.StartsWith("operation-", StringComparison.Ordinal))
            invoker.OperationToken.Should().NotBe(invoker.ScopeToken,
                "the production CombineWithCurrent helper must create the derived operation token");
        if (mode == "third-after-deadline")
        {
            invoker.ScopeToken.IsCancellationRequested.Should().BeTrue();
            invoker.OriginalException!.CancellationToken.Should().NotBe(invoker.ScopeToken);
        }
        var expected = mode.EndsWith("caller", StringComparison.Ordinal)
            ? CapabilityFailureKind.Cancelled
            : mode.Contains("third", StringComparison.Ordinal) ? CapabilityFailureKind.NonCaller : CapabilityFailureKind.ManagedDeadline;
        result.FailureFacts.Should().NotBeNull();
        result.FailureFacts!.Kind.Should().Be(expected,
            "the exception's source must survive formal composition; "
            + $"finalSnapshotMatchesException={invoker.FinalSnapshot?.ExecutionToken == invoker.OriginalException!.CancellationToken}");
    }

    private sealed class ComposedInvoker(
        IServiceProvider services, string mode, CancellationTokenSource caller, CancellationTokenSource third)
        : ICapabilityContextAwareHandlerInvoker
    {
        public int Calls { get; private set; }
        public int BodyCalls { get; private set; }
        public bool HadRealTransaction { get; private set; }
        public bool ReusedOwner { get; private set; }
        public CancellationToken ScopeToken { get; private set; }
        public CancellationToken OperationToken { get; private set; }
        public OperationCanceledException? OriginalException { get; private set; }
        public OperationCanceledException? FinalException { get; private set; }
        public UnitOfWorkCancellationSnapshot? InnerSnapshot { get; private set; }
        public UnitOfWorkCancellationSnapshot? FinalSnapshot { get; private set; }
        public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            Calls++;
            var manager = services.GetRequiredService<IUnitOfWorkManager>();
            var outerDeadline = mode is "single-deadline" or "nested-outer-deadline" or "operation-deadline" or "third-after-deadline";
            try
            {
                return await manager.ExecuteAsync<object?>(async outerToken =>
                {
                    var outerOwner = manager.CurrentOrNull;
                    if (mode.StartsWith("nested-", StringComparison.Ordinal))
                    {
                        try
                        {
                            return await manager.ExecuteAsync<object?>(async innerToken =>
                            {
                                ReusedOwner = ReferenceEquals(outerOwner, manager.CurrentOrNull);
                                return await WaitAsync(innerToken, ct);
                            }, new UnitOfWorkOptions
                            {
                                Timeout = mode == "nested-inner-deadline" ? TimeSpan.FromMilliseconds(200) : null,
                            }, outerToken);
                        }
                        catch (OperationCanceledException ex)
                        {
                            InnerSnapshot = ex.Data[UnitOfWorkCancellationSnapshot.ExceptionDataKey]
                                as UnitOfWorkCancellationSnapshot;
                            if (mode == "nested-outer-deadline") await Task.Delay(40);
                            throw;
                        }
                    }
                    return await WaitAsync(outerToken, ct);
                }, new UnitOfWorkOptions
                {
                    Timeout = outerDeadline ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromSeconds(10),
                }, ct);
            }
            catch (OperationCanceledException ex)
            {
                FinalException = ex;
                FinalSnapshot = ex.Data[UnitOfWorkCancellationSnapshot.ExceptionDataKey]
                    as UnitOfWorkCancellationSnapshot;
                throw;
            }
        }
        private async Task<object?> WaitAsync(CancellationToken execution, CancellationToken rootCaller)
        {
            BodyCalls++;
            HadRealTransaction = services.GetRequiredService<HelpdeskDbContext>().Database.CurrentTransaction is not null;
            ScopeToken = execution;
            try
            {
                if (mode.StartsWith("operation-", StringComparison.Ordinal))
                {
                    using var link = UnitOfWorkExecutionContext.CombineWithCurrent(rootCaller, out var effective);
                    OperationToken = effective;
                    if (mode.EndsWith("caller", StringComparison.Ordinal)) caller.CancelAfter(100);
                    await services.GetRequiredService<HelpdeskDbContext>().Database
                        .ExecuteSqlRawAsync("SELECT pg_sleep(10)", effective);
                }
                else if (mode is "nested-third" or "third-after-deadline")
                {
                    third.CancelAfter(mode == "third-after-deadline" ? 1000 : 100);
                    await Task.Delay(Timeout.Infinite, third.Token);
                }
                else
                {
                    if (mode.EndsWith("caller", StringComparison.Ordinal)) caller.CancelAfter(100);
                    await Task.Delay(Timeout.Infinite, execution);
                }
                return null;
            }
            catch (OperationCanceledException ex)
            {
                OriginalException = ex;
                throw;
            }
        }
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException("Context-aware entry is required.");
    }
}
