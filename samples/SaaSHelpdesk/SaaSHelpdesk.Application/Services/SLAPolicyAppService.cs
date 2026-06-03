using System;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class SLAPolicyAppService : CrestAppServiceBase<SLAPolicy, Guid, SLAPolicyDto, CreateSLAPolicyDto, UpdateSLAPolicyDto>, ISLAPolicyAppService
{
    public SLAPolicyAppService(
        ICrestRepositoryBase<SLAPolicy, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
    }

    protected override SLAPolicy MapToEntity(CreateSLAPolicyDto dto)
    {
        var policy = new SLAPolicy(Guid.NewGuid(), dto.Name);
        policy.SetDescription(dto.Description);
        policy.SetResponseMinutes(TicketPriority.Low, dto.LowPriorityResponseMinutes);
        policy.SetResponseMinutes(TicketPriority.Medium, dto.MediumPriorityResponseMinutes);
        policy.SetResponseMinutes(TicketPriority.High, dto.HighPriorityResponseMinutes);
        policy.SetResponseMinutes(TicketPriority.Urgent, dto.UrgentPriorityResponseMinutes);
        policy.SetResolutionMinutes(TicketPriority.Low, dto.LowPriorityResolutionMinutes);
        policy.SetResolutionMinutes(TicketPriority.Medium, dto.MediumPriorityResolutionMinutes);
        policy.SetResolutionMinutes(TicketPriority.High, dto.HighPriorityResolutionMinutes);
        policy.SetResolutionMinutes(TicketPriority.Urgent, dto.UrgentPriorityResolutionMinutes);
        return policy;
    }

    protected override SLAPolicyDto MapToDto(SLAPolicy entity)
    {
        return new SLAPolicyDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            IsActive = entity.IsActive,
            LowPriorityResponseMinutes = entity.LowPriorityResponseMinutes,
            LowPriorityResolutionMinutes = entity.LowPriorityResolutionMinutes,
            MediumPriorityResponseMinutes = entity.MediumPriorityResponseMinutes,
            MediumPriorityResolutionMinutes = entity.MediumPriorityResolutionMinutes,
            HighPriorityResponseMinutes = entity.HighPriorityResponseMinutes,
            HighPriorityResolutionMinutes = entity.HighPriorityResolutionMinutes,
            UrgentPriorityResponseMinutes = entity.UrgentPriorityResponseMinutes,
            UrgentPriorityResolutionMinutes = entity.UrgentPriorityResolutionMinutes,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
        };
    }

    protected override void MapToEntity(UpdateSLAPolicyDto dto, SLAPolicy entity)
    {
        entity.SetName(dto.Name);
        entity.SetDescription(dto.Description);
        entity.SetResponseMinutes(TicketPriority.Low, dto.LowPriorityResponseMinutes);
        entity.SetResponseMinutes(TicketPriority.Medium, dto.MediumPriorityResponseMinutes);
        entity.SetResponseMinutes(TicketPriority.High, dto.HighPriorityResponseMinutes);
        entity.SetResponseMinutes(TicketPriority.Urgent, dto.UrgentPriorityResponseMinutes);
        entity.SetResolutionMinutes(TicketPriority.Low, dto.LowPriorityResolutionMinutes);
        entity.SetResolutionMinutes(TicketPriority.Medium, dto.MediumPriorityResolutionMinutes);
        entity.SetResolutionMinutes(TicketPriority.High, dto.HighPriorityResolutionMinutes);
        entity.SetResolutionMinutes(TicketPriority.Urgent, dto.UrgentPriorityResolutionMinutes);
    }
}
