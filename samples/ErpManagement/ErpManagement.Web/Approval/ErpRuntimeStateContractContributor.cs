using CrestCreates.Runtime.Persistence.Abstractions.State;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Json;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Web.Approval;

public sealed class ErpRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        builder.Add(
            ErpContractIds.RequisitionDecisionTypeId,
            ErpEventJsonContext.Default.ErpRequisitionDecisionFact,
            new HashSet<Type> { typeof(ErpRequisitionDecisionFact) });
        builder.Add(ErpContractIds.SalesDecisionTypeId, ErpEventJsonContext.Default.ErpSalesDecisionFact,
            new HashSet<Type> { typeof(ErpSalesDecisionFact) });
    }
}