using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class TenantScopedRegistryTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
    }

    [Fact]
    public void GetById_Returns_WhenNoTenantContext()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(new SchemaDescriptor { Id = "s1", Name = "A", Version = 1 });
        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);

        var result = scoped.GetById("s1");
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetAll_ReturnsAll_WhenNoTenant()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(new SchemaDescriptor { Id = "s1", Name = "A", Version = 1 });
        inner.Register(new SchemaDescriptor { Id = "s2", Name = "B", Version = 1 });

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        scoped.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_ReturnsAll_WhenNullTenantContext()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(new SchemaDescriptor { Id = "s1", Name = "A", Version = 1 });

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        scoped.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void GetByName_DelegatesToInner()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(new SchemaDescriptor { Id = "s1", Name = "TestSchema", Version = 1 });

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        var result = scoped.GetByName("TestSchema");
        result.Should().NotBeNull();
        result!.Id.Should().Be("s1");
    }
}