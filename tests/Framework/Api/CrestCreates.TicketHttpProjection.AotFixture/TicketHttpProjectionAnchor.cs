using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.Interfaces;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Local explicit-host anchor over the real Ticket contract. The fixture owns no HTTP map: the formal
/// AppService HTTP/JSON toolchain materializes the 11 action bundle from the contract attributes.
/// </summary>
[CapabilityCompatibilityProjection(
    ContractType = typeof(ITicketAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/ticket",
    CapabilityIdPrefix = "compat.appservice.ticket")]
public static class TicketHttpProjectionAnchor;
