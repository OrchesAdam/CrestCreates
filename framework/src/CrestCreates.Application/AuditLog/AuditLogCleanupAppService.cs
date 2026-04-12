using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.AuditLog;

/// <summary>
/// 审计日志清理应用服务
/// </summary>
[CrestService]
public class AuditLogCleanupAppService : IAuditLogCleanupAppService
{
    private readonly IAuditLogRepository _repository;
    private readonly ISettingProvider _settingProvider;

    private const string RetentionDaysSettingName = "AuditLogging.RetentionDays";
    private const int DefaultRetentionDays = 90;

    public AuditLogCleanupAppService(
        IAuditLogRepository repository,
        ISettingProvider settingProvider)
    {
        _repository = repository;
        _settingProvider = settingProvider;
    }

    public async Task<CleanupAuditLogsResultDto> ProcessCleanupAsync(
        CleanupAuditLogsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        DateTime beforeTime;

        if (request.BeforeTime.HasValue)
        {
            beforeTime = request.BeforeTime.Value;
        }
        else if (request.RetentionDays.HasValue)
        {
            beforeTime = DateTime.UtcNow.AddDays(-request.RetentionDays.Value);
        }
        else
        {
            // 从 Setting Management 读取保留天数默认值
            var retentionDays = await GetRetentionDaysAsync(cancellationToken);
            beforeTime = DateTime.UtcNow.AddDays(-retentionDays);
        }

        var deletedCount = await _repository.DeleteOlderThanAsync(beforeTime, cancellationToken);

        return new CleanupAuditLogsResultDto
        {
            DeletedCount = deletedCount
        };
    }

    private async Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken)
    {
        var settingValue = await _settingProvider.GetOrNullAsync(RetentionDaysSettingName, cancellationToken);
        if (!string.IsNullOrEmpty(settingValue) && int.TryParse(settingValue, out var retentionDays))
        {
            return retentionDays;
        }
        return DefaultRetentionDays;
    }
}
