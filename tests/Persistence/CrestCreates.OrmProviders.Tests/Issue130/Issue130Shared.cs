using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using FreeSql.DataAnnotations;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>
/// #130 S1 fixture database: an independent SQLite file per test. Raw verification uses a
/// separate Microsoft.Data.Sqlite connection so neither the EF change tracker nor the SDK
/// caches can satisfy the assertion.
/// </summary>
internal sealed class Issue130SqliteFile : IDisposable
{
    public Issue130SqliteFile()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "crest-issue130-" + Guid.NewGuid().ToString("N") + ".db");
    }

    public string Path { get; }

    public string ConnectionString => "Data Source=" + Path;

    public long Count(string table, string where = "1 = 1")
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE {where};";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public long Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    public Issue130RawRow Read(string table, long id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table}\" WHERE \"Id\" = @id;";
        command.Parameters.AddWithValue("@id", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Issue130RawRow.Missing;
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }

        return new Issue130RawRow(values);
    }

    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var candidate = Path + suffix;
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Delete(candidate);
            }
            catch (IOException)
            {
                // SDK clients may still hold the file on platforms with mandatory file locks;
                // the unique per-test path keeps the leftover harmless.
            }
        }
    }
}

internal sealed class Issue130RawRow
{
    public static readonly Issue130RawRow Missing = new(new Dictionary<string, object?>());

    private readonly IReadOnlyDictionary<string, object?> _values;

    public Issue130RawRow(IReadOnlyDictionary<string, object?> values)
    {
        _values = values;
    }

    public bool Exists => _values.Count > 0;

    public string? Text(string column)
    {
        if (!_values.TryGetValue(column, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            byte[] bytes when bytes.Length == 16 => new Guid(bytes).ToString("D"),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    public long? Integer(string column)
    {
        var text = Text(column);
        return text is null ? null : long.Parse(text, CultureInfo.InvariantCulture);
    }

    public Guid? GuidValue(string column)
    {
        var text = Text(column);
        return text is not null && Guid.TryParse(text, out var parsed) ? parsed : null;
    }

    public DateTime? DateTimeValue(string column)
    {
        var text = Text(column);
        return text is not null &&
               DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}

public sealed class Issue130CurrentUser : CrestCreates.Authorization.Abstractions.ICurrentUser
{
    public string Id { get; set; } = Guid.Empty.ToString("D");

    public string UserName => "issue-130";

    public bool IsAuthenticated => true;

    public string TenantId => string.Empty;

    public string[] Roles => Array.Empty<string>();

    public Guid? OrganizationId => null;

    public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();

    public int DataScopeValue => 0;

    public bool IsSuperAdmin => false;

    public string FindClaimValue(string claimType) => string.Empty;

    public string[] FindClaimValues(string claimType) => Array.Empty<string>();

    public bool IsInRole(string roleName) => false;

    public bool IsInOrganization(Guid orgId) => false;
}

public sealed class Issue130CurrentUserProvider : CrestCreates.Data.Abstractions.ICurrentUserProvider
{
    public Guid? UserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Guid? GetCurrentUserId() => UserId;
}

public sealed class Issue130MutableTenant : ICurrentTenant
{
    public Issue130MutableTenant(string? id)
    {
        Id = id;
    }

    public ITenantInfo? Tenant => null;

    public string? Id { get; set; }

    public Task<IDisposable> ChangeAsync(string tenantId)
    {
        Id = tenantId;
        return Task.FromResult<IDisposable>(new NoopDisposable());
    }

    public IDisposable Change(ITenantInfo? tenant)
    {
        Id = tenant?.Id;
        return new NoopDisposable();
    }

    public void SetTenantId(string tenantId)
    {
        Id = tenantId;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

public static class Issue130Tables
{
    public const string SoftStamped = "S1_SoftStamped";
    public const string SoftOnly = "S1_SoftOnly";
    public const string Plain = "S1_Plain";
    public const string Tenant = "S1_Tenant";
}

/// <summary>Soft-delete entity with concurrency stamp and full audit fields.</summary>
[Table(Name = Issue130Tables.SoftStamped)]
[SugarTable(Issue130Tables.SoftStamped)]
public sealed class Issue130SoftStamped :
    IEntity<long>,
    IAuditedEntity,
    ISoftDelete
{
    [Column(IsIdentity = false, IsPrimary = true)]
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? CreatorId { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastModificationTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? LastModifierId { get; set; }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public bool IsDeleted { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeletionTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? DeleterId { get; set; }

    public Issue130SoftStamped()
    {
    }

    public Issue130SoftStamped(long id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>Soft-delete entity without a concurrency stamp (ordinary delete contract).</summary>
[Table(Name = Issue130Tables.SoftOnly)]
[SugarTable(Issue130Tables.SoftOnly)]
public sealed class Issue130SoftOnly : IEntity<long>, ISoftDelete
{
    [Column(IsIdentity = false, IsPrimary = true)]
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeletionTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? DeleterId { get; set; }

    public Issue130SoftOnly()
    {
    }

    public Issue130SoftOnly(long id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>Non-soft-delete entity: deletes stay physical.</summary>
[Table(Name = Issue130Tables.Plain)]
[SugarTable(Issue130Tables.Plain)]
public sealed class Issue130Plain : IEntity<long>
{
    [Column(IsIdentity = false, IsPrimary = true)]
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Issue130Plain()
    {
    }

    public Issue130Plain(long id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>Mandatory-tenant entity used for detached-write and tenant-scope baselines.</summary>
[Table(Name = Issue130Tables.Tenant)]
[SugarTable(Issue130Tables.Tenant)]
public sealed class Issue130TenantEntity :
    IEntity<long>,
    IAuditedEntity,
    IMustHaveTenant
{
    [Column(IsIdentity = false, IsPrimary = true)]
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? CreatorId { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastModificationTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? LastModifierId { get; set; }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public Issue130TenantEntity()
    {
    }

    public Issue130TenantEntity(long id, string tenantId, string name)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
    }
}
