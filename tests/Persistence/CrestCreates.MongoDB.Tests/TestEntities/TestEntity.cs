using System;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.MongoDB.Tests.TestEntities;

public class TestEntity : IEntity<string>
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}
