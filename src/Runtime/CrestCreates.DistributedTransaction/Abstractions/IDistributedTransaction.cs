using System;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface IDistributedTransaction : IDisposable
    {
        Guid TransactionId { get; }
        TransactionStatus Status { get; }
        
        Task BeginAsync();
        Task CommitAsync();
        Task RollbackAsync();
        Task CompensateAsync();
    }
}
