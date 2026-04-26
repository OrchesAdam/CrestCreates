using System.Threading.Tasks;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ICompensationExecutor
    {
        string Name { get; }
        Task ExecuteAsync(string? compensationData);
    }
}