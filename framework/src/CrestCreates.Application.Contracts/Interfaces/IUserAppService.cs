using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IUserAppService
{
    Task<IdentityUserDto> CreateAsync(CreateIdentityUserDto input, CancellationToken cancellationToken = default);
    Task<IdentityUserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityUserDto>> GetListAsync(string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IdentityUserDto> UpdateAsync(Guid userId, UpdateIdentityUserDto input, CancellationToken cancellationToken = default);
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangeIdentityPasswordDto input, CancellationToken cancellationToken = default);
    Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityUserRoleDto>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);
}
