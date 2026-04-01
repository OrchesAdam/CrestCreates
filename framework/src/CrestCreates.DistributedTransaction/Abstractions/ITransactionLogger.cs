using System;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ITransactionLogger
    {
        Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string message = null);
        Task LogTransactionErrorAsync(Guid transactionId, Exception exception);
        Task<TransactionStatus?> GetTransactionStatusAsync(Guid transactionId);
    }
}
