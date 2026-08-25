using CrestCreates.Accountability.Abstractions.Composition;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.Context;
using CrestCreates.Accountability.Identity;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Accountability.Preparation;
using CrestCreates.Accountability.Delivery;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Bootstrap;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestCreates.Accountability.Bootstrap;

public static class AccountabilityServiceCollectionExtensions
{
    public static IServiceCollection AddAccountability(
        this IServiceCollection services,
        Action<AccountabilityOptions>? configure = null)
    {
        // Accountability owns its canonical hash dependency. Producers must not
        // have to register unrelated Metadata services to make the foundation valid.
        services.AddDescriptorStableHash();
        services.AddOptions<AccountabilityOptions>();
        if (configure is not null)
            services.Configure(configure);
        services.TryAddSingleton<AccountabilityOptions>(sp =>
            sp.GetRequiredService<IOptions<AccountabilityOptions>>().Value);
        services.TryAddSingleton<IAccountabilityRuntimeMarker, AccountabilityRuntimeMarker>();
        services.TryAddSingleton<AuditEnvelopeValidator>();
        services.TryAddSingleton<IAuditOperationContextAccessor, AuditOperationContextAccessor>();
        services.TryAddSingleton<IAuditIdentityGenerator, GuidAuditIdentityGenerator>();
        services.TryAddSingleton<AccountabilityCanonicalProjectionWriter>();
        services.TryAddSingleton<AuditPayloadSanitizationRuleRegistry>(sp =>
            new AuditPayloadSanitizationRuleRegistry(sp.GetServices<IAuditPayloadSanitizationRule>()));
        services.TryAddSingleton<AuditDataArtifactSanitizationRuleRegistry>(sp =>
            new AuditDataArtifactSanitizationRuleRegistry(sp.GetServices<IAuditDataArtifactSanitizationRule>()));
        services.TryAddSingleton<IAuditSanitizer, DefaultAuditSanitizer>();
        services.TryAddSingleton<IAuditIntegrityHasher, DefaultAuditIntegrityHasher>();
        services.TryAddSingleton<IAuditEnvelopePreparer, DefaultAuditEnvelopePreparer>();
        services.TryAddScoped<AuditSinkFanOut>();
        services.TryAddScoped<PreparedAuditRecorder>();
        services.TryAddSingleton<IAuditRecorder, DefaultAuditRecorder>();
        services.AddSingleton<CrestCreates.Runtime.Delivery.Abstractions.Registration.OutboxDeliveryHandlerRegistration>(
            new CrestCreates.Runtime.Delivery.Abstractions.Registration.OutboxDeliveryHandlerRegistration(
                AccountabilityDeliveryConstants.ContractId,
                sp => sp.GetRequiredService<AccountabilityOutboxHandler>()));
        services.TryAddScoped<AccountabilityOutboxHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, AccountabilityCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AccountabilityCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<CrestCreates.Runtime.Delivery.Abstractions.Composition.IOutboxDurableCompositionCheck, AccountabilityOutboxSinkCompositionCheck>());
        return services;
    }

    public static IServiceCollection AddAuditSink<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSink>(this IServiceCollection services)
        where TSink : class, IAuditSink
    {
        services.AddSingleton<IAuditSink, TSink>();
        return services;
    }

    private sealed class AccountabilityRuntimeMarker : IAccountabilityRuntimeMarker
    {
    }
}
