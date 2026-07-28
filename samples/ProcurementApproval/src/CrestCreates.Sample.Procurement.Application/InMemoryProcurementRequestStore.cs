using CrestCreates.Sample.Procurement.Domain.Entities;

namespace CrestCreates.Sample.Procurement.Application;

public sealed class InMemoryProcurementRequestStore
{
    private readonly Dictionary<Guid, ProcurementRequest> _requests = new();

    public void Add(ProcurementRequest request) => _requests[request.Id] = request;

    public ProcurementRequest? GetById(Guid id) => _requests.GetValueOrDefault(id);

    public bool Exists(Guid id) => _requests.ContainsKey(id);
}
