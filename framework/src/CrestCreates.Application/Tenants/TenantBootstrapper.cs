using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Authorization;
using CrestCreates.MultiTenancy;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Application.Tenants;

public class TenantBootstrapper : ITenantDataSeeder
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

    public async Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAutoBootstrap)
            return TenantSeedResult.Succeeded();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            await BootstrapAdminUserAsync(scope, context, cancellationToken);
            await BootstrapDefaultRoleAsync(scope, context, cancellationToken);
            await BootstrapBasicPermissionsAsync(scope, context, cancellationToken);

            return TenantSeedResult.Succeeded();
        }
        catch (Exception ex)
        {
            return TenantSeedResult.Failed(ex.Message);
        }
    }

    private async Task BootstrapAdminUserAsync(IServiceScope scope, TenantInitializationContext context, CancellationToken cancellationToken)
    {
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var adminEmail = string.Format(_options.DefaultAdminEmail, context.TenantName.ToLowerInvariant());
        var hashedPassword = passwordHasher.HashPassword(_options.DefaultAdminPassword);
        var adminUser = new User(Guid.NewGuid(), _options.DefaultAdminUserName, adminEmail, context.TenantId.ToString())
        {
            IsActive = true,
            IsSuperAdmin = true,
            PasswordHash = hashedPassword
        };

        await userRepository.InsertAsync(adminUser, cancellationToken);
        _logger.LogDebug("租户 {TenantName} 创建管理员用户 {UserName}", context.TenantName, adminUser.UserName);
    }

    private async Task BootstrapDefaultRoleAsync(IServiceScope scope, TenantInitializationContext context, CancellationToken cancellationToken)
    {
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var role = new Role(Guid.NewGuid(), _options.DefaultRoleName, context.TenantId.ToString())
        {
            DisplayName = _options.DefaultRoleDisplayName,
            IsActive = true
        };

        await roleRepository.InsertAsync(role, cancellationToken);
        _logger.LogDebug("租户 {TenantName} 创建默认角色 {RoleName}", context.TenantName, role.Name);
    }

    private async Task BootstrapBasicPermissionsAsync(IServiceScope scope, TenantInitializationContext context, CancellationToken cancellationToken)
    {
        var permissionGrantRepository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        foreach (var permissionName in _options.BasicPermissions)
        {
            var grant = new PermissionGrant(
                Guid.NewGuid(),
                permissionName,
                PermissionGrantProviderType.Role,
                _options.DefaultRoleName,
                PermissionGrantScope.Tenant,
                context.TenantId.ToString());

            await permissionGrantRepository.InsertAsync(grant, cancellationToken);
        }

        _logger.LogDebug("租户 {TenantName} 授予 {Count} 个基础权限", context.TenantName, _options.BasicPermissions.Length);
    }
}
