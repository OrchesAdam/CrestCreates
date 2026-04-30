using System;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Xunit;

namespace CrestCreates.Domain.Tests;

public class ConcurrencyStampTests
{
    [Fact]
    public void NewAuditedEntity_HasNonEmptyConcurrencyStamp()
    {
        var entity = new TestAuditedEntity();
        Assert.False(string.IsNullOrEmpty(entity.ConcurrencyStamp));
    }

    [Fact]
    public void NewAuditedEntity_ConcurrencyStamp_IsValidGuid()
    {
        var entity = new TestAuditedEntity();
        Assert.True(Guid.TryParse(entity.ConcurrencyStamp, out _));
    }

    [Fact]
    public void AuditedEntity_ImplementsIHasConcurrencyStamp()
    {
        var entity = new TestAuditedEntity();
        Assert.IsAssignableFrom<IHasConcurrencyStamp>(entity);
    }

    [Fact]
    public void ConcurrencyStamp_CanBeSet()
    {
        var entity = new TestAuditedEntity();
        entity.ConcurrencyStamp = "custom-stamp";
        Assert.Equal("custom-stamp", entity.ConcurrencyStamp);
    }

    [Fact]
    public void AuditedAggregateRoot_ImplementsIHasConcurrencyStamp()
    {
        var entity = new TestAuditedAggregateRoot();
        Assert.IsAssignableFrom<IHasConcurrencyStamp>(entity);
    }

    [Fact]
    public void NewAuditedEntities_HaveUniqueConcurrencyStamps()
    {
        var entity1 = new TestAuditedEntity();
        var entity2 = new TestAuditedEntity();
        Assert.NotEqual(entity1.ConcurrencyStamp, entity2.ConcurrencyStamp);
    }

    private class TestAuditedEntity : AuditedEntity<Guid> { }

    private class TestAuditedAggregateRoot : AuditedAggregateRoot<Guid> { }
}
