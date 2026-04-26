using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ITransactionCompensator
    {
        Task CompensateAsync(Guid transactionId);
        Task<bool> CanCompensateAsync(Guid transactionId);

        // Extended methods for persistent compensation
        Task RegisterCompensationAsync(
            Guid transactionId,
            string participantName,
            object? compensationData);

        Task<IEnumerable<TransactionCompensation>> GetPendingCompensationsAsync(Guid transactionId);
        Task MarkCompensationCompletedAsync(Guid compensationId);
        Task MarkCompensationFailedAsync(Guid compensationId, string errorMessage);
        Task ProcessRetryingCompensationsAsync();
    }
}
