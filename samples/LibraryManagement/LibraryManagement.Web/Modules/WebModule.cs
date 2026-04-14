using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Modularity;
using LibraryManagement.EntityFrameworkCore;
using LibraryManagement.EntityFrameworkCore.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{ 

    public override void OnApplicationInitialization(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        EnsureSchemaTablesCreated(dbContext);

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

        var role = dbContext.Roles.FirstOrDefault(role => role.Name == roleName && role.TenantId == tenant.Id.ToString());
        if (role == null)
        {
            role = new Role(Guid.NewGuid(), roleName, tenant.Id.ToString())
            {
                DisplayName = roleName,
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };

            dbContext.Roles.Add(role);
        }

        var user = dbContext.Users.FirstOrDefault(user => user.UserName == userName && user.TenantId == tenant.Id.ToString());
        if (user == null)
        {
            user = new User(Guid.NewGuid(), userName, email, tenant.Id.ToString())
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
            dbContext.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenant.Id.ToString()));
        }

        dbContext.SaveChanges();
    }

    private static void EnsureSchemaTablesCreated(LibraryDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
        if (shouldCloseConnection)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = current_schema()
                  AND table_name = 'Tenants';
                """;

            var tenantsTableExists = Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            if (tenantsTableExists)
            {
                return;
            }

            var databaseCreator = dbContext.Database.GetService<IRelationalDatabaseCreator>();
            databaseCreator.CreateTables();
        }
        finally
        {
            if (shouldCloseConnection)
            {
                connection.Close();
            }
        }
    }
}
