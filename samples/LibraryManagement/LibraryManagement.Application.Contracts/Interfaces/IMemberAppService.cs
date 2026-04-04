using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface IMemberAppService : ICrestAppServiceBase<Member, Guid, MemberDto, CreateMemberDto, MemberDto>
{
    Task<MemberDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<PagedResultDto<MemberDto>> GetActiveMembersAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);
    
    Task PayBalanceAsync(Guid id, decimal amount, CancellationToken cancellationToken = default);
}
