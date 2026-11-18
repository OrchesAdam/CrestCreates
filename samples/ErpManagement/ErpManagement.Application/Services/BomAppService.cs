using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Repositories;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IBomAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/bom",
    CapabilityIdPrefix = "compat.appservice.bom")]
public class BomAppService : CrestAppServiceBase<Bom, Guid, BomDto, CreateBomDto, UpdateBomDto>, IBomAppService
{
    private readonly IBomRepository _bomRepository;
    private readonly ICurrentTenant _currentTenant;

    public BomAppService(
        IBomRepository bomRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(bomRepository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _bomRepository = bomRepository;
        _currentTenant = currentTenant;
    }

    [UnitOfWorkMo]
    public override async Task<BomDto> CreateAsync(CreateBomDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var bom = new Bom(Guid.NewGuid(), input.MaterialId, input.Version, tenantId);
        bom.SetNotes(input.Notes);
        bom.ReplaceLines(BuildLines(input.Lines, bom.Id, tenantId));
        await SetCreationAuditPropertiesAsync(bom);
        var created = await _bomRepository.InsertAsync(bom, cancellationToken);
        return MapToDto(created);
    }

    public override async Task<BomDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var bom = await _bomRepository.GetWithLinesAsync(id, cancellationToken);
        return bom == null ? null : MapToDto(bom);
    }

    public override async Task<IReadOnlyList<BomDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var boms = await _bomRepository.GetAllWithLinesAsync(cancellationToken);
        return boms.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<BomDto>> GetByMaterialAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var boms = await _bomRepository.GetByMaterialWithLinesAsync(materialId, cancellationToken);
        return boms.Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<BomDto> UpdateAsync(Guid id, UpdateBomDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var bom = await _bomRepository.GetWithLinesAsync(id, cancellationToken)
                  ?? throw new KeyNotFoundException($"BOM 不存在: {id}");
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        bom.SetVersion(input.Version);
        bom.SetNotes(input.Notes);
        bom.ReplaceLines(BuildLines(input.Lines, bom.Id, tenantId));
        await SetModificationAuditPropertiesAsync(bom);
        var updated = await _bomRepository.UpdateAsync(bom, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<BomDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync("Bom.Activate", cancellationToken);
        var bom = await _bomRepository.GetWithLinesAsync(id, cancellationToken)
                  ?? throw new KeyNotFoundException($"BOM 不存在: {id}");
        bom.Activate();
        await SetModificationAuditPropertiesAsync(bom);
        var updated = await _bomRepository.UpdateAsync(bom, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<BomDto> DisableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync("Bom.Activate", cancellationToken);
        var bom = await _bomRepository.GetWithLinesAsync(id, cancellationToken)
                  ?? throw new KeyNotFoundException($"BOM 不存在: {id}");
        bom.Disable();
        await SetModificationAuditPropertiesAsync(bom);
        var updated = await _bomRepository.UpdateAsync(bom, cancellationToken);
        return MapToDto(updated);
    }

    private static List<BomLine> BuildLines(List<BomLineInput> lines, Guid bomId, Guid tenantId)
    {
        return lines.Select(l => new BomLine(Guid.NewGuid(), bomId, l.ComponentMaterialId, l.Quantity, l.Position, tenantId)).ToList();
    }

    protected override Bom MapToEntity(CreateBomDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        return new Bom(Guid.NewGuid(), dto.MaterialId, dto.Version, tenantId);
    }

    protected override BomDto MapToDto(Bom entity)
    {
        return new BomDto
        {
            Id = entity.Id,
            MaterialId = entity.MaterialId,
            Version = entity.Version,
            Status = entity.Status,
            Notes = entity.Notes,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            Lines = entity.Lines.OrderBy(l => l.Position).Select(l => new BomLineDto
            {
                Id = l.Id,
                ComponentMaterialId = l.ComponentMaterialId,
                Quantity = l.Quantity,
                Position = l.Position
            }).ToList()
        };
    }

    protected override void MapToEntity(UpdateBomDto dto, Bom entity)
    {
        entity.SetVersion(dto.Version);
        entity.SetNotes(dto.Notes);
    }
}