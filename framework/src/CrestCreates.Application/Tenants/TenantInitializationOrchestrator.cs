using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Application.Tenants;

public class TenantInitializationOrchestrator
{
    private readonly ITenantDatabaseInitializer _dbInitializer;
    private readonly ITenantMigrationRunner _migrationRunner;
    private readonly ITenantDataSeeder _dataSeeder;
    private readonly ITenantSettingDefaultsSeeder _settingsSeeder;
    private readonly ITenantFeatureDefaultsSeeder _featuresSeeder;
    private readonly ITenantInitializationStore _store;
    private readonly ILogger<TenantInitializationOrchestrator> _logger;

    private const int MaxErrorLength = 2000;

    public TenantInitializationOrchestrator(
        ITenantDatabaseInitializer dbInitializer,
        ITenantMigrationRunner migrationRunner,
        ITenantDataSeeder dataSeeder,
        ITenantSettingDefaultsSeeder settingsSeeder,
        ITenantFeatureDefaultsSeeder featuresSeeder,
        ITenantInitializationStore store,
        ILogger<TenantInitializationOrchestrator> logger)
    {
        _dbInitializer = dbInitializer;
        _migrationRunner = migrationRunner;
        _dataSeeder = dataSeeder;
        _settingsSeeder = settingsSeeder;
        _featuresSeeder = featuresSeeder;
        _store = store;
        _logger = logger;
    }

    public async Task<TenantInitializationResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Atomic begin
        var record = await _store.TryBeginInitializationAsync(
            context.TenantId, context.CorrelationId, cancellationToken);

        if (record is null)
            return TenantInitializationResult.Failed(
                context.CorrelationId,
                "Tenant is already initializing or initialized.",
                Array.Empty<TenantInitializationStep>());

        var steps = new List<TenantInitializationStep>();

        try
        {
            // Phase 1: Database Initialize (independent only)
            if (context.IsIndependentDatabase)
            {
                var step1 = await ExecutePhaseAsync("DatabaseInitialize", record,
                    async ct => await _dbInitializer.InitializeAsync(context, ct),
                    cancellationToken);
                steps.Add(step1);
                if (step1.Status != TenantInitializationStepStatus.Succeeded)
                    return BuildFailureResult(context, record, steps, step1.Error);

                // Phase 2: Migration (independent only)
                var step2 = await ExecutePhaseAsync("Migration", record,
                    async ct => await _migrationRunner.RunAsync(context, ct),
                    cancellationToken);
                steps.Add(step2);
                if (step2.Status != TenantInitializationStepStatus.Succeeded)
                    return BuildFailureResult(context, record, steps, step2.Error);
            }

            // Phase 3: Data Seed
            var step3 = await ExecutePhaseAsync("DataSeed", record,
                async ct => await _dataSeeder.SeedAsync(context, ct),
                cancellationToken);
            steps.Add(step3);
            if (step3.Status != TenantInitializationStepStatus.Succeeded)
                return BuildFailureResult(context, record, steps, step3.Error);

            // Phase 4: Settings Defaults
            var step4 = await ExecutePhaseAsync("SettingsDefaults", record,
                async ct => await _settingsSeeder.SeedAsync(context, ct),
                cancellationToken);
            steps.Add(step4);
            if (step4.Status != TenantInitializationStepStatus.Succeeded)
                return BuildFailureResult(context, record, steps, step4.Error);

            // Phase 5: Feature Defaults
            var step5 = await ExecutePhaseAsync("FeatureDefaults", record,
                async ct => await _featuresSeeder.SeedAsync(context, ct),
                cancellationToken);
            steps.Add(step5);
            if (step5.Status != TenantInitializationStepStatus.Succeeded)
                return BuildFailureResult(context, record, steps, step5.Error);

            // Success
            record.MarkSucceeded();
            await _store.UpdateAsync(record, cancellationToken);

            return TenantInitializationResult.Succeeded(context.CorrelationId, steps);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Infrastructure failure during tenant {TenantId} initialization. CorrelationId: {CorrelationId}",
                context.TenantId, context.CorrelationId);
            throw;
        }
    }

    private async Task<TenantInitializationStep> ExecutePhaseAsync(
        string phaseName,
        TenantInitializationRecord record,
        Func<CancellationToken, Task<IPhaseResult>> phaseAction,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;

        record.SetCurrentStep(phaseName);
        record.AppendStepResult(phaseName, TenantInitializationStepStatus.Running, startedAt, null, null);
        await _store.UpdateAsync(record, cancellationToken);

        try
        {
            var result = await phaseAction(cancellationToken);
            var completedAt = DateTime.UtcNow;

            if (result.Success)
            {
                record.AppendStepResult(phaseName, TenantInitializationStepStatus.Succeeded,
                    startedAt, completedAt, null);
                await _store.UpdateAsync(record, cancellationToken);

                return new TenantInitializationStep
                {
                    Name = phaseName,
                    Status = TenantInitializationStepStatus.Succeeded,
                    StartedAt = startedAt,
                    CompletedAt = completedAt
                };
            }
            else
            {
                var error = Truncate(result.Error);
                record.AppendStepResult(phaseName, TenantInitializationStepStatus.Failed,
                    startedAt, completedAt, error);
                await _store.UpdateAsync(record, cancellationToken);

                return new TenantInitializationStep
                {
                    Name = phaseName,
                    Status = TenantInitializationStepStatus.Failed,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    Error = error
                };
            }
        }
        catch (Exception ex)
        {
            var completedAt = DateTime.UtcNow;
            var error = Truncate(ex.Message);
            record.AppendStepResult(phaseName, TenantInitializationStepStatus.Failed,
                startedAt, completedAt, error);
            await _store.UpdateAsync(record, cancellationToken);

            _logger.LogError(ex, "Phase {PhaseName} failed for tenant initialization. CorrelationId: {CorrelationId}",
                phaseName, record.CorrelationId);

            return new TenantInitializationStep
            {
                Name = phaseName,
                Status = TenantInitializationStepStatus.Failed,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                Error = error
            };
        }
    }

    private TenantInitializationResult BuildFailureResult(
        TenantInitializationContext context,
        TenantInitializationRecord record,
        List<TenantInitializationStep> steps,
        string? failedStepError)
    {
        var detailedError = failedStepError ?? "Tenant initialization failed.";
        var publicError = Sanitize(detailedError);
        record.MarkFailed(detailedError);
        return TenantInitializationResult.Failed(context.CorrelationId, publicError, steps);
    }

    private static string Sanitize(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "Tenant initialization failed.";
        var sanitized = error
            .Replace("Data Source=", "[redacted]")
            .Replace("Server=", "[redacted]")
            .Replace("Password=", "[redacted]")
            .Replace("User ID=", "[redacted]");
        return Truncate(sanitized);
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
    }
}
