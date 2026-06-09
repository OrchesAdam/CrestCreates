using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;
using RegistryState = CrestCreates.Metadata.Abstractions.RegistryState;

namespace CrestCreates.Event.Tests;

public class RegistryEventValidatorTests
{
    [Fact]
    public void ValidateOrThrow_registered_event_passes()
    {
        var resolver = new Mock<IEventResolver>();
        var metadata = new Mock<IEventMetadataProvider>();
        metadata.Setup(m => m.State).Returns(RegistryState.Built);
        resolver.Setup(r => r.GetByName("test.event"))
            .Returns(new GeneratedEventDescriptor { Name = "test.event" });
        var validator = new RegistryEventValidator(resolver.Object, metadata.Object);

        Action act = () => validator.ValidateOrThrow("test.event", null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateOrThrow_throws_when_registry_not_built()
    {
        var resolver = new Mock<IEventResolver>();
        var metadata = new Mock<IEventMetadataProvider>();
        metadata.Setup(m => m.State).Returns(RegistryState.Building);
        var validator = new RegistryEventValidator(resolver.Object, metadata.Object);

        Action act = () => validator.ValidateOrThrow("test.event", null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not been built*");
    }

    [Fact]
    public void ValidateOrThrow_throws_when_not_registered()
    {
        var resolver = new Mock<IEventResolver>();
        var metadata = new Mock<IEventMetadataProvider>();
        metadata.Setup(m => m.State).Returns(RegistryState.Built);
        resolver.Setup(r => r.GetByName("test.event")).Returns((IEventDescriptor?)null);
        metadata.Setup(m => m.GetLatestVersion("test.event")).Returns((GeneratedEventDescriptor?)null);
        var validator = new RegistryEventValidator(resolver.Object, metadata.Object);

        Action act = () => validator.ValidateOrThrow("test.event", null);

        act.Should().Throw<EventValidationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public void ValidateOrThrow_throws_on_deprecated()
    {
        var resolver = new Mock<IEventResolver>();
        var metadata = new Mock<IEventMetadataProvider>();
        metadata.Setup(m => m.State).Returns(RegistryState.Built);
        resolver.Setup(r => r.GetByName("test.event")).Returns((IEventDescriptor?)null);
        metadata.Setup(m => m.GetLatestVersion("test.event"))
            .Returns(new GeneratedEventDescriptor
            {
                Name = "test.event",
                State = DescriptorState.Deprecated
            });
        var validator = new RegistryEventValidator(resolver.Object, metadata.Object);

        Action act = () => validator.ValidateOrThrow("test.event", null);

        act.Should().Throw<EventValidationException>()
            .WithMessage("*deprecated*");
    }
}

public class DynamicEventRegistryTests
{
    [Fact]
    public void TryRegister_succeeds_for_local_event()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Built);
        generated.Setup(g => g.GetByName("custom.event")).Returns((GeneratedEventDescriptor?)null);
        var dynamic = new DynamicEventRegistry(generated.Object);

        var result = dynamic.TryRegister("custom.event", null, EventScope.Local);

        result.Should().BeTrue();
    }

    [Fact]
    public void TryRegister_throws_on_non_local_scope()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Built);
        var dynamic = new DynamicEventRegistry(generated.Object);

        Action act = () => dynamic.TryRegister("custom.event", null, EventScope.Integration);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Scope.Local*");
    }

    [Fact]
    public void TryRegister_returns_false_when_generated_conflicts()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Built);
        generated.Setup(g => g.GetByName("capability.succeeded"))
            .Returns(new GeneratedEventDescriptor { Name = "capability.succeeded" });
        var dynamic = new DynamicEventRegistry(generated.Object);

        var result = dynamic.TryRegister("capability.succeeded", null, EventScope.Local);

        result.Should().BeFalse();
    }

    [Fact]
    public void Upsert_throws_when_generated_conflicts()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Built);
        generated.Setup(g => g.GetByName("capability.succeeded"))
            .Returns(new GeneratedEventDescriptor { Name = "capability.succeeded" });
        var dynamic = new DynamicEventRegistry(generated.Object);

        Action act = () => dynamic.Upsert("capability.succeeded", null, EventScope.Local);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicts*");
    }

    [Fact]
    public void Upsert_replaces_existing_dynamic_event()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Built);
        generated.Setup(g => g.GetByName("custom.event")).Returns((GeneratedEventDescriptor?)null);
        var dynamic = new DynamicEventRegistry(generated.Object);

        dynamic.TryRegister("custom.event", typeof(string), EventScope.Local);
        dynamic.Upsert("custom.event", typeof(int), EventScope.Local);

        var descriptor = dynamic.GetByName("custom.event");
        descriptor.Should().NotBeNull();
        descriptor!.PayloadType.Should().Be(typeof(int));
    }

    [Fact]
    public void TryRegister_throws_when_registry_not_built()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Building);
        var dynamic = new DynamicEventRegistry(generated.Object);

        Action act = () => dynamic.TryRegister("custom.event", null, EventScope.Local);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Build()*");
    }

    [Fact]
    public void GetByName_returns_registered_dynamic_event()
    {
        var generated = new Mock<IEventRegistry>();
        generated.Setup(g => g.State).Returns(RegistryState.Built);
        generated.Setup(g => g.GetByName("custom.event")).Returns((GeneratedEventDescriptor?)null);
        var dynamic = new DynamicEventRegistry(generated.Object);
        dynamic.TryRegister("custom.event", typeof(string), EventScope.Local);

        var result = dynamic.GetByName("custom.event");

        result.Should().NotBeNull();
        result!.Name.Should().Be("custom.event");
    }
}
