using System;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IAgentAppService
{
    Task<IdentityUserDto> CreateAsync(CreateAgentDto input);
    Task<IdentityUserDto?> GetByIdAsync(Guid id);
    Task<IdentityUserDto> DeactivateAsync(Guid id);
}
