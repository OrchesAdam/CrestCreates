using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Metadata;

public sealed class TenantScopedRegistry<TDescriptor> : IVersionedDescriptorRegistry<TDescriptor>
    where TDescriptor : class, IVersionedDescriptor
{
    private readonly IVersionedDescriptorRegistry<TDescriptor> _inner;
    private readonly ITenantContext? _tenantContext;
    private readonly Func<TDescriptor, string?> _tenantSelector;

    public TenantScopedRegistry(
        IVersionedDescriptorRegistry<TDescriptor> inner,
        ITenantContext? tenantContext,
        Func<TDescriptor, string?> tenantSelector)
    {
        _inner = inner;
        _tenantContext = tenantContext;
        _tenantSelector = tenantSelector;
    }

    private bool IsAccessible(TDescriptor descriptor)
    {
        if (_tenantContext?.CurrentTenantId == null) return true;
        var descriptorTenant = _tenantSelector(descriptor);
        return descriptorTenant == null || descriptorTenant == _tenantContext.CurrentTenantId;
    }

    public TDescriptor? GetById(string id)
    {
        var d = _inner.GetById(id);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetByName(string name)
    {
        var d = _inner.GetByName(name);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetByNameAndVersion(string name, int version)
    {
        var d = _inner.GetByNameAndVersion(name, version);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetByVersion(string id, int version)
    {
        var d = _inner.GetByVersion(id, version);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetActiveVersion(string name)
    {
        var d = _inner.GetActiveVersion(name);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetLatestVersion(string name)
    {
        var d = _inner.GetLatestVersion(name);
        return d != null && IsAccessible(d) ? d : null;
    }

    public IReadOnlyList<TDescriptor> GetAllByName(string name)
        => _inner.GetAllByName(name).Where(IsAccessible).ToList().AsReadOnly();

    public IReadOnlyList<TDescriptor> GetDeprecatedVersions(string name)
        => _inner.GetDeprecatedVersions(name).Where(IsAccessible).ToList().AsReadOnly();

    public IReadOnlyList<TDescriptor> GetAll()
        => _inner.GetAll().Where(IsAccessible).ToList().AsReadOnly();

    public void Build(IEnumerable<IDescriptorProvider<TDescriptor>> providers)
        => _inner.Build(providers);
}
