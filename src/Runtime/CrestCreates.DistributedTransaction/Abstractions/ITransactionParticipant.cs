using System.Threading.Tasks;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ITransactionParticipant
    {
        Task<bool> PrepareAsync();
        Task CommitAsync();
        Task RollbackAsync();
        Task CompensateAsync();
    }
}
