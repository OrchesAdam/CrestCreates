using System.Collections.Generic;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ICompensationExecutorRegistry
    {
        ICompensationExecutor? GetExecutor(string name);
        IEnumerable<ICompensationExecutor> GetAll();
    }
}