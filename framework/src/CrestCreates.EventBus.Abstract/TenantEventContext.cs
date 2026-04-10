using System;
using System.Threading;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.EventBus;

public class TenantEventContext
{
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public bool IsSuperAdminContext { get; set; }
}

public interface ITenantEventContextAccessor
{
    TenantEventContext? TenantContext { get; }
    void SetTenantContext(string? tenantId, string? tenantName = null, bool isSuperAdminContext = false);
    void ClearTenantContext();
}

public class TenantEventContextAccessor : ITenantEventContextAccessor
{
    private readonly ICurrentTenant _currentTenant;
    private readonly AsyncLocal<TenantEventContextHolder> _context = new();

    public TenantEventContextAccessor(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public TenantEventContext? TenantContext
    {
        get
        {
            var explicitContext = _context.Value?.Context;
            if (explicitContext != null)
            {
                return explicitContext;
            }

            if (!string.IsNullOrEmpty(_currentTenant.Id))
            {
                return new TenantEventContext
                {
                    TenantId = _currentTenant.Id,
                    TenantName = _currentTenant.Tenant?.Name
                };
            }

            return null;
        }
    }

    public void SetTenantContext(string? tenantId, string? tenantName = null, bool isSuperAdminContext = false)
    {
        _context.Value = new TenantEventContextHolder
        {
            Context = new TenantEventContext
            {
                TenantId = tenantId,
                TenantName = tenantName,
                IsSuperAdminContext = isSuperAdminContext
            }
        };
    }

    public void ClearTenantContext()
    {
        _context.Value = null;
    }

    private class TenantEventContextHolder
    {
        public TenantEventContext? Context { get; set; }
    }
}
