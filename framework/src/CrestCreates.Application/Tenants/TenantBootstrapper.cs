using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Application.Tenants;

public class TenantBootstrapper : ITenantBootstrapper
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TenantBootstrapOptions _options;
    private readonly ILogger<TenantBootstrapper> _logger;

    public TenantBootstrapper(
        IServiceProvider serviceProvider,
        IOptions<TenantBootstrapOptions> options,
        ILogger<TenantBootstrapper> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task BootstrapAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAutoBootstrap)
        {
            _logger.LogInformation("租户自动初始化已禁用，跳过租户 {TenantName} 的初始化", tenant.Name);
            return;
        }

        _logger.LogInformation("开始初始化租户 {TenantName} (ID: {TenantId})", tenant.Name, tenant.Id);

        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var permissionGrantRepository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenantId = tenant.Id.ToString();

        await BootstrapAdminUserAsync(tenant, userRepository, passwordHasher, cancellationToken);
        await BootstrapDefaultRoleAsync(tenant, roleRepository, cancellationToken);
        await BootstrapBasicPermissionsAsync(tenant, permissionGrantRepository, cancellationToken);

        _logger.LogInformation("租户 {TenantName} 初始化完成", tenant.Name);
    }

    private async Task BootstrapAdminUserAsync(Tenant tenant, IUserRepository userRepository, IPasswordHasher passwordHasher, CancellationToken cancellationToken)
    {
        var adminEmail = string.Format(_options.DefaultAdminEmail, tenant.Name.ToLowerInvariant());
        var hashedPassword = passwordHasher.HashPassword(_options.DefaultAdminPassword);
        var adminUser = new User(Guid.NewGuid(), _options.DefaultAdminUserName, adminEmail, tenant.Id.ToString())
        {
            IsActive = true,
            IsSuperAdmin = true,
            PasswordHash = hashedPassword
        };

        await userRepository.InsertAsync(adminUser, cancellationToken);
        _logger.LogDebug("租户 {TenantName} 创建管理员用户 {UserName}", tenant.Name, adminUser.UserName);
    }

    private async Task BootstrapDefaultRoleAsync(Tenant tenant, IRoleRepository roleRepository, CancellationToken cancellationToken)
    {
        var role = new Role(Guid.NewGuid(), _options.DefaultRoleName, tenant.Id.ToString())
        {
            DisplayName = _options.DefaultRoleDisplayName,
            IsActive = true
        };

        await roleRepository.InsertAsync(role, cancellationToken);
        _logger.LogDebug("租户 {TenantName} 创建默认角色 {RoleName}", tenant.Name, role.Name);
    }

    private async Task BootstrapBasicPermissionsAsync(Tenant tenant, IPermissionGrantRepository permissionGrantRepository, CancellationToken cancellationToken)
    {
        foreach (var permissionName in _options.BasicPermissions)
        {
            var grant = new PermissionGrant(
                Guid.NewGuid(),
                permissionName,
                PermissionGrantProviderType.Role,
                _options.DefaultRoleName,
                PermissionGrantScope.Tenant,
                tenant.Id.ToString());

            await permissionGrantRepository.InsertAsync(grant, cancellationToken);
        }

        _logger.LogDebug("租户 {TenantName} 授予 {Count} 个基础权限", tenant.Name, _options.BasicPermissions.Length);
    }
}
