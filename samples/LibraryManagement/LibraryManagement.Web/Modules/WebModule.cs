using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Modularity;
using LibraryManagement.EntityFrameworkCore;
using LibraryManagement.EntityFrameworkCore.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{ 

    public override void OnApplicationInitialization(IHost host)
    {
        // 确保数据库已创建
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        dbContext.Database.EnsureCreated();

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenantId = configuration["SeedIdentity:TenantId"] ?? "host";
        var roleName = configuration["SeedIdentity:RoleName"] ?? "Administrators";
        var userName = configuration["SeedIdentity:UserName"] ?? "admin";
        var email = configuration["SeedIdentity:Email"] ?? "admin@library.local";
        var password = configuration["SeedIdentity:Password"] ?? "Admin123!";
        var defaultConnectionString = configuration.GetConnectionString("Default");

        var tenant = dbContext.Tenants.FirstOrDefault(item => item.Name == tenantId);
        if (tenant == null)
        {
            tenant = new Tenant(Guid.NewGuid(), tenantId)
            {
                DisplayName = configuration["SeedTenant:DisplayName"] ?? tenantId,
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };
            dbContext.Tenants.Add(tenant);
        }
        else
        {
            tenant.DisplayName ??= configuration["SeedTenant:DisplayName"] ?? tenantId;
        }

        var defaultTenantConnectionString = dbContext.TenantConnectionStrings
            .FirstOrDefault(item =>
                item.TenantId == tenant.Id &&
                item.Name == TenantConnectionString.DefaultName);
        if (defaultTenantConnectionString == null)
        {
            if (!string.IsNullOrWhiteSpace(defaultConnectionString))
            {
                dbContext.TenantConnectionStrings.Add(
                    new TenantConnectionString(
                        Guid.NewGuid(),
                        tenant.Id,
                        TenantConnectionString.DefaultName,
                        defaultConnectionString));
            }
        }
        else if (!string.IsNullOrWhiteSpace(defaultConnectionString))
        {
            defaultTenantConnectionString.SetValue(defaultConnectionString);
        }

        var role = dbContext.Roles.FirstOrDefault(role => role.Name == roleName && role.TenantId == tenantId);
        if (role == null)
        {
            role = new Role(Guid.NewGuid(), roleName, tenantId)
            {
                DisplayName = roleName,
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };

            dbContext.Roles.Add(role);
        }

        var user = dbContext.Users.FirstOrDefault(user => user.UserName == userName && user.TenantId == tenantId);
        if (user == null)
        {
            user = new User(Guid.NewGuid(), userName, email, tenantId)
            {
                PasswordHash = passwordHasher.HashPassword(password),
                IsActive = true,
                IsSuperAdmin = true,
                LockoutEnabled = true,
                CreationTime = DateTime.UtcNow,
                LastPasswordChangeTime = DateTime.UtcNow
            };

            dbContext.Users.Add(user);
        }

        var userRole = dbContext.UserRoles.FirstOrDefault(link => link.UserId == user.Id && link.RoleId == role.Id);
        if (userRole == null)
        {
            dbContext.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenantId));
        }

        dbContext.SaveChanges();
    }
}
