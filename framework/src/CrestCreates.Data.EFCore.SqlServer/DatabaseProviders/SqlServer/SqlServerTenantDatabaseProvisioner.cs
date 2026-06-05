using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.EFCore.SqlServer.DatabaseProviders.SqlServer;

public class SqlServerTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    private readonly ILogger<SqlServerTenantDatabaseProvisioner> _logger;
    private static readonly Regex ValidDatabaseNameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public SqlServerTenantDatabaseProvisioner(ILogger<SqlServerTenantDatabaseProvisioner> logger)
    {
        _logger = logger;
    }

    public async Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"tenant_{context.TenantId:N}";

        if (!ValidDatabaseNameRegex.IsMatch(databaseName))
        {
            return TenantDatabaseInitializeResult.Failed($"Invalid database name: {databaseName}");
        }

        var masterConnectionString = GetMasterConnectionString(context.ConnectionString!);

        try
        {
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync(cancellationToken);

            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = $"SELECT COUNT(*) FROM sys.databases WHERE name = @name";
            checkCommand.Parameters.AddWithValue("@name", databaseName);
            var exists = (int)(await checkCommand.ExecuteScalarAsync(cancellationToken))! > 0;

            if (!exists)
            {
                var escapedName = databaseName.Replace("]", "]]");
                var createCommand = connection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE [{escapedName}]";
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Created database {DatabaseName} for tenant {TenantId}", databaseName, context.TenantId);
            }

            return TenantDatabaseInitializeResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database for tenant {TenantId}", context.TenantId);
            return TenantDatabaseInitializeResult.Failed(ex.Message);
        }
    }

    private static string GetMasterConnectionString(string tenantConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(tenantConnectionString);
        builder.InitialCatalog = "master";
        return builder.ConnectionString;
    }
}