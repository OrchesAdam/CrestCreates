using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public class EfCoreTenantDatabaseInitializer : ITenantDatabaseInitializer
    {
        private readonly ILogger<EfCoreTenantDatabaseInitializer> _logger;

        public EfCoreTenantDatabaseInitializer(ILogger<EfCoreTenantDatabaseInitializer> logger)
        {
            _logger = logger;
        }

        public async Task<TenantDatabaseInitializeResult> InitializeAsync(
            TenantInitializationContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(context.ConnectionString);
                var databaseName = builder.InitialCatalog;

                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    _logger.LogWarning(
                        "No database name found in connection string for tenant {TenantId}",
                        context.TenantId);
                    return TenantDatabaseInitializeResult.Failed(
                        "Connection string does not specify a database name.");
                }

                builder.InitialCatalog = "master";

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT 1 FROM sys.databases WHERE name = @name";
                checkCmd.Parameters.AddWithValue("@name", databaseName);
                var exists = await checkCmd.ExecuteScalarAsync(cancellationToken) != null;

                if (!exists)
                {
                    var createCmd = connection.CreateCommand();
                    createCmd.CommandText = $"CREATE DATABASE [{databaseName}]";
                    await createCmd.ExecuteNonQueryAsync(cancellationToken);

                    _logger.LogInformation(
                        "Created database {DatabaseName} for tenant {TenantId}",
                        databaseName, context.TenantId);
                }
                else
                {
                    _logger.LogDebug(
                        "Database {DatabaseName} already exists for tenant {TenantId}",
                        databaseName, context.TenantId);
                }

                return TenantDatabaseInitializeResult.Succeeded();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to ensure database exists for tenant {TenantId}",
                    context.TenantId);
                return TenantDatabaseInitializeResult.Failed(ex.Message);
            }
        }
    }
}
