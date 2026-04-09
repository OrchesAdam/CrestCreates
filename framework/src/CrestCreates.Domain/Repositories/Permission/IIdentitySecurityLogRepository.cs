using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission;

public interface IIdentitySecurityLogRepository : ICrestRepositoryBase<IdentitySecurityLog, Guid>
{
    Task<List<IdentitySecurityLog>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
