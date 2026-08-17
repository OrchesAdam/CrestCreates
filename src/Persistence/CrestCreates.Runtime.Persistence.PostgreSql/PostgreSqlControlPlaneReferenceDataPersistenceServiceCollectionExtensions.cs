using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public static class PostgreSqlControlPlaneReferenceDataPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var hasBaseProvider = false;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(PostgreSqlRuntimeProviderRegistrationMarker))
                hasBaseProvider = true;
        }

        if (!hasBaseProvider)
        {
            throw new InvalidOperationException(
                "Control Plane and Reference Data persistence requires the base PostgreSQL Runtime persistence provider. " +
                "Call AddCrestCreatesPostgreSqlRuntimePersistence(options) before adding the Control Plane and Reference Data stores.");
        }

        services.RemoveAll<IDescriptorDraftStore>();
        services.RemoveAll<IOrganizationStore>();
        services.RemoveAll<IDataPermissionScopeRuleStore>();

        services.AddSingleton<IDescriptorDraftStore, PostgreSqlDescriptorDraftStore>();
        services.AddSingleton<IOrganizationStore, PostgreSqlOrganizationStore>();
        services.AddSingleton<IDataPermissionScopeRuleStore, PostgreSqlDataPermissionScopeRuleStore>();

        return services;
    }
}

internal sealed class PostgreSqlRuntimeProviderRegistrationMarker { }
