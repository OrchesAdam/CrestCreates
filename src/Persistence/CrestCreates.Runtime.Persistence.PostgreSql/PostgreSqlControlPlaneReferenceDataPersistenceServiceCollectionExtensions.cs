using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public static class PostgreSqlControlPlaneReferenceDataPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var hasBaseProvider = services.Any(descriptor =>
            descriptor.ServiceType == typeof(PostgreSqlRuntimeProviderRegistrationMarker));
        var hasProviderKernel = services.Any(descriptor => descriptor.ServiceType == typeof(PostgreSqlRuntimePersistenceOptions))
            && services.Any(descriptor => descriptor.ServiceType == typeof(NpgsqlDataSource))
            && services.Any(descriptor => descriptor.ServiceType == typeof(PostgreSqlRuntimeMigrationRunner))
            && services.Any(descriptor => descriptor.ServiceType == typeof(PostgreSqlRuntimeTransactionCoordinator));

        if (!hasBaseProvider || !hasProviderKernel)
        {
            throw new InvalidOperationException(
                "Control Plane and Reference Data persistence requires the complete base PostgreSQL Runtime persistence provider kernel. " +
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
