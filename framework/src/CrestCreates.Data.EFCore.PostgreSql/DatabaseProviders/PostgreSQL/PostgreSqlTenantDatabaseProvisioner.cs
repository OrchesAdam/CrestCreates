using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CrestCreates.Data.EFCore.PostgreSql.DatabaseProviders.PostgreSQL;

public class PostgreSqlTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    private readonly ILogger<PostgreSqlTenantDatabaseProvisioner> _logger;

    public PostgreSqlTenantDatabaseProvisioner(
        ILogger<PostgreSqlTenantDatabaseProvisioner> logger)
    {
        _logger = logger;
    }

    public async Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(context.ConnectionString);
            var databaseName = builder.Database;

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.LogWarning(
                    "No database name found in connection string for tenant {TenantId}",
                    context.TenantId);
                return TenantDatabaseInitializeResult.Failed(
                    "Connection string does not specify a database name.");
            }

            if (!IsValidDatabaseName(databaseName))
            {
                return TenantDatabaseInitializeResult.Failed(
                    $"Invalid database name '{databaseName}'. Only alphanumeric, underscore, and hyphen characters are allowed.");
            }

            builder.Database = "postgres";

            using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            checkCmd.Parameters.AddWithValue("@name", databaseName);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken) != null;

            if (!exists)
            {
                var escaped = databaseName.Replace("\"", "\"\"");
                var createCmd = connection.CreateCommand();
                createCmd.CommandText = $"CREATE DATABASE \"{escaped}\"";
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
        catch (OperationCanceledException)
        {
            throw;
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

    private static bool IsValidDatabaseName(string name)
    {
        return Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$");
    }
}
