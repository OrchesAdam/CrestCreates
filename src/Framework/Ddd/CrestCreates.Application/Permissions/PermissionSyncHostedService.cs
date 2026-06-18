using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Application.Permissions;

public class PermissionSyncHostedService : IHostedService
{
    private readonly IPermissionSyncService _permissionSyncService;
    private readonly PermissionSyncOptions _options;
    private readonly ILogger<PermissionSyncHostedService> _logger;

    public PermissionSyncHostedService(
        IPermissionSyncService permissionSyncService,
        IOptions<PermissionSyncOptions> options,
        ILogger<PermissionSyncHostedService> logger)
    {
        _permissionSyncService = permissionSyncService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableSync)
        {
            _logger.LogInformation("权限同步已禁用，跳过同步");
            return;
        }

        try
        {
            _logger.LogInformation("开始同步实体权限...");
            await _permissionSyncService.SyncAllAsync(cancellationToken);
            _logger.LogInformation("实体权限同步完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步实体权限时发生错误");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
