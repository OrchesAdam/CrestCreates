using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace CrestCreates.Data.EFCore.MySql.DatabaseProviders.MySql;

public class MySqlTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    private readonly ILogger<MySqlTenantDatabaseProvisioner> _logger;
    private static readonly Regex ValidDatabaseNameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public MySqlTenantDatabaseProvisioner(ILogger<MySqlTenantDatabaseProvisioner> logger)
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

        var serverConnectionString = GetServerConnectionString(context.ConnectionString!);

        try
        {
            await using var connection = new MySqlConnection(serverConnectionString);
            await connection.OpenAsync(cancellationToken);

            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @name";
            checkCommand.Parameters.AddWithValue("@name", databaseName);
            var exists = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;

            if (!exists)
            {
                var escapedName = MySqlHelper.EscapeString(databaseName);
                var createCommand = connection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE `{escapedName}`";
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

    private static string GetServerConnectionString(string tenantConnectionString)
    {
        var builder = new MySqlConnectionStringBuilder(tenantConnectionString);
        builder.Database = string.Empty;
        return builder.ConnectionString;
    }
}
