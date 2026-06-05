using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Modularity;
using Microsoft.EntityFrameworkCore;
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
        // Run framework host migration + identity seeding
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        runner.RunAsync(host.Services).GetAwaiter().GetResult();

        // Run application-specific demo data seeding
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var seeders = scope.ServiceProvider.GetServices<IDataSeeder>();
        foreach (var seeder in seeders)
        {
            seeder.SeedAsync().GetAwaiter().GetResult();
        }

        SeedDemoData(dbContext);
    }

    private static void SeedDemoData(HelpdeskDbContext dbContext)
    {
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
            dbContext.SaveChanges();
        }

        // Seed demo customers and tickets
        var tenantId = dbContext.Tenants.First().Id;
        if (!dbContext.Set<Customer>().Any())
        {
            var customers = new Customer[3];
            customers[0] = new Customer(Guid.NewGuid(), "Alice Johnson", "alice@example.com", tenantId);
            customers[0].SetPhone("+1-555-0101");
            customers[0].SetCompany("Acme Corp");
            customers[1] = new Customer(Guid.NewGuid(), "Bob Smith", "bob@company.com", tenantId);
            customers[1].SetPhone("+1-555-0102");
            customers[1].SetCompany("TechStart Inc");
            customers[2] = new Customer(Guid.NewGuid(), "Carol White", "carol@client.org", tenantId);
            customers[2].SetPhone("+1-555-0103");
            customers[2].SetCompany("ClientOrg LLC");
            dbContext.Set<Customer>().AddRange(customers);

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
            dbContext.SaveChanges();
        }
    }
}