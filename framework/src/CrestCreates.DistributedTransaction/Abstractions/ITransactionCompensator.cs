using System;
using System.Threading.Tasks;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ITransactionCompensator
    {
        Task CompensateAsync(Guid transactionId);
        Task<bool> CanCompensateAsync(Guid transactionId);
    }
}
