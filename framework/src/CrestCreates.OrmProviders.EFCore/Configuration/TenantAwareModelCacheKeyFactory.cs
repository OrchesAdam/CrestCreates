using System;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CrestCreates.OrmProviders.EFCore.Configuration;

public sealed class TenantAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is ITenantAwareDbContext tenantAwareDbContext)
        {
            return new TenantAwareModelCacheKey(context.GetType(), tenantAwareDbContext.CurrentTenantId, designTime);
        }

        return new TenantAwareModelCacheKey(context.GetType(), null, designTime);
    }

    public object Create(DbContext context)
    {
        return Create(context, false);
    }
}

internal sealed record TenantAwareModelCacheKey(Type ContextType, string? CurrentTenantId, bool DesignTime);
