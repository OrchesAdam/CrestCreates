using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface IUserRepository : ICrestRepositoryBase<User, Guid>
    {
        Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);
        Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
        Task<List<User>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default);
    }
}
