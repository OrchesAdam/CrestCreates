using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.Repositories;

public class SettingRepository : EfCoreRepository<SettingValue, System.Guid>, ISettingRepository
{
    public SettingRepository(IDataBaseContext dbContext)
        : base(dbContext)
    {
    }

    public override async Task<SettingValue> InsertAsync(
        SettingValue entity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.InsertAsync(entity, cancellationToken);
        }
        catch (DbUpdateException exception) when (EfCoreUniqueConstraintViolationDetector.IsUniqueConstraintViolation(exception))
        {
            DetachAfterFailedInsert(entity);
            throw new CrestUniqueConstraintException(nameof(SettingValue), exception);
        }
    }

    public Task<SettingValue?> FindAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .FirstOrDefaultAsync(
                setting => setting.Name == name &&
                           setting.Scope == scope &&
                           setting.ProviderKey == providerKey &&
                           setting.TenantId == tenantId,
                cancellationToken);
    }

    public Task<List<SettingValue>> GetListByScopeAsync(
        SettingScope scope,
        string? providerKey = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable().Where(setting => setting.Scope == scope);

        if (providerKey is not null)
        {
            query = query.Where(setting => setting.ProviderKey == providerKey);
        }

        if (tenantId is null)
        {
            query = query.Where(setting => setting.TenantId == null);
        }
        else
        {
            query = query.Where(setting => setting.TenantId == tenantId);
        }

        return query
            .OrderBy(setting => setting.Name)
            .ToListAsync(cancellationToken);
    }
}
