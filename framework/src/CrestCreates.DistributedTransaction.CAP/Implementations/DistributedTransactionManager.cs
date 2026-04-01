using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class DistributedTransactionManager : IDistributedTransactionManager
    {
        private IDistributedTransaction _currentTransaction;
        private readonly IServiceProvider _serviceProvider;

        public DistributedTransactionManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IDistributedTransaction CurrentTransaction => _currentTransaction;
        public bool HasActiveTransaction => CurrentTransaction != null;

        public async Task<IDistributedTransaction> CreateTransactionAsync()
        {
            var transaction = new DistributedTransaction(_serviceProvider);
            _currentTransaction = transaction;
            await transaction.BeginAsync();
            return transaction;
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            if (HasActiveTransaction)
            {
                return await action();
            }

            var transaction = await CreateTransactionAsync();
            try
            {
                var result = await action();
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                _currentTransaction = null;
            }
        }

        public async Task ExecuteInTransactionAsync(
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            if (HasActiveTransaction)
            {
                await action();
                return;
            }

            var transaction = await CreateTransactionAsync();
            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                _currentTransaction = null;
            }
        }
    }
}
