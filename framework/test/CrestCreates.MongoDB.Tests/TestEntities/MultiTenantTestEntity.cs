using System;
using CrestCreates.DataFilter.Entities;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.MongoDB.Tests.TestEntities;

public class MultiTenantTestEntity : IEntity<string>, IMultiTenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? TenantId { get; set; }
}
