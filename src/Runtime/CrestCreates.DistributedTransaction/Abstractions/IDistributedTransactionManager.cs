using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface IDistributedTransactionManager
    {
        Task<IDistributedTransaction> CreateTransactionAsync();
        IDistributedTransaction CurrentTransaction { get; }
        bool HasActiveTransaction { get; }
        
        Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> action,
            CancellationToken cancellationToken = default);
        
        Task ExecuteInTransactionAsync(
            Func<Task> action,
            CancellationToken cancellationToken = default);
    }
}
