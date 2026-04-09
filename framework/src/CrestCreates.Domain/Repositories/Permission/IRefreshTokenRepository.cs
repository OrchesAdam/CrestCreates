using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission;

public interface IRefreshTokenRepository : ICrestRepositoryBase<RefreshToken, Guid>
{
    Task<RefreshToken?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<List<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task RevokeAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
