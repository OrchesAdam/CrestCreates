using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SaaSHelpdesk.EntityFrameworkCore;

public class HelpdeskDbContextFactory : IDesignTimeDbContextFactory<HelpdeskDbContext>
{
    public HelpdeskDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../SaaSHelpdesk.Web"))
            .AddJsonFile("appsettings.json")
            .Build();

        var builder = new DbContextOptionsBuilder<HelpdeskDbContext>();
        var connectionString = configuration.GetConnectionString("Default");
        builder.UseNpgsql(connectionString);

        return new HelpdeskDbContext(builder.Options);
    }
}
