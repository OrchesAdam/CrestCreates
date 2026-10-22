using CrestCreates.Generated.HttpProjection;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Calls the fixture's generated per-assembly AppService HTTP activation explicitly. The fixture
/// references SaaSHelpdesk.Application, which carries its own generated extension of the same name;
/// naming the fixture activation type avoids ambiguous extension resolution and, more importantly,
/// activates only the Ticket declaration generated for this assembly.
/// </summary>
public static class TicketActivation
{
    public static IServiceCollection Add(IServiceCollection services)
        => GeneratedAppServiceHttpActivation_CrestCreates_002ETicketHttpProjection_002EAotFixture
            .AddGeneratedAppServiceHttp(services);
}
