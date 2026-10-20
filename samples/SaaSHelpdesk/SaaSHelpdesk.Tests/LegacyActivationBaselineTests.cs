using CrestCreates.DynamicApi;
using SaaSHelpdesk.Application.Contracts.Interfaces;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// #129 S4 L02: the remaining legacy services are explicitly allowlisted, and the formal Ticket
/// contract is never published by the legacy provider. The host's MapLegacyGeneratedApi validation
/// enforces the exact allowlist; this baseline locks the composition fact.
/// </summary>
public class LegacyActivationBaselineTests : BaseTest
{
    public LegacyActivationBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void Legacy_provider_does_not_publish_the_formal_ticket_contract()
    {
        var options = Factory.Services.GetRequiredService<DynamicApiOptions>();
        var registry = DynamicApiGeneratedRegistryStore.BuildRegistry(options);

        registry.Should().NotBeNull("the explicitly activated legacy provider must be discoverable");
        registry!.Services.Should().NotContain(service => service.ServiceType == typeof(ITicketAppService),
            "Ticket is owned exclusively by the formal ExplicitHost contribution");
        registry.Services.Select(service => service.ServiceName).Should().BeEquivalentTo(
            new[] { "Agent", "Category", "Customer", "CustomerPortal", "Dashboard", "KnowledgeBase", "SLAPolicy" },
            "the SaaSHelpdesk legacy allowlist is an explicit per-service list; a new service must be reviewed and added");
    }
}
