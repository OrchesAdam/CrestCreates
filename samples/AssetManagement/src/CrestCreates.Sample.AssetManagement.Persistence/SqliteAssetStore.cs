using System.Globalization;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace CrestCreates.Sample.AssetManagement.Persistence;

public sealed class SqliteAssetStore : IAssetStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private int _initialized;

    public SqliteAssetStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Assets (
                Id TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                OrganizationId TEXT NULL,
                AssetTag TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Category TEXT NOT NULL,
                Location TEXT NULL,
                Status INTEGER NOT NULL,
                AssignedUserId TEXT NULL,
                ActiveAssignmentId TEXT NULL,
                MaintenanceWorkflowInstanceId TEXT NULL,
                ConcurrencyStamp TEXT NOT NULL,
                CreationTime TEXT NOT NULL,
                LastModificationTime TEXT NULL,
                CreatorId TEXT NULL,
                LastModifierId TEXT NULL,
                PRIMARY KEY (TenantId, Id),
                UNIQUE (TenantId, AssetTag)
            );
            CREATE INDEX IF NOT EXISTS IX_Assets_Tenant_Organization ON Assets (TenantId, OrganizationId, AssetTag, Id);
            CREATE TABLE IF NOT EXISTS AssetAssignments (
                Id TEXT NOT NULL PRIMARY KEY,
                TenantId TEXT NOT NULL,
                AssetId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                AssignedAt TEXT NOT NULL,
                ReturnedAt TEXT NULL,
                FOREIGN KEY (TenantId, AssetId) REFERENCES Assets(TenantId, Id)
            );
            CREATE INDEX IF NOT EXISTS IX_AssetAssignments_Active ON AssetAssignments (TenantId, AssetId, ReturnedAt);
            CREATE TABLE IF NOT EXISTS MaintenanceRecords (
                Id TEXT NOT NULL PRIMARY KEY,
                TenantId TEXT NOT NULL,
                AssetId TEXT NOT NULL,
                OrganizationId TEXT NULL,
                WorkflowInstanceId TEXT NOT NULL,
                RequestedBy TEXT NOT NULL,
                CompletedBy TEXT NOT NULL,
                Note TEXT NOT NULL,
                Approved INTEGER NOT NULL,
                CompletedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId, AssetId) REFERENCES Assets(TenantId, Id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Assets (Id,TenantId,OrganizationId,AssetTag,Name,Description,Category,Location,Status,AssignedUserId,ActiveAssignmentId,MaintenanceWorkflowInstanceId,ConcurrencyStamp,CreationTime,LastModificationTime,CreatorId,LastModifierId)
            VALUES ($id,$tenant,$organization,$tag,$name,$description,$category,$location,$status,$assigned,$assignment,$workflow,$stamp,$created,$modified,$creator,$modifier)
            """;
        AddAssetParameters(command, asset);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Asset?> GetAsync(string tenantId, Guid assetId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM Assets WHERE TenantId=$tenant AND Id=$id LIMIT 1";
        command.Parameters.AddWithValue("$tenant", tenantId);
        command.Parameters.AddWithValue("$id", assetId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    public async Task<IReadOnlyList<Asset>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM Assets WHERE TenantId=$tenant ORDER BY AssetTag COLLATE BINARY, Id";
        command.Parameters.AddWithValue("$tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<Asset>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadAsset(reader));
        return result;
    }

    public async Task UpdateAsync(Asset asset, string expectedConcurrencyStamp, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = UpdateSql();
        AddAssetParameters(command, asset);
        command.Parameters.AddWithValue("$expected", expectedConcurrencyStamp);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Asset '{asset.Id}' was changed or is outside the current tenant.");
    }

    public async Task SaveAssignmentAsync(Asset asset, string expectedConcurrencyStamp, AssetAssignment assignment, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var transaction = _connection.BeginTransaction();
        await using var update = _connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = UpdateSql();
        AddAssetParameters(update, asset);
        update.Parameters.AddWithValue("$expected", expectedConcurrencyStamp);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Asset '{asset.Id}' was changed before assignment.");
        await using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO AssetAssignments (Id,TenantId,AssetId,UserId,OrganizationId,AssignedAt) VALUES ($id,$tenant,$asset,$user,$org,$at)";
        insert.Parameters.AddWithValue("$id", assignment.Id.ToString("D"));
        insert.Parameters.AddWithValue("$tenant", assignment.TenantId);
        insert.Parameters.AddWithValue("$asset", assignment.AssetId.ToString("D"));
        insert.Parameters.AddWithValue("$user", assignment.UserId);
        insert.Parameters.AddWithValue("$org", assignment.OrganizationId?.ToString() ?? string.Empty);
        insert.Parameters.AddWithValue("$at", Format(assignment.AssignedAt));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveReturnAsync(Asset asset, string expectedConcurrencyStamp, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var transaction = _connection.BeginTransaction();
        await using var update = _connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = UpdateSql();
        AddAssetParameters(update, asset);
        update.Parameters.AddWithValue("$expected", expectedConcurrencyStamp);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Asset '{asset.Id}' was changed before return.");
        await using var assignment = _connection.CreateCommand();
        assignment.Transaction = transaction;
        assignment.CommandText = "UPDATE AssetAssignments SET ReturnedAt=$returned WHERE TenantId=$tenant AND AssetId=$asset AND Id=$id AND ReturnedAt IS NULL";
        assignment.Parameters.AddWithValue("$returned", Format(DateTime.UtcNow));
        assignment.Parameters.AddWithValue("$tenant", asset.TenantId);
        assignment.Parameters.AddWithValue("$asset", asset.Id.ToString("D"));
        assignment.Parameters.AddWithValue("$id", assignmentId.ToString("D"));
        if (await assignment.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Active assignment '{assignmentId}' was not found.");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveMaintenanceDecisionAsync(Asset asset, string expectedConcurrencyStamp, MaintenanceRecord record, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var transaction = _connection.BeginTransaction();
        await using var update = _connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = UpdateSql();
        AddAssetParameters(update, asset);
        update.Parameters.AddWithValue("$expected", expectedConcurrencyStamp);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Asset '{asset.Id}' was changed before maintenance decision.");
        await using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO MaintenanceRecords (Id,TenantId,AssetId,OrganizationId,WorkflowInstanceId,RequestedBy,CompletedBy,Note,Approved,CompletedAt) VALUES ($id,$tenant,$asset,$org,$workflow,$requested,$completed,$note,$approved,$at)";
        insert.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        insert.Parameters.AddWithValue("$tenant", record.TenantId);
        insert.Parameters.AddWithValue("$asset", record.AssetId.ToString("D"));
        insert.Parameters.AddWithValue("$org", record.OrganizationId?.ToString() ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$workflow", record.WorkflowInstanceId);
        insert.Parameters.AddWithValue("$requested", record.RequestedBy);
        insert.Parameters.AddWithValue("$completed", record.CompletedBy);
        insert.Parameters.AddWithValue("$note", record.Note);
        insert.Parameters.AddWithValue("$approved", record.Approved ? 1 : 0);
        insert.Parameters.AddWithValue("$at", Format(record.CompletedAt));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public void Dispose() => _connection.Dispose();

    private static string UpdateSql() => """
        UPDATE Assets SET OrganizationId=$organization,AssetTag=$tag,Name=$name,Description=$description,Category=$category,Location=$location,Status=$status,AssignedUserId=$assigned,ActiveAssignmentId=$assignment,MaintenanceWorkflowInstanceId=$workflow,ConcurrencyStamp=$stamp,LastModificationTime=$modified,LastModifierId=$modifier
        WHERE TenantId=$tenant AND Id=$id AND ConcurrencyStamp=$expected
        """;

    private static void AddAssetParameters(SqliteCommand command, Asset asset)
    {
        command.Parameters.AddWithValue("$id", asset.Id.ToString("D"));
        command.Parameters.AddWithValue("$tenant", asset.TenantId);
        command.Parameters.AddWithValue("$organization", asset.OrganizationId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$tag", asset.AssetTag);
        command.Parameters.AddWithValue("$name", asset.Name);
        command.Parameters.AddWithValue("$description", asset.Description);
        command.Parameters.AddWithValue("$category", asset.Category);
        command.Parameters.AddWithValue("$location", asset.Location ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)asset.Status);
        command.Parameters.AddWithValue("$assigned", asset.AssignedUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$assignment", asset.ActiveAssignmentId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$workflow", asset.MaintenanceWorkflowInstanceId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$stamp", asset.ConcurrencyStamp);
        command.Parameters.AddWithValue("$created", Format(asset.CreationTime));
        command.Parameters.AddWithValue("$modified", asset.LastModificationTime is { } modified ? Format(modified) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$creator", asset.CreatorId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$modifier", asset.LastModifierId?.ToString("D") ?? (object)DBNull.Value);
    }

    private static Asset ReadAsset(SqliteDataReader reader)
        => Asset.Rehydrate(
            Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            reader.GetString(reader.GetOrdinal("TenantId")),
            reader.GetString(reader.GetOrdinal("AssetTag")),
            reader.GetString(reader.GetOrdinal("Name")),
            reader.GetString(reader.GetOrdinal("Description")),
            reader.GetString(reader.GetOrdinal("Category")),
            ReadGuid(reader, "OrganizationId"),
            ReadString(reader, "Location"),
            (AssetStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            ReadString(reader, "AssignedUserId"),
            ReadGuid(reader, "ActiveAssignmentId"),
            ReadString(reader, "MaintenanceWorkflowInstanceId"),
            reader.GetString(reader.GetOrdinal("ConcurrencyStamp")),
            ParseTime(reader.GetString(reader.GetOrdinal("CreationTime"))),
            ReadTime(reader, "LastModificationTime"),
            ReadGuid(reader, "CreatorId"),
            ReadGuid(reader, "LastModifierId"));

    private static Guid? ReadGuid(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : Guid.Parse(reader.GetString(reader.GetOrdinal(name)));
    private static string? ReadString(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
    private static DateTime? ReadTime(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : ParseTime(reader.GetString(reader.GetOrdinal(name)));
    private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTime ParseTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
