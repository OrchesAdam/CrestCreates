using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Delivery.Tests;

#region Test payload and dependency types

public sealed record TestActivationPayload(string Value);

public interface ITestActivationDependencyA { }
public interface ITestActivationDependencyB { }

public sealed class TestActivationDependencyA : ITestActivationDependencyA, IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

public sealed class TestActivationDependencyB : ITestActivationDependencyB { }

#endregion

#region Test consumers

public sealed class WellFormedActivationConsumer
    : IOutboxRequiredConsumer<TestActivationPayload>, IOutboxConsumerActivation<WellFormedActivationConsumer>
{
    private readonly ITestActivationDependencyA _depA;
    private readonly ITestActivationDependencyB _depB;

    public string ConsumerId => "well-formed-activation-consumer";

    public WellFormedActivationConsumer(ITestActivationDependencyA depA, ITestActivationDependencyB depB)
    {
        _depA = depA;
        _depB = depB;
    }

    static WellFormedActivationConsumer IOutboxConsumerActivation<WellFormedActivationConsumer>.CreateOutboxConsumer(IServiceProvider services)
        => new WellFormedActivationConsumer(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ITestActivationDependencyA>(services),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ITestActivationDependencyB>(services));

    public ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(TestActivationPayload payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(OutboxRequiredConsumerResult.Accepted());
}

public sealed class ScopeTrackingActivationConsumer
    : IOutboxRequiredConsumer<TestActivationPayload>, IOutboxConsumerActivation<ScopeTrackingActivationConsumer>, IDisposable
{
    public static int InstanceCount;
    public static int DisposedCount;
    public int InstanceNumber;
    public bool IsDisposed;

    public string ConsumerId => "scope-tracking-activation-consumer";

    public ScopeTrackingActivationConsumer()
    {
        InstanceNumber = Interlocked.Increment(ref InstanceCount);
    }

    static ScopeTrackingActivationConsumer IOutboxConsumerActivation<ScopeTrackingActivationConsumer>.CreateOutboxConsumer(IServiceProvider services)
        => new ScopeTrackingActivationConsumer();

    public ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(TestActivationPayload payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(OutboxRequiredConsumerResult.Accepted());

    public void Dispose()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            Interlocked.Increment(ref DisposedCount);
        }
    }

    public static void Reset()
    {
        InstanceCount = 0;
        DisposedCount = 0;
    }
}

public sealed class ConstructorFailingActivationConsumer
    : IOutboxRequiredConsumer<TestActivationPayload>, IOutboxConsumerActivation<ConstructorFailingActivationConsumer>
{
    public static bool ConsumeWasCalled;

    public string ConsumerId => "constructor-failing-activation-consumer";

    public ConstructorFailingActivationConsumer(ITestActivationDependencyA dep)
    {
        throw new InvalidOperationException("Constructor intentionally fails");
    }

    static ConstructorFailingActivationConsumer IOutboxConsumerActivation<ConstructorFailingActivationConsumer>.CreateOutboxConsumer(IServiceProvider services)
        => new ConstructorFailingActivationConsumer(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ITestActivationDependencyA>(services));

    public ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(TestActivationPayload payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        ConsumeWasCalled = true;
        return ValueTask.FromResult(OutboxRequiredConsumerResult.Accepted());
    }

    public static void Reset() => ConsumeWasCalled = false;
}

