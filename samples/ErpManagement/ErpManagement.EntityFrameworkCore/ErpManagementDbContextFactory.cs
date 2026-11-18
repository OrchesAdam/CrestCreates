using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ErpManagement.EntityFrameworkCore;

public class ErpManagementDbContextFactory : IDesignTimeDbContextFactory<ErpManagementDbContext>
{
    public ErpManagementDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../ErpManagement.Web"))
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        var builder = new DbContextOptionsBuilder<ErpManagementDbContext>();
        var connectionString = configuration.GetConnectionString("Default");
        builder.UseNpgsql(connectionString);

        return new ErpManagementDbContext(builder.Options);
    }
}