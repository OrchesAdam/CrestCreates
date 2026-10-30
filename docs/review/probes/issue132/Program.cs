using System.Data.Common;
using System.Text.Json;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Settings;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Characterization of the existing cleanup, not a retention implementation or a hold protocol.
var cutoff = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
var results = new List<object>();
await Case("C01_expired_recent_exact_cutoff", "existing-behavior", async bed =>
{
    await bed.Seed("old", "tenant-a", cutoff.AddTicks(-1));
    await bed.Seed("recent", "tenant-a", cutoff.AddDays(1));
    await bed.Seed("boundary", "tenant-a", cutoff);
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(result.DeletedCount == 1 && await bed.Names() is ["boundary", "recent"]);
    return new { result.DeletedCount, remaining = await bed.Names() };
});
await Case("C02_tenant_does_not_delete_other_scope", "existing-behavior", async bed =>
{
    await bed.Seed("a", "tenant-a", cutoff.AddDays(-1));
    await bed.Seed("b", "tenant-b", cutoff.AddDays(-1));
    await bed.Seed("host", null, cutoff.AddDays(-1));
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(result.DeletedCount == 1 && await bed.Names() is ["b", "host"]);
    return new { result.DeletedCount, remaining = await bed.Names() };
});
await Case("C03_null_host_implicitly_deletes_all_tenants", "gap-reproduced", async bed =>
{
    bed.Tenant.Id = null;
    await bed.Seed("a", "tenant-a", cutoff.AddDays(-1));
    await bed.Seed("b", "tenant-b", cutoff.AddDays(-1));
    await bed.Seed("host", null, cutoff.AddDays(-1));
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(result.DeletedCount == 3 && (await bed.Names()).Length == 0);
    return new { result.DeletedCount, explicitAllTenantAuthorization = false };
});
await Case("C04_protection_marker_is_ignored", "gap-reproduced", async bed =>
{
    await bed.Seed("held", "tenant-a", cutoff.AddDays(-1), "hold");
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(result.DeletedCount == 1 && (await bed.Names()).Length == 0);
    return new { result.DeletedCount, nativeHoldContract = false, marker = "fixture-only ExtraProperties protection hint" };
});
await Case("C05_future_cutoff_deletes_recent_record", "gap-reproduced", async bed =>
{
    await bed.Seed("recent", "tenant-a", DateTime.UtcNow);
    var result = await bed.Cleanup(new() { BeforeTime = DateTime.UtcNow.AddDays(2) });
    Check(result.DeletedCount == 1);
    return new { result.DeletedCount };
});
await Case("C06_negative_retention_is_accepted", "gap-reproduced", async bed =>
{
    await bed.Seed("recent", "tenant-a", DateTime.UtcNow);
    var result = await bed.Cleanup(new() { RetentionDays = -1 });
    Check(result.DeletedCount == 1);
    return new { result.DeletedCount };
});
await Case("C07_unbounded_batch", "gap-reproduced", async bed =>
{
    for (var i = 0; i < 1101; i++)
        bed.Context.Add(Bed.Row($"old-{i}", "tenant-a", cutoff.AddDays(-1)));
    await bed.Context.SaveChangesAsync();
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(result.DeletedCount == 1101);
    return new { result.DeletedCount, requestedBatchLimit = (int?)null };
});
await Case("C08_cleanup_evidence_marker_is_deleted", "gap-reproduced", async bed =>
{
    await bed.Seed("cleanup-receipt", "tenant-a", cutoff.AddDays(-1), "cleanup-evidence");
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(result.DeletedCount == 1 && (await bed.Names()).Length == 0);
    return new { result.DeletedCount, marker = "fixture-only cleanup evidence hint; no existing receipt store" };
});
await Case("C09_precancel_leaves_rows", "existing-behavior", async bed =>
{
    await bed.Seed("old", "tenant-a", cutoff.AddDays(-1));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { await bed.Cleanup(new() { BeforeTime = cutoff }, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
    Check(await bed.Names() is ["old"]);
    return new { remaining = await bed.Names() };
});
await Case("C10_count_delete_race_overreports", "gap-reproduced", async bed =>
{
    await bed.Seed("old", "tenant-a", cutoff.AddDays(-1));
    bed.Race.Enabled = true;
    var result = await bed.Cleanup(new() { BeforeTime = cutoff });
    Check(bed.Race.CompetingDeletes == 1 && bed.Race.CleanupActualDeletes == 0 && result.DeletedCount == 1 && (await bed.Names()).Length == 0);
    return new { result.DeletedCount, competingDeletes = bed.Race.CompetingDeletes, cleanupCommandActualDeletes = bed.Race.CleanupActualDeletes };
});
Console.WriteLine(JsonSerializer.Serialize(new { cases = results, sentinel = "CRESTCREATES_ISSUE132_GAPS_REPRODUCED" }, new JsonSerializerOptions { WriteIndented = true }));

async Task Case(string id, string classification, Func<Bed, Task<object>> run)
{
    await using var bed = new Bed();
    await bed.Context.Database.EnsureCreatedAsync();
    results.Add(new { id, classification, observation = await run(bed) });
}
static void Check(bool condition) { if (!condition) throw new Exception("Baseline observation changed; review this characterization."); }

sealed class Bed : IAsyncDisposable
{
    public MutableTenant Tenant { get; } = new();
    public DeleteRace Race { get; } = new();
    public ProbeContext Context { get; }
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public Bed()
    {
        _connection.Open();
        Context = new(new DbContextOptionsBuilder<ProbeContext>().UseSqlite(_connection).AddInterceptors(Race).Options);
    }
    public static AuditLog Row(string name, string? tenant, DateTime time) => new(Guid.NewGuid())
    { ServiceName = name, TenantId = tenant, ExecutionTime = time, CreationTime = time };
    public async Task Seed(string name, string? tenant, DateTime time, string? marker = null)
    {
        var row = Row(name, tenant, time);
        if (marker != null) row.ExtraProperties["ProtectionHint"] = marker;
        Context.Add(row);
        await Context.SaveChangesAsync();
    }
    public Task<string?[]> Names() => Context.Set<AuditLog>().AsNoTracking().OrderBy(x => x.ServiceName).Select(x => x.ServiceName).ToArrayAsync();
    public Task<CleanupAuditLogsResultDto> Cleanup(CleanupAuditLogsRequestDto request, CancellationToken ct = default) =>
        new AuditLogCleanupAppService(new EfCoreAuditLogRepository(new EfCoreDbContextAdapter(Context), Tenant), new FixedSettings())
            .ProcessCleanupAsync(request, ct);
    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
}
sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AuditLog>().HasKey(x => x.Id);
        // Retain real fixture hints as persisted metadata. Production has no hold field or protocol.
        builder.Entity<AuditLog>().Property(x => x.ExtraProperties).HasConversion(
            x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
            x => JsonSerializer.Deserialize<Dictionary<string, object>>(x, (JsonSerializerOptions?)null)!);
    }
}
sealed class MutableTenant : ICurrentTenant
{
    public string? Id { get; set; } = "tenant-a";
    public ITenantInfo? Tenant => null;
    public Task<IDisposable> ChangeAsync(string tenantId) => throw new NotSupportedException();
    public IDisposable Change(ITenantInfo? tenant) => throw new NotSupportedException();
    public void SetTenantId(string tenantId) => Id = tenantId;
}
sealed class FixedSettings : ISettingProvider
{
    public Task<string?> GetOrNullAsync(string name, CancellationToken ct = default) => Task.FromResult<string?>("90");
    public Task<T?> GetAsync<T>(string name, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ResolvedSettingValue>> GetAllAsync(string? groupName = null, CancellationToken ct = default) => throw new NotSupportedException();
}
sealed class DeleteRace : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public int CompetingDeletes { get; private set; }
    public int? CleanupActualDeletes { get; private set; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Enabled && command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            Enabled = false;
            await using var competing = command.Connection!.CreateCommand();
            competing.CommandText = "DELETE FROM AuditLog";
            CompetingDeletes = await competing.ExecuteNonQueryAsync(ct);
        }
        return result;
    }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default)
    {
        if (CompetingDeletes > 0 && command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            CleanupActualDeletes = result;
        return ValueTask.FromResult(result);
    }
}
