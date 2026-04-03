using AutoMapper;
using CrestCreates.Domain.Shared.Attributes;
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

namespace LibraryManagement.Application.Services;

[Service(GenerateController = false)]
public class MemberAppService : IMemberAppService
{
    private readonly IMemberRepository _memberRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly IMapper _mapper;

    public MemberAppService(
        IMemberRepository memberRepository,
        ILoanRepository loanRepository,
        IMapper mapper)
    {
        _memberRepository = memberRepository;
        _loanRepository = loanRepository;
        _mapper = mapper;
    }

    public async Task<MemberDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await _memberRepository.GetByIdAsync(id);
        if (member == null)
            throw new Exception($"Member with id {id} not found");

        return await MapToDtoAsync(member);
    }

    public async Task<MemberDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var member = await _memberRepository.GetByEmailAsync(email, cancellationToken);
        if (member == null)
            return null;

        return await MapToDtoAsync(member);
    }

    public async Task<PagedResult<MemberDto>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var members = await _memberRepository.GetAllAsync();
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

        return new PagedResult<MemberDto>(dtos, totalCount, pageIndex, pageSize);
    }

    public async Task<PagedResult<MemberDto>> GetActiveMembersAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
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

        return new PagedResult<MemberDto>(dtos, totalCount, pageIndex, pageSize);
    }

    public async Task<MemberDto> CreateAsync(CreateMemberDto input, CancellationToken cancellationToken = default)
    {
        // Validate email uniqueness
        if (!await _memberRepository.IsEmailUniqueAsync(input.Email, cancellationToken: cancellationToken))
            throw new Exception($"Email '{input.Email}' already exists");

        var member = new Member(
            Guid.NewGuid(),
            input.Name,
            input.Email,
            input.Type,
            input.Phone,
            input.Address,
            input.ExpiryDate
        );

        await _memberRepository.AddAsync(member);
        return await MapToDtoAsync(member);
    }

    public async Task<MemberDto> UpdateAsync(Guid id, UpdateMemberDto input, CancellationToken cancellationToken = default)
    {
        var member = await _memberRepository.GetByIdAsync(id);
        if (member == null)
            throw new Exception($"Member with id {id} not found");

        member.SetName(input.Name);
        member.SetPhone(input.Phone);
        member.SetType(input.Type);
        member.SetActive(input.IsActive);

        await _memberRepository.UpdateAsync(member);
        return await MapToDtoAsync(member);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Check if member has active loans
        var loanCount = await _loanRepository.GetActiveLoanCountByMemberAsync(id, cancellationToken);
        if (loanCount > 0)
            throw new Exception("Cannot delete member with active loans");

        var member = await _memberRepository.GetByIdAsync(id);
        if (member != null)
        {
            await _memberRepository.DeleteAsync(member);
        }
    }

    public async Task PayBalanceAsync(Guid id, decimal amount, CancellationToken cancellationToken = default)
    {
        var member = await _memberRepository.GetByIdAsync(id);
        if (member == null)
            throw new Exception($"Member with id {id} not found");

        member.PayBalance(amount);
        await _memberRepository.UpdateAsync(member);
    }

    private async Task<MemberDto> MapToDtoAsync(Member member)
    {
        var dto = _mapper.Map<MemberDto>(member);
        dto.CurrentLoanCount = await _loanRepository.GetActiveLoanCountByMemberAsync(member.Id, CancellationToken.None);
        return dto;
    }
}
