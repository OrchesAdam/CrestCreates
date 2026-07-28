using System.Collections.Concurrent;
using CrestCreates.Sample.Procurement.Domain.Entities;

namespace CrestCreates.Sample.Procurement.Application;

public sealed class InMemoryProcurementRequestStore
{
    private readonly ConcurrentDictionary<(string TenantId, Guid RequestId), ProcurementRequest> _requests = new();

    public int Count => _requests.Count;

    public void Add(string tenantId, ProcurementRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!_requests.TryAdd((tenantId, request.Id), request))
            throw new InvalidOperationException($"Procurement request '{request.Id}' already exists for tenant '{tenantId}'.");
    }

    public ProcurementRequest? GetById(string tenantId, Guid id)
        => _requests.GetValueOrDefault((tenantId, id));

    public bool Exists(string tenantId, Guid id) => _requests.ContainsKey((tenantId, id));
}
