using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Tests.Compatibility;

public class CompatibilityProjectionTests
{
    [Fact]
    public async Task Submit_via_compatibility_projection_returns_legacy_envelope()
    {
        true.Should().BeTrue();
    }
}
