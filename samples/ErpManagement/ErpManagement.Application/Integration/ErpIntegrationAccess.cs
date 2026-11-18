using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.Integration;
using Microsoft.Extensions.Logging;

namespace ErpManagement.Application.Integration;

internal static class ErpIntegrationAccess
{
    public static bool IsClient(this ICurrentPrincipalAccessor principalAccessor, ICurrentTenant tenant, string client)
        => tenant.Id is { Length: > 0 } tenantId && principalAccessor.Principal.Identities.Any(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == ErpIntegrationClients.Scheme
            && identity.HasClaim(ErpIntegrationClients.ClaimType, client)
            && identity.HasClaim("tenantid", tenantId));

    public static async Task PublishIfEnabledAsync<TEvent>(this ILocalEventBus events, IFeatureChecker features,
        string feature, TEvent message, ILogger logger, CancellationToken cancellationToken) where TEvent : ILocalEvent
    {
        try
        {
            if (await features.IsEnabledAsync(feature, cancellationToken))
                await events.PublishAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ERP could not publish the optional outbound event {EventType}", typeof(TEvent).Name);
        }
    }
}
