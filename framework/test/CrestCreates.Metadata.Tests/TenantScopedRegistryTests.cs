using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
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

    private static SchemaRegistry CreateSchemaRegistry(params SchemaDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<SchemaDescriptor>([]);
        var registry = new SchemaRegistry(engine);
        var provider = new TestSchemaProvider(descriptors.ToList());
        registry.Build([provider]);
        return registry;
    }

    private sealed class TestSchemaProvider : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors;
        public TestSchemaProvider(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }

    [Fact]
    public void GetById_Returns_WhenNoTenantContext()
    {
        var inner = CreateSchemaRegistry(new SchemaDescriptor { Id = "s1", Name = "A", Version = 1 });
        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);

        var result = scoped.GetById("s1");
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetAll_ReturnsAll_WhenNoTenant()
    {
        var inner = CreateSchemaRegistry(
            new SchemaDescriptor { Id = "s1", Name = "A", Version = 1 },
            new SchemaDescriptor { Id = "s2", Name = "B", Version = 1 });

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        scoped.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_ReturnsAll_WhenNullTenantContext()
    {
        var inner = CreateSchemaRegistry(new SchemaDescriptor { Id = "s1", Name = "A", Version = 1 });

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        scoped.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void GetByName_DelegatesToInner()
    {
        var inner = CreateSchemaRegistry(new SchemaDescriptor { Id = "s1", Name = "TestSchema", Version = 1 });

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        var result = scoped.GetByName("TestSchema");
        result.Should().NotBeNull();
        result!.Id.Should().Be("s1");
    }
}
