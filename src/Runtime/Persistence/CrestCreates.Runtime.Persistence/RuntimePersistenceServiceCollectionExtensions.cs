using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Json;
using CrestCreates.Runtime.Persistence.State;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Persistence;

public static class RuntimePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimePersistence(this IServiceCollection services)
    {
        services.AddSingleton<IRuntimeStateContractContributor, BuiltInRuntimeStateContractContributor>();
        services.AddSingleton<RuntimeStateContractRegistry>(provider =>
        {
            var builder = new RuntimeStateContractBuilder();
            foreach (var contributor in provider.GetServices<IRuntimeStateContractContributor>())
                contributor.Contribute(builder);

            return builder.Build();
        });
        services.AddSingleton<IRuntimeStateContractRegistry>(provider =>
            provider.GetRequiredService<RuntimeStateContractRegistry>());
        return services;
    }
}
