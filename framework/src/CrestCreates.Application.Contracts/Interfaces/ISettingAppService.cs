using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ISettingAppService
{
    Task<IReadOnlyList<SettingValueDto>> GetCurrentValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetGlobalValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetCurrentTenantValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetTenantValuesAsync(
        string tenantId,
        string? groupName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetCurrentUserValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetUserValuesAsync(
        string userId,
        string? tenantId = null,
        string? groupName = null,
        CancellationToken cancellationToken = default);

    Task UpdateGlobalAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    Task UpdateCurrentTenantAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    Task UpdateTenantAsync(string tenantId, string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    Task UpdateCurrentUserAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    Task UpdateUserAsync(
        string userId,
        string name,
        UpdateSettingValueDto input,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task DeleteGlobalAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteCurrentTenantAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteTenantAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    Task DeleteCurrentUserAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteUserAsync(
        string userId,
        string name,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
