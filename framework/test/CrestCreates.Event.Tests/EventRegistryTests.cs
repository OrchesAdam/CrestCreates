using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventRegistryTests
{
    private static GeneratedEventDescriptor CreateDescriptor(
        string name, int version, DescriptorState state = DescriptorState.Active,
        Type? payloadType = null)
        => new()
        {
            Id = GeneratedEventDescriptor.GenerateId(name),
            Name = name,
            Version = version,
            State = state,
            PayloadType = payloadType ?? typeof(object),
            Scope = EventScope.Local,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Business
        };

    private class TestProvider(List<GeneratedEventDescriptor> descriptors) : IEventDescriptorProvider
    {
        public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => descriptors;
    }

    [Fact]
    public void Build_single_descriptor_succeeds()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([CreateDescriptor("test.event", 1)]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Build_is_idempotent()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([CreateDescriptor("test.event", 1)]);

        registry.Build([provider]);
        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Build_throws_on_duplicate_name_version()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1),
            CreateDescriptor("test.event", 1)
        ]);

        Action act = () => registry.Build([provider]);
        act.Should().Throw<EventRegistryBuildException>()
            .WithMessage("*Duplicate*");
    }

    [Fact]
    public void Build_throws_when_no_active_version()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Deprecated)
        ]);

        Action act = () => registry.Build([provider]);
        act.Should().Throw<EventRegistryBuildException>()
            .WithMessage("*no Active version*");
    }

    [Fact]
    public void Build_throws_when_multiple_active_versions()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Active),
            CreateDescriptor("test.event", 2, DescriptorState.Active)
        ]);

        Action act = () => registry.Build([provider]);
        act.Should().Throw<EventRegistryBuildException>()
            .WithMessage("*Active versions*");
    }

    [Fact]
    public void Build_throws_when_highest_is_not_active()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Active),
            CreateDescriptor("test.event", 2, DescriptorState.Deprecated)
        ]);

        Action act = () => registry.Build([provider]);
        act.Should().Throw<EventRegistryBuildException>()
            .WithMessage("*highest version*");
    }

    [Fact]
    public void Build_succeeds_for_upgrade_scenario()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Deprecated),
            CreateDescriptor("test.event", 2, DescriptorState.Active)
        ]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void GetByName_returns_highest_active()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Deprecated),
            CreateDescriptor("test.event", 2, DescriptorState.Active)
        ]);
        registry.Build([provider]);

        var result = registry.GetByName("test.event");

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void GetByNameAndVersion_returns_exact_version()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Deprecated),
            CreateDescriptor("test.event", 2, DescriptorState.Active)
        ]);
        registry.Build([provider]);

        var v1 = registry.GetByNameAndVersion("test.event", 1);
        var v2 = registry.GetByNameAndVersion("test.event", 2);

        v1!.Version.Should().Be(1);
        v1.State.Should().Be(DescriptorState.Deprecated);
        v2!.Version.Should().Be(2);
    }

    [Fact]
    public void GetLatestVersion_returns_highest_regardless_of_state()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Removed),
            CreateDescriptor("test.event", 2, DescriptorState.Active)
        ]);
        registry.Build([provider]);

        var latest = registry.GetLatestVersion("test.event");

        latest.Should().NotBeNull();
        latest!.Version.Should().Be(2);
    }

    [Fact]
    public void GetAllVersions_returns_all()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Deprecated),
            CreateDescriptor("test.event", 2, DescriptorState.Active)
        ]);
        registry.Build([provider]);

        var all = registry.GetAllVersions("test.event");

        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetByPayloadType_resolves_typed_publish()
    {
        var payloadType = typeof(string);
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, payloadType: payloadType)
        ]);
        registry.Build([provider]);

        var result = registry.GetByPayloadType(payloadType);

        result.Should().NotBeNull();
        result!.Name.Should().Be("test.event");
    }

    [Fact]
    public void Build_marks_state_failed_on_exception()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("test.event", 1, DescriptorState.Deprecated)
        ]);

        try { registry.Build([provider]); } catch { }

        registry.State.Should().Be(RegistryState.Failed);
    }

    [Fact]
    public void Build_after_failed_throws()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([CreateDescriptor("test.event", 1, DescriptorState.Deprecated)]);
        try { registry.Build([provider]); } catch { }

        var goodProvider = new TestProvider([CreateDescriptor("test.event", 1)]);
        Action act = () => registry.Build([goodProvider]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*previously failed*");
    }

    [Fact]
    public void GetAll_returns_all_descriptors()
    {
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("evt.a", 1, payloadType: typeof(string)),
            CreateDescriptor("evt.b", 1, payloadType: typeof(int))
        ]);
        registry.Build([provider]);

        var all = registry.GetAll();

        all.Should().HaveCount(2);
    }

    [Fact]
    public void Build_throws_on_payload_type_conflict()
    {
        var sharedType = typeof(string);
        var registry = new EventRegistry();
        var provider = new TestProvider([
            CreateDescriptor("evt.a", 1, payloadType: sharedType),
            CreateDescriptor("evt.b", 1, payloadType: sharedType)  // same type, different name
        ]);

        Action act = () => registry.Build([provider]);
        act.Should().Throw<EventRegistryBuildException>()
            .WithMessage("*PayloadType*");
    }
}
