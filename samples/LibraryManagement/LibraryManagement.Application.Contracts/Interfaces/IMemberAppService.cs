using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface IMemberAppService
{
    Task<MemberDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<PagedResult<MemberDto>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<PagedResult<MemberDto>> GetActiveMembersAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<MemberDto> CreateAsync(CreateMemberDto input, CancellationToken cancellationToken = default);
    Task<MemberDto> UpdateAsync(Guid id, UpdateMemberDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task PayBalanceAsync(Guid id, decimal amount, CancellationToken cancellationToken = default);
}
