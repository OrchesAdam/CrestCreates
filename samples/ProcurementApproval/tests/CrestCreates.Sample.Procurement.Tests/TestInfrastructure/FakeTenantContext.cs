using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

public sealed class FakeTenantContext : ITenantContext
{
    public string? CurrentTenantId { get; set; }
}
