using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityProfileTests
{
    [Fact]
    public void CapabilityProfile_References_Capability_By_VersionedRef()
    {
        var profile = new CapabilityProfile
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 3),
            Scope = "Global-Prod",
            Timeout = TimeSpan.FromSeconds(10)
        };

        profile.Capability.Id.Should().Be("cap_01");
        profile.Capability.Version.Should().Be(3);
        profile.Scope.Should().Be("Global-Prod");
        profile.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CapabilityProfile_Defaults_All_Optional_Props_To_Null()
    {
        var profile = new CapabilityProfile
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            Scope = "Global"
        };

        profile.Timeout.Should().BeNull();
        profile.RetryPolicy.Should().BeNull();
        profile.RequireApproval.Should().BeNull();
        profile.RateLimit.Should().BeNull();
    }
}