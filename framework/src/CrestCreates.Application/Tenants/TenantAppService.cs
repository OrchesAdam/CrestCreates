using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Tenants;

[CrestService]
public class TenantAppService : ITenantAppService
{
    private readonly ITenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantInitializationOrchestrator _orchestrator;
    private readonly ITenantInitializationStore _store;

    public TenantAppService(
        ITenantManager tenantManager,
        ITenantRepository tenantRepository,
        TenantInitializationOrchestrator orchestrator,
        ITenantInitializationStore store)
    {
        _tenantManager = tenantManager;
        _tenantRepository = tenantRepository;
        _orchestrator = orchestrator;
        _store = store;
    }

    public async Task<TenantDto> CreateAsync(
        CreateTenantDto input,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = input.Name.Trim().ToUpperInvariant();
        var existing = await _tenantRepository.FindByNameAsync(normalizedName, cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException($"Tenant '{input.Name}' already exists.");

        var tenant = await _tenantManager.CreateAsync(
            input.Name, input.DisplayName, input.DefaultConnectionString);

        await _tenantRepository.InsertAsync(tenant, cancellationToken);

        var correlationId = Guid.NewGuid().ToString("N");
        var context = new TenantInitializationContext
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            ConnectionString = tenant.GetDefaultConnectionString(),
            CorrelationId = correlationId,
            RequestedByUserId = null
        };

        try
        {
            var result = await _orchestrator.InitializeAsync(context, cancellationToken);

            if (result.Success)
                tenant.MarkInitializationSucceeded();
            else
                tenant.MarkInitializationFailed(result.Error ?? "Initialization failed");

            await _tenantRepository.UpdateAsync(tenant, cancellationToken);
            return MapToDto(tenant);
        }
        catch
        {
            throw;
        }
    }

    public async Task<TenantDto?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(
            NormalizeRequired(name, nameof(name)),
            cancellationToken);

        return tenant == null ? null : MapToDto(tenant);
    }

    public async Task<IReadOnlyList<TenantDto>> GetListAsync(
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantRepository.GetListWithDetailsAsync(cancellationToken);
        return tenants
            .Where(tenant => !isActive.HasValue || tenant.IsActive == isActive.Value)
            .OrderBy(tenant => tenant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto)
            .ToArray();
    }

    public async Task<TenantDto> UpdateAsync(
        string name,
        UpdateTenantDto input,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantManager.UpdateAsync(
            NormalizeRequired(name, nameof(name)),
            input.DisplayName,
            input.DefaultConnectionString,
            cancellationToken);

        return MapToDto(tenant);
    }

    public Task SetActiveAsync(
        string name,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        return _tenantManager.SetActiveAsync(
            NormalizeRequired(name, nameof(name)),
            isActive,
            cancellationToken);
    }

    public async Task<TenantInitializationResult> RetryInitializationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        if (tenant.InitializationStatus != TenantInitializationStatus.Pending &&
            tenant.InitializationStatus != TenantInitializationStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' is in '{tenant.InitializationStatus}' state; only Pending or Failed tenants can retry initialization.");
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var context = new TenantInitializationContext
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            ConnectionString = tenant.GetDefaultConnectionString(),
            CorrelationId = correlationId,
            RequestedByUserId = null
        };

        var result = await _orchestrator.InitializeAsync(context, cancellationToken);

        if (result.Success)
            tenant.MarkInitializationSucceeded();
        else
            tenant.MarkInitializationFailed(result.Error ?? "Initialization failed");

        await _tenantRepository.UpdateAsync(tenant, cancellationToken);
        return result;
    }

    public async Task<TenantInitializationResult> GetInitializationStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        var record = await _store.GetLatestAsync(tenantId, cancellationToken);

        if (record is null)
        {
            return new TenantInitializationResult
            {
                Success = false,
                Status = tenant.InitializationStatus,
                Error = "No initialization record found.",
                CorrelationId = string.Empty,
                Steps = Array.Empty<TenantInitializationStep>()
            };
        }

        var steps = record.GetSteps().Select(s => new TenantInitializationStep
        {
            Name = s.Name,
            Status = s.Status,
            StartedAt = s.StartedAt,
            CompletedAt = s.CompletedAt,
            Error = s.Error
        }).ToArray();

        return new TenantInitializationResult
        {
            Success = record.Status == TenantInitializationStatus.Initialized,
            Status = record.Status,
            Error = record.Error,
            CorrelationId = record.CorrelationId,
            Steps = steps
        };
    }

    public async Task<TenantInitializationResult> ForceRetryInitializationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        if (tenant.InitializationStatus == TenantInitializationStatus.Initialized)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' is already Initialized; cannot force retry.");
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var record = await _store.ForceBeginInitializationAsync(
            tenantId, correlationId, "Admin force retry", cancellationToken);

        if (record is null)
        {
            return TenantInitializationResult.Failed(
                correlationId,
                "Could not force begin initialization. Tenant may be in a conflicting state.",
                Array.Empty<TenantInitializationStep>());
        }

        var context = new TenantInitializationContext
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            ConnectionString = tenant.GetDefaultConnectionString(),
            CorrelationId = correlationId,
            RequestedByUserId = null
        };

        var result = await _orchestrator.InitializeWithRecordAsync(context, record, cancellationToken);

        if (result.Success)
            tenant.MarkInitializationSucceeded();
        else
            tenant.MarkInitializationFailed(result.Error ?? "Force retry initialization failed");

        await _tenantRepository.UpdateAsync(tenant, cancellationToken);
        return result;
    }

    public async Task ForceFailInitializationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        if (tenant.InitializationStatus != TenantInitializationStatus.Initializing)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' is in '{tenant.InitializationStatus}' state; only Initializing tenants can be force-failed.");
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var record = await _store.GetLatestAsync(tenantId, cancellationToken);

        if (record is not null && record.Status == TenantInitializationStatus.Initializing)
        {
            record.MarkFailed("manually marked as failed");
            await _store.UpdateAsync(record, cancellationToken);
        }
        else
        {
            // No active Initializing record — create a recovery record
            var recoveryRecord = await _store.ForceBeginInitializationAsync(
                tenantId, correlationId, "Recovery: force-fail with no active record", cancellationToken);
            if (recoveryRecord is not null)
            {
                recoveryRecord.MarkFailed("manually marked as failed");
                await _store.UpdateAsync(recoveryRecord, cancellationToken);
            }
        }

        tenant.MarkInitializationFailed("manually marked as failed");
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);
    }

    private static TenantDto MapToDto(Tenant tenant)
    {
        return new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            DisplayName = tenant.DisplayName,
            DefaultConnectionString = tenant.GetDefaultConnectionString(),
            IsActive = tenant.IsActive,
            CreationTime = tenant.CreationTime,
            LastModificationTime = tenant.LastModificationTime,
            InitializationStatus = tenant.InitializationStatus,
            InitializedAt = tenant.InitializedAt,
            LastInitializationError = tenant.LastInitializationError
        };
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }
}
