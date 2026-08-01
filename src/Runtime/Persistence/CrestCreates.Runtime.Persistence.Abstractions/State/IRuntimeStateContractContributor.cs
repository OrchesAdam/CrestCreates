namespace CrestCreates.Runtime.Persistence.Abstractions.State;

public interface IRuntimeStateContractContributor
{
    void Contribute(IRuntimeStateContractBuilder builder);
}
