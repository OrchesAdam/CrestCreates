using AutoMapper;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using LibraryManagement.Domain.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Services;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;

namespace LibraryManagement.Application.Services;

[CrestService]
public class MemberAppService :CrestAppServiceBase<Member, Guid,MemberDto, CreateMemberDto, MemberDto>, IMemberAppService
{
    private readonly IMemberRepository _memberRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly IMapper _mapper;

    public MemberAppService(ICrestRepositoryBase<Member, Guid> repository, IMapper mapper, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IMemberRepository memberRepository, ILoanRepository loanRepository) : base(repository, mapper, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _memberRepository = memberRepository;
        _loanRepository = loanRepository;
        _mapper = mapper;
    }

    public async Task<MemberDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var member = await _memberRepository.GetByEmailAsync(email, cancellationToken);
        if (member == null)
            return null;

        return await MapToDtoAsync(member);
    }


    public async Task<PagedResultDto<MemberDto>> GetActiveMembersAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var members = await _memberRepository.GetActiveMembersAsync(cancellationToken);
        var totalCount = members.Count;

        var pagedMembers = members
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();

        var dtos = new List<MemberDto>();
        foreach (var member in pagedMembers)
        {
            dtos.Add(await MapToDtoAsync(member));
        }

        return new PagedResultDto<MemberDto>(dtos, totalCount, pageIndex, pageSize);
    }

    [UnitOfWorkMo]
    public async Task PayBalanceAsync(Guid id, decimal amount, CancellationToken cancellationToken = default)
    {
        var member = await _memberRepository.GetAsync(id, cancellationToken);
        if (member == null)
            throw new Exception($"Member with id {id} not found");

        member.PayBalance(amount);
        await _memberRepository.UpdateAsync(member, cancellationToken);
    }

    private async Task<MemberDto> MapToDtoAsync(Member member)
    {
        var dto = _mapper.Map<MemberDto>(member);
        dto.CurrentLoanCount = await _loanRepository.GetActiveLoanCountByMemberAsync(member.Id, CancellationToken.None);
        return dto;
    }
}
