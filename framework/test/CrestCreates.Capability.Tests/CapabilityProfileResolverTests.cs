using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityProfileResolverTests
{
    [Fact]
    public void Resolve_ReturnsDefaults_WhenNoProfiles()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var result = CapabilityProfileResolver.Resolve(descriptor, Array.Empty<CapabilityProfile>());

        result.Timeout.Should().BeNull();
        result.RequireApproval.Should().BeNull();
    }

    [Fact]
    public void Resolve_TenantProfile_WinsOverGlobal()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var profiles = new[]
        {
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
                Scope = "Global",
                Timeout = TimeSpan.FromSeconds(10)
            },
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
                Scope = "Tenant:VIP",
                Timeout = TimeSpan.FromSeconds(5)
            }
        };

        var result = CapabilityProfileResolver.Resolve(descriptor, profiles, tenantId: "VIP");
        result.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Resolve_GlobalOnly_ReturnsGlobal()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var profiles = new[]
        {
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
                Scope = "Global-Prod",
                Timeout = TimeSpan.FromSeconds(30)
            }
        };

        var result = CapabilityProfileResolver.Resolve(descriptor, profiles);
        result.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Resolve_IgnoresUnrelatedProfiles()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var profiles = new[]
        {
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_02", 1),
                Scope = "Global",
                Timeout = TimeSpan.FromSeconds(99)
            }
        };

        var result = CapabilityProfileResolver.Resolve(descriptor, profiles);
        result.Timeout.Should().BeNull();
    }
}