using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.Data.Context;

namespace CrestCreates.Repositories.EFCore
{
    /// <summary>
    /// Entity Framework Core 事务实现
    /// </summary>
    public class EFCoreTransaction : IDbTransaction
    {
        private readonly IDbContextTransaction _transaction;
        private bool _disposed;

        public EFCoreTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            TransactionId = _transaction.TransactionId;
        }

        public Guid TransactionId { get; }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.RollbackAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _transaction?.Dispose();
                _disposed = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                }
                _disposed = true;
            }
        }
    }
}
