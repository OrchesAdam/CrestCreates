using CrestCreates.Data.EFCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.EFCore.PostgreSQL.Configuration;

/// <summary>
/// <see cref="IEfCoreDbContextOptionsContributor"/> that configures DbContexts to use Npgsql (PostgreSQL).
/// </summary>
public class NpgsqlDbContextOptionsContributor : IEfCoreDbContextOptionsContributor
{
    private readonly string? _connectionString;

    public NpgsqlDbContextOptionsContributor(string? connectionString = null)
    {
        _connectionString = connectionString;
    }

    public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
    {
        var connectionString = _connectionString
            ?? serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
                .GetConnectionString("Default");

        optionsBuilder.UseNpgsql(connectionString);
    }
}
