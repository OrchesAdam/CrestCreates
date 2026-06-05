using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Security.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.EFCore.DataSeed;

/// <summary>
/// Seeds host-level identity data at application startup.
/// Creates the host tenant, admin role, admin user, and user-role link.
/// All operations are idempotent — safe to run on every startup.
/// </summary>
public class HostIdentityDataSeeder : IDataSeeder
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HostIdentityDataSeeder> _logger;

    public HostIdentityDataSeeder(
        DbContext dbContext,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<HostIdentityDataSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _configuration["SeedIdentity:TenantId"] ?? "host";
        var roleName = _configuration["SeedIdentity:RoleName"] ?? "Administrators";
        var userName = _configuration["SeedIdentity:UserName"] ?? "admin";
        var email = _configuration["SeedIdentity:Email"] ?? "admin@localhost.local";
        var password = _configuration["SeedIdentity:Password"] ?? "Admin123!";
        var defaultConnectionString = _configuration.GetConnectionString("Default");

        _logger.LogInformation("Seeding host identity: Tenant={TenantId}, Role={RoleName}, User={UserName}",
            tenantId, roleName, userName);

        // Seed host tenant
        var tenant = await _dbContext.Set<Tenant>()
            .FirstOrDefaultAsync(t => t.Name == tenantId, cancellationToken);
        if (tenant == null)
        {
            tenant = new Tenant(Guid.NewGuid(), tenantId)
            {
                DisplayName = _configuration["SeedIdentity:TenantId"] ?? tenantId,
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };
            _dbContext.Set<Tenant>().Add(tenant);
            _logger.LogInformation("Created host tenant: {TenantId}", tenantId);
        }
        else
        {
            tenant.DisplayName ??= _configuration["SeedIdentity:TenantId"] ?? tenantId;
        }

        // Seed default connection string for host tenant
        if (!string.IsNullOrWhiteSpace(defaultConnectionString))
        {
            var existingConnStr = await _dbContext.Set<TenantConnectionString>()
                .FirstOrDefaultAsync(cs =>
                    cs.TenantId == tenant.Id &&
                    cs.Name == TenantConnectionString.DefaultName, cancellationToken);
            if (existingConnStr == null)
            {
                _dbContext.Set<TenantConnectionString>().Add(
                    new TenantConnectionString(
                        Guid.NewGuid(),
                        tenant.Id,
                        TenantConnectionString.DefaultName,
                        defaultConnectionString));
            }
            else
            {
                existingConnStr.SetValue(defaultConnectionString);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Seed admin role
        var role = await _dbContext.Set<Role>()
            .FirstOrDefaultAsync(r => r.Name == roleName && r.TenantId == tenant.Id.ToString(), cancellationToken);
        if (role == null)
        {
            role = new Role(Guid.NewGuid(), roleName, tenant.Id.ToString())
            {
                DisplayName = roleName,
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };
            _dbContext.Set<Role>().Add(role);
            _logger.LogInformation("Created role: {RoleName}", roleName);
        }

        // Seed admin user
        var user = await _dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.UserName == userName && u.TenantId == tenant.Id.ToString(), cancellationToken);
        if (user == null)
        {
            user = new User(Guid.NewGuid(), userName, email, tenant.Id.ToString())
            {
                PasswordHash = _passwordHasher.HashPassword(password),
                IsActive = true,
                IsSuperAdmin = true,
                LockoutEnabled = true,
                CreationTime = DateTime.UtcNow,
                LastPasswordChangeTime = DateTime.UtcNow
            };
            _dbContext.Set<User>().Add(user);
            _logger.LogInformation("Created admin user: {UserName}", userName);
        }

        // Seed user-role link
        var userRole = await _dbContext.Set<UserRole>()
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, cancellationToken);
        if (userRole == null)
        {
            _dbContext.Set<UserRole>().Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenant.Id.ToString()));
            _logger.LogInformation("Linked user {UserName} to role {RoleName}", userName, roleName);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Host identity seeding completed.");
    }
}