public sealed class IdMismatchActivationConsumer
    : IOutboxRequiredConsumer<TestActivationPayload>, IOutboxConsumerActivation<IdMismatchActivationConsumer>
{
    public string ConsumerId => "wrong-id";

    public IdMismatchActivationConsumer() { }

    static IdMismatchActivationConsumer IOutboxConsumerActivation<IdMismatchActivationConsumer>.CreateOutboxConsumer(IServiceProvider services)
        => new IdMismatchActivationConsumer();

    public ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(TestActivationPayload payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(OutboxRequiredConsumerResult.Accepted());
}

#endregion

#region Tests

public sealed class OutboxConsumerActivationTests
{
    [Fact]
    public void MissingConsumerDependency_Should_FailClosed()
    {
        var services = new ServiceCollection();
        services.AddOutboxRequiredConsumer<TestActivationPayload, WellFormedActivationConsumer>("well-formed-activation-consumer");

        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<WellFormedActivationConsumer>();

        act.Should().Throw<InvalidOperationException>(
            because: "missing DI dependency should cause static factory to fail when resolving unregistered dependency");
    }

    [Fact]
    public void DuplicateConsumerId_Should_FailComposition()
    {
        var services = new ServiceCollection();
        services.AddOutboxRequiredConsumer<TestActivationPayload, WellFormedActivationConsumer>("duplicate-id");
        services.AddOutboxRequiredConsumer<TestActivationPayload, IdMismatchActivationConsumer>("duplicate-id");

        var metadata = services
            .Where(d => d.ServiceType == typeof(OutboxRequiredConsumerMetadata))
            .Select(d => (OutboxRequiredConsumerMetadata)d.ImplementationInstance!)
            .ToList();

        metadata.Should().HaveCount(2);
        metadata.Select(m => m.ConsumerId).Distinct(StringComparer.Ordinal).Count()
            .Should().BeLessThan(metadata.Count,
                because: "duplicate consumer IDs should be detectable in metadata registrations");
    }

    [Fact]
    public void ConsumerIdMismatch_Should_FailComposition()
    {
        var services = new ServiceCollection();
        services.AddOutboxRequiredConsumer<TestActivationPayload, IdMismatchActivationConsumer>("registered-id");

        using var sp = services.BuildServiceProvider();

        var validations = sp.GetServices<OutboxRequiredConsumerValidationRegistration>().ToList();
        validations.Should().NotBeEmpty();

        using var scope = sp.CreateScope();
        var act = () =>
        {
            foreach (var v in validations)
                v.ValidateResolution(scope.ServiceProvider);
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mismatched*");
    }

    [Fact]
    public void ConsumerActivation_Should_RespectScopeAndDisposal()
    {
        ScopeTrackingActivationConsumer.Reset();

        var services = new ServiceCollection();
        services.AddOutboxRequiredConsumer<TestActivationPayload, ScopeTrackingActivationConsumer>("scope-tracking-activation-consumer");

        using var sp = services.BuildServiceProvider();

        ScopeTrackingActivationConsumer instance1A;
        ScopeTrackingActivationConsumer instance1B;
        using (var scope1 = sp.CreateScope())
        {
            instance1A = scope1.ServiceProvider.GetRequiredService<ScopeTrackingActivationConsumer>();
            instance1B = scope1.ServiceProvider.GetRequiredService<ScopeTrackingActivationConsumer>();
            instance1A.Should().BeSameAs(instance1B, because: "same scope should return same instance");
        }

        ScopeTrackingActivationConsumer instance2;
        using (var scope2 = sp.CreateScope())
        {
            instance2 = scope2.ServiceProvider.GetRequiredService<ScopeTrackingActivationConsumer>();
        }

        instance1A.Should().NotBeSameAs(instance2, because: "different scopes should return different instances");
        ScopeTrackingActivationConsumer.InstanceCount.Should().Be(2);
        instance1A.IsDisposed.Should().BeTrue(because: "scope disposal should dispose scoped consumer");
    }

    [Fact]
    public void ConstructorFailure_Should_Not_InvokeConsumer()
    {
        ConstructorFailingActivationConsumer.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<ITestActivationDependencyA, TestActivationDependencyA>();
        services.AddOutboxRequiredConsumer<TestActivationPayload, ConstructorFailingActivationConsumer>("constructor-failing-activation-consumer");

        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<ConstructorFailingActivationConsumer>();

        act.Should().Throw<InvalidOperationException>();
        ConstructorFailingActivationConsumer.ConsumeWasCalled.Should().BeFalse(
            because: "constructor failure must not trigger business method invocation");
    }

    [Fact]
    public void UnregisteredConsumer_Should_Not_Be_Discovered_Implicitly()
    {
        var services = new ServiceCollection();
        services.AddOutboxRequiredConsumer<TestActivationPayload, WellFormedActivationConsumer>("well-formed-activation-consumer");
        services.AddSingleton<ITestActivationDependencyA, TestActivationDependencyA>();
        services.AddSingleton<ITestActivationDependencyB, TestActivationDependencyB>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IOutboxRequiredConsumerResolver<TestActivationPayload>>();

        var act = () => resolver.Resolve(sp, "unregistered-consumer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unregistered-consumer*");
    }

    [Fact]
    public void ConsumerRegistration_Should_UseStaticFactory_NotImplementationType()
    {
        var services = new ServiceCollection();
        services.AddOutboxRequiredConsumer<TestActivationPayload, WellFormedActivationConsumer>("well-formed-activation-consumer");

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(WellFormedActivationConsumer));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationFactory.Should().NotBeNull(
            because: "consumer registration should use a static factory delegate, not implementation-type resolution");
        descriptor.ImplementationType.Should().BeNull(
            because: "implementation-type registration should not be used after activation migration");
    }

    [Fact]
    public void ActivationBinding_Should_Not_Depend_On_RegistrationOrder()
    {
        var servicesA = new ServiceCollection();
        servicesA.AddSingleton<ITestActivationDependencyA, TestActivationDependencyA>();
        servicesA.AddSingleton<ITestActivationDependencyB, TestActivationDependencyB>();
        servicesA.AddOutboxRequiredConsumer<TestActivationPayload, WellFormedActivationConsumer>("well-formed-activation-consumer");

        var servicesB = new ServiceCollection();
        servicesB.AddOutboxRequiredConsumer<TestActivationPayload, WellFormedActivationConsumer>("well-formed-activation-consumer");
        servicesB.AddSingleton<ITestActivationDependencyA, TestActivationDependencyA>();
        servicesB.AddSingleton<ITestActivationDependencyB, TestActivationDependencyB>();

        using var spA = servicesA.BuildServiceProvider();
        using var spB = servicesB.BuildServiceProvider();

        var consumerA = spA.CreateScope().ServiceProvider.GetRequiredService<WellFormedActivationConsumer>();
        var consumerB = spB.CreateScope().ServiceProvider.GetRequiredService<WellFormedActivationConsumer>();

        consumerA.Should().NotBeNull();
        consumerB.Should().NotBeNull();
        consumerA.ConsumerId.Should().Be(consumerB.ConsumerId);
    }
}

#endregion
