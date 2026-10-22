using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Security.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IAgentAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/agent",
    CapabilityIdPrefix = "compat.appservice.agent")]
public class AgentAppService : IAgentAppService
{
    private readonly ICrestRepositoryBase<CrestCreates.Domain.Permission.User, Guid> _userRepository;
    private readonly ICrestRepositoryBase<CrestCreates.Domain.Permission.Role, Guid> _roleRepository;
    private readonly ICrestRepositoryBase<CrestCreates.Domain.Permission.UserRole, Guid> _userRoleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentTenant _currentTenant;

    public AgentAppService(
        ICrestRepositoryBase<CrestCreates.Domain.Permission.User, Guid> userRepository,
        ICrestRepositoryBase<CrestCreates.Domain.Permission.Role, Guid> roleRepository,
        ICrestRepositoryBase<CrestCreates.Domain.Permission.UserRole, Guid> userRoleRepository,
        IPasswordHasher passwordHasher,
        ICurrentTenant currentTenant)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _passwordHasher = passwordHasher;
        _currentTenant = currentTenant;
    }

    [UnitOfWorkMo]
    public async Task<IdentityUserDto> CreateAsync(CreateAgentDto input, CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant.Id ?? "host";
        var user = new CrestCreates.Domain.Permission.User(
            Guid.NewGuid(), input.UserName, input.Email, tenantId)
        {
            PasswordHash = _passwordHasher.HashPassword(input.Password),
            IsActive = true,
            IsSuperAdmin = false,
            CreationTime = DateTime.UtcNow,
            LastPasswordChangeTime = DateTime.UtcNow
        };
        await _userRepository.InsertAsync(user, cancellationToken);

        if (!string.IsNullOrEmpty(input.Role))
        {
            var roles = await _roleRepository.GetListAsync(
                r => r.Name == input.Role && r.TenantId == tenantId, cancellationToken);
            var role = roles.FirstOrDefault();
            if (role is not null)
            {
                var userRole = new CrestCreates.Domain.Permission.UserRole(
                    Guid.NewGuid(), user.Id, role.Id, tenantId);
                await _userRoleRepository.InsertAsync(userRole, cancellationToken);
            }
        }

        return new IdentityUserDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            IsActive = user.IsActive,
            Role = input.Role,
        };
    }

    public async Task<IdentityUserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(id, cancellationToken);
        if (user == null) return null;
        return new IdentityUserDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            IsActive = user.IsActive,
        };
    }

    [UnitOfWorkMo]
    public async Task<IdentityUserDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(id, cancellationToken);
        if (user == null)
            throw new KeyNotFoundException("Agent not found");
        user.IsActive = false;
        await _userRepository.UpdateAsync(user, cancellationToken);
        return new IdentityUserDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            IsActive = false,
        };
    }
}
