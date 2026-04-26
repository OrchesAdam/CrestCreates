namespace CrestCreates.DistributedTransaction.Models
{
    public enum CompensationStatus
    {
        Pending = 0,
        Executing = 1,
        Completed = 2,
        Failed = 3,
        Retrying = 4
    }
}