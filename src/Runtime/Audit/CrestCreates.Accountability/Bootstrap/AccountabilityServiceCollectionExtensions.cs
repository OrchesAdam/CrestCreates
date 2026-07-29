using CrestCreates.Accountability.Abstractions.Composition;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.Context;
using CrestCreates.Accountability.Identity;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Bootstrap;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Accountability.Bootstrap;

public static class AccountabilityServiceCollectionExtensions
{
    public static IServiceCollection AddAccountability(
        this IServiceCollection services,
        Action<AccountabilityOptions>? configure = null)
    {
        var options = new AccountabilityOptions();
        configure?.Invoke(options);
        // Accountability owns its canonical hash dependency. Producers must not
        // have to register unrelated Metadata services to make the foundation valid.
        services.AddDescriptorStableHash();
        services.TryAddSingleton(options);
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
        services.TryAddSingleton<IAuditRecorder, DefaultAuditRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, AccountabilityCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AccountabilityCompositionValidator>());
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
