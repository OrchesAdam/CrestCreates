using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Application.Permissions;

public class PermissionSyncService : IPermissionSyncService
{
    private readonly PermissionSyncOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PermissionSyncService> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;
    private EntityPermissionsManifest? _manifest;

    public PermissionSyncService(
        IOptions<PermissionSyncOptions> options,
        IServiceProvider serviceProvider,
        ILogger<PermissionSyncService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SyncToDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableSync)
        {
            _logger.LogInformation("权限同步已禁用，跳过数据库同步");
            return;
        }

        try
        {
            _logger.LogInformation("开始同步权限到数据库");

            var manifest = await LoadManifestAsync(cancellationToken);
            if (manifest == null)
            {
                _logger.LogWarning("未找到权限清单文件: {ManifestPath}", _options.ManifestPath);
                return;
            }

            var repository = _serviceProvider.GetService(typeof(IPermissionRepository)) as IPermissionRepository;
            if (repository == null)
            {
                _logger.LogError("无法获取 IPermissionRepository 服务");
                return;
            }

            var syncedCount = 0;
            foreach (var entityInfo in manifest.Permissions)
            {
                foreach (var permissionName in entityInfo.Permissions)
                {
                    var existingPermission = await repository.FindByNameAsync(permissionName, cancellationToken);
                    if (existingPermission == null)
                    {
                        var permission = new Permission
                        {
                            Name = permissionName,
                            DisplayName = $"{entityInfo.EntityName}.{permissionName}",
                            GroupName = entityInfo.EntityName,
                            IsEnabled = true
                        };

                        await repository.InsertAsync(permission, cancellationToken);
                        syncedCount++;
                        _logger.LogDebug("已同步权限: {PermissionName}", permissionName);
                    }
                }
            }

            _logger.LogInformation("权限同步完成，共同步 {Count} 个新权限", syncedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步权限到数据库时发生错误");
        }
    }

    public async Task SyncToAuthorizationCenterAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableSync)
        {
            _logger.LogInformation("权限同步已禁用，跳过授权中心同步");
            return;
        }

        if (string.IsNullOrEmpty(_options.AuthorizationCenterUrl))
        {
            _logger.LogWarning("未配置授权中心URL，跳过授权中心同步");
            return;
        }

        if (_httpClientFactory == null)
        {
            _logger.LogError("无法获取 IHttpClientFactory 服务");
            return;
        }

        try
        {
            _logger.LogInformation("开始同步权限到授权中心: {Url}", _options.AuthorizationCenterUrl);

            var manifest = await LoadManifestAsync(cancellationToken);
            if (manifest == null)
            {
                _logger.LogWarning("未找到权限清单文件: {ManifestPath}", _options.ManifestPath);
                return;
            }

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_options.AuthorizationCenterUrl);

            if (!string.IsNullOrEmpty(_options.AuthorizationCenterApiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-Api-Key", _options.AuthorizationCenterApiKey);
            }

            var jsonContent = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("api/permissions/sync", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("权限同步到授权中心成功");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("权限同步到授权中心失败: {StatusCode} - {Error}", response.StatusCode, errorContent);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "连接授权中心失败: {Url}", _options.AuthorizationCenterUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步权限到授权中心时发生错误");
        }
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始执行完整权限同步");

        if (_options.SyncToDatabase)
        {
            await SyncToDatabaseAsync(cancellationToken);
        }

        if (_options.SyncToAuthorizationCenter)
        {
            await SyncToAuthorizationCenterAsync(cancellationToken);
        }

        _logger.LogInformation("完整权限同步执行完成");
    }

    private async Task<EntityPermissionsManifest?> LoadManifestAsync(CancellationToken cancellationToken = default)
    {
        if (_manifest != null)
        {
            return _manifest;
        }

        try
        {
            if (!File.Exists(_options.ManifestPath))
            {
                _logger.LogWarning("权限清单文件不存在: {ManifestPath}", _options.ManifestPath);
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(_options.ManifestPath, cancellationToken);
            _manifest = JsonSerializer.Deserialize<EntityPermissionsManifest>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            return _manifest;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析权限清单文件失败: {ManifestPath}", _options.ManifestPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "读取权限清单文件失败: {ManifestPath}", _options.ManifestPath);
            return null;
        }
    }
}
