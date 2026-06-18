namespace CrestCreates.DistributedTransaction.Models
{
    public enum TransactionStatus
    {
        Pending,
        Started,
        Committed,
        RolledBack,
        Compensating,
        Compensated,
        Failed
    }
}
