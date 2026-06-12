using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Form;

public static class FormServiceCollectionExtensions
{
    public static IServiceCollection AddFormKernel(this IServiceCollection services)
    {
        // Registry (singleton — holds built snapshot)
        services.TryAddSingleton<IFormRegistry, FormRegistry>();

        // Validation engine (singleton — consumed by singleton FormRegistry)
        // MUST be singleton to avoid captive dependency.
        services.TryAddSingleton<IRegistryValidationEngine<FormDescriptor>,
            RegistryValidationEngine<FormDescriptor>>();

        // Validators (singleton — stateless, consumed by singleton engine)
        services.TryAddSingleton<IRegistryValidator<FormDescriptor>,
            FormDescriptorValidator>();

        // Schema binding validator (singleton — stateless, used via onFormBuilt callback)
        services.TryAddSingleton<FormSchemaBindingValidator>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, FormBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, FormRelationshipExtractor>();

        return services;
    }
}
