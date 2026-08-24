using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Json;

namespace CrestCreates.Sample.Procurement.Host;

public sealed class ProcurementRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
        => builder.Add(
            "crest.sample.procurement.humantask-decision/v1",
            ProcurementJsonContext.Default.ProcurementHumanTaskDecisionFact,
            new HashSet<Type> { typeof(ProcurementHumanTaskDecisionFact) });
}
