using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Middleware;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Validation.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Application.Services;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Repositories;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// The fixture's complete service composition. Program and the metadata-rejection probe both use
/// this exact registration; only the JSON resolver chain differs in the negative probe.
/// </summary>
public static class TicketFixtureComposition
{
    public static IServiceCollection AddTicketFixture(this IServiceCollection services)
    {
        services.AddCapabilityRuntime();
        services.AddAccountability();
        services.RemoveAll<ValidationMiddleware>();
        services.RemoveAll<IBootstrapValidator>();
        services.RemoveAll<IDescriptorBindingStatusContributor>();
        services.AddSingleton(new CapabilityPipelineBuilder()
            .Use<AuthorizationMiddleware>());
        services.AddCrestCompatibilityProjection();

        TicketActivation.Add(services);

        services.AddSingleton<TicketMemoryStore>();
        services.AddSingleton<MemoryTicketRepository>();
        services.AddSingleton<ITicketRepository>(provider => provider.GetRequiredService<MemoryTicketRepository>());
        services.AddSingleton<ICrestRepositoryBase<Ticket, Guid>>(provider => provider.GetRequiredService<MemoryTicketRepository>());
        services.AddSingleton<MemorySlaPolicyRepository>();
        services.AddSingleton<ICrestRepositoryBase<SLAPolicy, Guid>>(provider => provider.GetRequiredService<MemorySlaPolicyRepository>());

        services.AddScoped<TicketAppService>();
        services.AddScoped<ITicketAppService>(provider => provider.GetRequiredService<TicketAppService>());
        services.AddScoped<IValidationService, ValidationService>();
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(TicketFailureJsonContext.Default));

        services.AddSingleton<IPermissionChecker, FixturePermissionChecker>();
        services.AddSingleton<ICurrentUser, FixtureCurrentUser>();
        services.AddSingleton<IDataPermissionFilter, IdentityDataPermissionFilter>();
        services.AddSingleton<CrestCreates.Capability.Abstractions.ICapabilityExceptionClassifier, TicketExceptionClassifier>();
        services.AddSingleton<ICapabilityEndpointFailurePresenter, TicketEndpointFailurePresenter>();

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddUnitOfWorkProvider(
            OrmProvider.EfCore,
            static provider => provider.GetRequiredService<MemoryTicketUnitOfWork>(),
            supportsRequiresNew: false,
            capabilities: new UnitOfWorkProviderCapabilities
            {
                SupportsTransactions = true,
                PromptTerminationOnAbandon = true,
                DiscardUncommittedOnAbandon = true,
            });
        services.AddScoped<MemoryTicketUnitOfWork>();

        services.AddLogging();
        return services;
    }
}
