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
        Task<UserWithRolesDto?> GetUserWithRolesAsync(string userName, CancellationToken cancellationToken = default);
        Task<List<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<User?> GetUserByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    }

    public class UserWithRolesDto
    {
        public User User { get; set; } = default!;
        public List<Role> Roles { get; set; } = new();
    }
}
