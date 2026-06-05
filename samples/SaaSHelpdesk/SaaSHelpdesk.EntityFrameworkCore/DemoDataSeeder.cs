using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.EntityFrameworkCore;

/// <summary>
/// Seeds demo data (Categories, Customers, Tickets) for the SaaSHelpdesk sample.
/// </summary>
public class DemoDataSeeder : IDataSeeder
{
    private readonly HelpdeskDbContext _dbContext;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(HelpdeskDbContext dbContext, ILogger<DemoDataSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Seed Default Categories
        if (!await _dbContext.Set<Category>().AnyAsync(cancellationToken))
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
            _dbContext.Set<Category>().AddRange(categories);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} demo categories", categories.Length);
        }

        // Seed demo customers and tickets
        if (!await _dbContext.Set<Customer>().AnyAsync(cancellationToken))
        {
            var tenantId = (await _dbContext.Tenants.FirstAsync(cancellationToken)).Id;

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
            _dbContext.Set<Customer>().AddRange(customers);

            var generalCategory = _dbContext.Set<Category>().First();
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
            _dbContext.Set<Ticket>().AddRange(tickets);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} demo customers and {TicketCount} demo tickets",
                customers.Length, tickets.Length);
        }
    }
}