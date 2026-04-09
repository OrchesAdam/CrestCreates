using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DataFilter;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class RefreshTokenRepository : EfCoreRepositoryBase<RefreshToken, Guid>, IRefreshTokenRepository
{
    public RefreshTokenRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<RefreshToken?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .FirstOrDefaultAsync(item => item.Token == token, cancellationToken);
    }

    public Task<List<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(item => item.UserId == userId &&
                           item.RevokedTime == null &&
                           item.ExpirationTime > DateTime.UtcNow)
            .OrderByDescending(item => item.CreationTime)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await GetQueryable()
            .Where(item => item.UserId == userId && item.RevokedTime == null)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return;
        }

        var revokedTime = DateTime.UtcNow;
        foreach (var item in items)
        {
            item.RevokedTime = revokedTime;
        }

        await UpdateRangeAsync(items, cancellationToken);
    }
}
