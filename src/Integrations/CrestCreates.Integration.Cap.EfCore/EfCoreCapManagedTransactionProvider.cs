using System;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Options;
using DotNetCore.CAP;
using DotNetCore.CAP.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CrestCreates.Integration.Cap.EfCore;

/// <summary>
/// EF Core + Npgsql 的受管事务绑定提供者（CAP 原子发布协议集成侧）：
/// 1) 存储身份绑定：唯一支持组合 = EF Core + Npgsql 参与资源 ↔ CAP PostgreSql 存储，
///    且双方指向**同一固定数据库**（按活连接的 host/port/database 规范字段比较，非原始串）；
/// 2) 借用边界：经 #137 <see cref="IEfCoreTransactionBorrower"/> 取受管事务（含资源/事务引用比对）；
/// 3) 瞬态绑定：设置 publisher.Transaction 为守护型 CAP 事务，Dispose 恢复先前值并解除引用；
/// 4) 全部拒绝在写入前抛 <see cref="CapPublishRefusalException"/>（诊断不含凭据）。
/// </summary>
public sealed class EfCoreCapManagedTransactionProvider : ICapManagedTransactionProvider
{
    private readonly IDataBaseContext _dataBaseContext;
    private readonly IEfCoreTransactionBorrower _borrower;
    private readonly IServiceProvider _serviceProvider;
    private readonly DistributedTransactionCapOptions _options;
    private readonly IOptions<DotNetCore.CAP.PostgreSqlOptions> _effectivePostgresStorage;

    public EfCoreCapManagedTransactionProvider(
        IDataBaseContext dataBaseContext,
        IEfCoreTransactionBorrower borrower,
        IServiceProvider serviceProvider,
        IOptions<DistributedTransactionCapOptions> options,
        IOptions<DotNetCore.CAP.PostgreSqlOptions> effectivePostgresStorage)
    {
        _dataBaseContext = dataBaseContext ?? throw new ArgumentNullException(nameof(dataBaseContext));
        _borrower = borrower ?? throw new ArgumentNullException(nameof(borrower));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _effectivePostgresStorage = effectivePostgresStorage ?? throw new ArgumentNullException(nameof(effectivePostgresStorage));
    }

    /// <inheritdoc />
    public void EnsureNoUnmanagedNativeTransaction()
    {
        if (!TryGetScopedDbContext(out var dbContext))
        {
            return; // 无可检测上下文：按无窗口处理（文档明示）
        }

        try
        {
            if (dbContext.Database.CurrentTransaction is not null)
            {
                throw new CapPublishRefusalException(
                    "UnmanagedNativeTransaction",
                    "A database transaction is open on the scoped context outside any managed unit-of-work window; " +
                    "CAP cannot join it and no message was written. Publish inside a managed unit of work or after " +
                    "the native transaction ends.");
            }
        }
        catch (CapPublishRefusalException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            // 无法读取当前事务（非关系型/未配置 Provider）：无可检测的未受管事务。
        }
    }

    /// <inheritdoc />
    public ICapManagedTransactionBinding BindManagedTransaction(ICapPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        // 全部前置校验先于任何写入/绑定动作。
        if (_options.StorageProvider != CapStorageProvider.PostgreSql)
        {
            throw new CapPublishRefusalException(
                "StorageIdentityMismatch",
                $"The CAP storage provider '{_options.StorageProvider}' is not supported by the EF Core + Npgsql " +
                "atomic-publish integration; no message was written.");
        }

        if (!_borrower.TryBorrowCurrent(out var dbContext, out var transaction) ||
            dbContext is null ||
            transaction is null)
        {
            throw new CapPublishRefusalException(
                "BorrowerUnavailable",
                "The managed window does not expose a borrowable EF transaction. Ensure the EF Core unit-of-work " +
                "integration is registered; no message was written.");
        }

        EnsureStorageIdentity(dbContext, transaction);

        var dispatcher = _serviceProvider.GetRequiredService<IDispatcher>();
        var guarded = new GuardedPostgreSqlCapTransaction(dispatcher)
        {
            DbTransaction = transaction,
            AutoCommit = false
        };

        return new Binding(publisher, guarded);
    }

    private void EnsureStorageIdentity(DbContext dbContext, IDbContextTransaction transaction)
    {
        var providerName = dbContext.Database.ProviderName;
        if (providerName is null || !providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            throw new CapPublishRefusalException(
                "StorageIdentityMismatch",
                "The participating resource is not an Npgsql EF Core provider. Atomic CAP publish supports " +
                "EF Core + Npgsql against a single fixed database only; no message was written.");
        }

        var connection = transaction.GetDbTransaction().Connection as NpgsqlConnection;
        if (connection is null)
        {
            throw new CapPublishRefusalException(
                "StorageIdentityMismatch",
                "The borrowed transaction does not expose an Npgsql connection; no message was written.");
        }

        // 权威比较对象是 CAP **最终有效**的存储配置（所有 Configure/PostConfigure 之后），
        // 而不是 Crest 输入快照——后续覆盖最终存储目标的注册同样会被此检查捕获。
        var effectiveStorage = _effectivePostgresStorage.Value;
        var effectiveConnectionString = effectiveStorage.ConnectionString
            ?? effectiveStorage.DataSource?.ConnectionString;
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            throw new CapPublishRefusalException(
                "StorageIdentityMismatch",
                "The final CAP PostgreSQL storage is not configured (PostgreSqlOptions.ConnectionString/DataSource " +
                "is empty). Atomic publish requires a configured single fixed database; no message was written.");
        }

        var live = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        var storage = new NpgsqlConnectionStringBuilder(effectiveConnectionString);
        var sameDatabase =
            string.Equals(live.Host ?? string.Empty, storage.Host ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            live.Port == storage.Port &&
            string.Equals(live.Database ?? string.Empty, storage.Database ?? string.Empty, StringComparison.Ordinal);

        if (!sameDatabase)
        {
            throw new CapPublishRefusalException(
                "StorageIdentityMismatch",
                $"CAP storage target '{storage.Host}:{storage.Port}/{storage.Database}' does not match the " +
                $"participating database '{live.Host}:{live.Port}/{live.Database}'. Atomic CAP publish supports a " +
                "single fixed database; no message was written.");
        }
    }

    private bool TryGetScopedDbContext(out DbContext dbContext)
    {
        try
        {
            if (_dataBaseContext.GetNativeContext() is DbContext native)
            {
                dbContext = native;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // 上下文不可解析（未注册/校验拒绝）：无可检测上下文。
        }

        dbContext = null!;
        return false;
    }

    private sealed class Binding : ICapManagedTransactionBinding
    {
        private readonly ICapPublisher _publisher;
        private readonly GuardedPostgreSqlCapTransaction _guarded;
        private readonly ICapTransaction? _previous;
        private bool _disposed;

        public Binding(ICapPublisher publisher, GuardedPostgreSqlCapTransaction guarded)
        {
            _publisher = publisher;
            _guarded = guarded;
            _previous = publisher.Transaction;
            publisher.Transaction = guarded;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 瞬态恢复：不跨调用遗留 publisher 上下文。
            _publisher.Transaction = _previous;

            // 仅解除借用引用（守护覆写只清 DbTransaction，不释放句柄）。
            _guarded.Dispose();
        }
    }
}
