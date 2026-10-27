using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Abstractions.Validation;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Accountability.Retention;

public static class AuditRetentionServiceCollectionExtensions
{
    public static IServiceCollection AddAuditRetention(this IServiceCollection services)
    {
        services.TryAddSingleton<IAuditRetentionService, AuditRetentionExecutor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, AuditRetentionCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditRetentionCompositionValidator>());
        return services;
    }
}

public sealed class AuditRetentionCompositionValidator : IBootstrapValidator, IHostedService
{
    private readonly IEnumerable<IAuditRetentionStore> _stores;
    private readonly IAuditRetentionService? _service;

    public AuditRetentionCompositionValidator(
        IEnumerable<IAuditRetentionStore> stores,
        IAuditRetentionService? service = null)
    {
        _stores = stores;
        _service = service;
    }

    public int Order => -190;

    public ValidationReport Validate()
    {
        var issues = new List<ValidationIssue>();
        if (_service is null)
        {
            issues.Add(Error("RETENTION_SERVICE_MISSING", "Audit retention service is not registered."));
        }

        var stores = _stores.ToArray();
        if (stores.Length == 0)
        {
            issues.Add(Error("RETENTION_STORE_REQUIRED", "Audit retention requires at least one explicit store."));
        }

        foreach (var duplicate in stores.GroupBy(store => store.Identity.StoreId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(Error("RETENTION_DUPLICATE_STORE_ID", $"Duplicate audit retention store id '{duplicate.Key}'."));
        }

        foreach (var store in stores)
        {
            var identity = store.Identity;
            if (!AuditSemanticNames.IsStableKind(identity.StoreId, AuditContractLimits.MaxIdentifierLength)
                || !AuditSemanticNames.IsStableKind(identity.SinkId, AuditContractLimits.MaxIdentifierLength))
            {
                issues.Add(Error(
                    "RETENTION_INVALID_STORE_IDENTITY",
                    $"Audit retention store '{identity.StoreId}' or sink '{identity.SinkId}' is not a stable identifier."));
            }

            if (!identity.Capabilities.IsComplete)
            {
                issues.Add(Error(
                    "RETENTION_CAPABILITY_MISSING",
                    $"Audit retention store '{identity.StoreId}' does not declare the full retention capability set."));
            }
        }

        var duplicateSinks = stores
            .GroupBy(store => store.Identity.SinkId, StringComparer.Ordinal)
            .Where(group => group.Select(store => store.Identity.StoreId).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var sinkId in duplicateSinks)
        {
            issues.Add(Error("RETENTION_SHARED_SINK", $"Multiple audit retention stores target sink '{sinkId}'."));
        }

        return issues.Count == 0 ? ValidationReport.Empty : ValidationReport.FromIssues(issues.ToArray());
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var report = Validate();
        if (report.HasErrors)
        {
            throw new InvalidOperationException(string.Join("; ", report.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ValidationIssue Error(string code, string message)
        => new(SeverityLevel.Error, message) { Code = new DiagnosticCode(code) };
}
