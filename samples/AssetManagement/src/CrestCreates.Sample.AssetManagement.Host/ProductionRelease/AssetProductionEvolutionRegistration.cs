using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public static class AssetProductionEvolutionRegistration
{
    /// <summary>Registers the Asset production proposal flow and its Control Plane admission guard.</summary>
    public static IServiceCollection AddAssetProductionEvolution(this IServiceCollection services)
    {
        services.TryAddScoped<AssetProductionEvolutionService>();
        services.TryAddSingleton<DefaultDescriptorActivationRequestService>();
        if (!services.Any(service => service.ServiceType == typeof(AssetProductionEvolutionActivation)))
        {
            services.AddSingleton<AssetProductionEvolutionActivation>(sp => new(
                sp.GetRequiredService<DefaultDescriptorActivationRequestService>(),
                sp.GetRequiredService<IDescriptorDraftStore>(),
                sp.GetRequiredService<IAgentPackageArtifactStore>(),
                sp.GetRequiredService<IDescriptorPackageSerializer>(),
                sp.GetRequiredService<IActivationEvidenceRechecker>()));
            services.Replace(ServiceDescriptor.Singleton<IDescriptorActivationRequestService>(sp =>
                sp.GetRequiredService<AssetProductionEvolutionActivation>()));
        }

        return services;
    }
}
