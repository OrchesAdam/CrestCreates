using System.Data;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;
using SaaSHelpdesk.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore.Modules;

namespace SaaSHelpdesk.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{
    public override void OnApplicationInitialization(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        EnsureSchemaTablesCreated(dbContext);

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenantId = configuration["SeedIdentity:TenantId"] ?? "host";
        var roleName = configuration["SeedIdentity:RoleName"] ?? "Administrators";
        var userName = configuration["SeedIdentity:UserName"] ?? "admin";
        var email = configuration["SeedIdentity:Email"] ?? "admin@helpdesk.local";
        var password = configuration["SeedIdentity:Password"] ?? "Admin123!";
        var defaultConnectionString = configuration.GetConnectionString("Default");

        // Seed Tenant
        var tenant = dbContext.Tenants.FirstOrDefault(item => item.Name == tenantId);
        if (tenant == null)
        {
            tenant = new Tenant(Guid.NewGuid(), tenantId)
            {
                DisplayName = configuration["SeedIdentity:TenantId"] ?? tenantId,
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };
            dbContext.Tenants.Add(tenant);
        }
        else
        {
            tenant.DisplayName ??= configuration["SeedIdentity:TenantId"] ?? tenantId;
        }

        // Seed TenantConnectionString
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

        // Seed Role
        var role = dbContext.Roles.FirstOrDefault(r => r.Name == roleName && r.TenantId == tenant.Id.ToString());
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

        // Seed Admin User
        var user = dbContext.Users.FirstOrDefault(u => u.UserName == userName && u.TenantId == tenant.Id.ToString());
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

        // Seed UserRole
        var userRole = dbContext.UserRoles.FirstOrDefault(link => link.UserId == user.Id && link.RoleId == role.Id);
        if (userRole == null)
        {
            dbContext.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenant.Id.ToString()));
        }

        // Seed Default Categories
        if (!dbContext.Set<Category>().Any())
        {
            var categories = new[]
            {
                new Category(Guid.NewGuid(), "General", 1),
                new Category(Guid.NewGuid(), "Technical Support", 2),
                new Category(Guid.NewGuid(), "Billing", 3),
                new Category(Guid.NewGuid(), "Feature Requests", 4),
                new Category(Guid.NewGuid(), "Bug Reports", 5),
                new Category(Guid.NewGuid(), "Account Management", 6),
            };
            dbContext.Set<Category>().AddRange(categories);
        }

        dbContext.SaveChanges();

        // Seed demo customers
        if (!dbContext.Set<Customer>().Any())
        {
            var customers = new Customer[3];
            customers[0] = new Customer(Guid.NewGuid(), "Alice Johnson", "alice@example.com", tenant.Id);
            customers[0].SetPhone("+1-555-0101");
            customers[0].SetCompany("Acme Corp");
            customers[1] = new Customer(Guid.NewGuid(), "Bob Smith", "bob@company.com", tenant.Id);
            customers[1].SetPhone("+1-555-0102");
            customers[1].SetCompany("TechStart Inc");
            customers[2] = new Customer(Guid.NewGuid(), "Carol White", "carol@client.org", tenant.Id);
            customers[2].SetPhone("+1-555-0103");
            customers[2].SetCompany("ClientOrg LLC");
            dbContext.Set<Customer>().AddRange(customers);

            // Seed demo tickets
            var generalCategory = dbContext.Set<Category>().First();
            var alice = customers[0];
            var bob = customers[1];
            var carol = customers[2];

            var tickets = new[]
            {
                new Ticket(Guid.NewGuid(), "Cannot login to portal", "Getting error 'Invalid credentials' when trying to login with correct password.", TicketPriority.High, TicketType.Incident, alice.Id),
                new Ticket(Guid.NewGuid(), "How to reset password?", "I forgot my password and need instructions to reset it.", TicketPriority.Low, TicketType.Question, alice.Id),
                new Ticket(Guid.NewGuid(), "Billing overcharge on invoice #4521", "Invoice shows $500 but we agreed on $400 monthly rate.", TicketPriority.High, TicketType.Problem, bob.Id),
                new Ticket(Guid.NewGuid(), "Feature request: Dark mode", "Would love to see a dark mode option for the dashboard.", TicketPriority.Medium, TicketType.FeatureRequest, carol.Id),
                new Ticket(Guid.NewGuid(), "Printer not working with new update", "After the latest update, our network printer stopped working with the system.", TicketPriority.Medium, TicketType.Incident, bob.Id),
            };
            dbContext.Set<Ticket>().AddRange(tickets);
        }

        dbContext.SaveChanges();
    }

    private static void EnsureSchemaTablesCreated(HelpdeskDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
            connection.Open();

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
                return;

            var databaseCreator = dbContext.Database.GetService<IRelationalDatabaseCreator>();
            databaseCreator.CreateTables();
        }
        finally
        {
            if (shouldCloseConnection)
                connection.Close();
        }
    }
}
