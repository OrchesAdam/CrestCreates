using CrestCreates.DescriptorDraft.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DescriptorDraft;

public static class DescriptorDraftServiceCollectionExtensions
{
    public static IServiceCollection AddDescriptorDrafts(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorDraftStore, InMemoryDescriptorDraftStore>();
        services.TryAddSingleton<IDescriptorDraftValidator, DefaultDescriptorDraftValidator>();
        services.TryAddSingleton<IDescriptorDraftMaterializer, DefaultDescriptorDraftMaterializer>();
        services.TryAddSingleton<IDescriptorDraftReviewService, DefaultDescriptorDraftReviewService>();
        return services;
    }
}
