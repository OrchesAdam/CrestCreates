using CrestCreates.DistributedTransaction.Abstractions;

namespace CrestCreates.DistributedTransaction.Extensions
{
    public interface ITransactionParticipantRegistry
    {
        void AddParticipant(ITransactionParticipant participant);
    }
}
