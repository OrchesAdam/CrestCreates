using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.MultiTenancy;

namespace CrestCreates.Application.Tenants;

[CrestService]
public class TenantDomainAppService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantDomainMappingRepository _domainMappingRepository;
    private readonly TenantIdentifierNormalizer _normalizer;

    public TenantDomainAppService(
        ITenantRepository tenantRepository,
        ITenantDomainMappingRepository domainMappingRepository,
        TenantIdentifierNormalizer normalizer)
    {
        _tenantRepository = tenantRepository;
        _domainMappingRepository = domainMappingRepository;
        _normalizer = normalizer;
    }

    public async Task<TenantDomainMappingDto> CreateDomainMappingAsync(
        string tenantName,
        CreateTenantDomainMappingDto input,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        var normalizedDomain = _normalizer.NormalizeDomain(input.Domain)
            ?? throw new ArgumentException("域名格式无效", nameof(input.Domain));

        if (!string.IsNullOrEmpty(input.Subdomain))
        {
            input.Subdomain = _normalizer.NormalizeSlug(input.Subdomain);
        }

        var existingMapping = await _domainMappingRepository.FindByDomainAsync(normalizedDomain, cancellationToken);
        if (existingMapping != null)
        {
            throw new InvalidOperationException($"域名 '{normalizedDomain}' 已被其他租户使用");
        }

        var mapping = new TenantDomainMapping(Guid.NewGuid(), tenant.Id, normalizedDomain)
        {
            Subdomain = input.Subdomain,
            IsActive = true,
            Priority = input.Priority
        };

        await _domainMappingRepository.InsertAsync(mapping, cancellationToken);

        return new TenantDomainMappingDto
        {
            Id = mapping.Id,
            TenantId = mapping.TenantId,
            TenantName = tenant.Name,
            Domain = mapping.Domain,
            Subdomain = mapping.Subdomain,
            IsActive = mapping.IsActive,
            Priority = mapping.Priority,
            CreationTime = mapping.CreationTime
        };
    }

    public async Task DeleteDomainMappingAsync(
        string tenantName,
        Guid mappingId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        var mapping = await _domainMappingRepository.GetAsync(mappingId, cancellationToken)
            ?? throw new InvalidOperationException("域名映射不存在");

        if (mapping.TenantId != tenant.Id)
        {
            throw new InvalidOperationException("无权删除此域名映射");
        }

        await _domainMappingRepository.DeleteAsync(mapping, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantDomainMappingDto>> GetTenantDomainMappingsAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        var mappings = await _domainMappingRepository.GetByTenantIdAsync(tenant.Id, cancellationToken);

        return mappings.Select(m => new TenantDomainMappingDto
        {
            Id = m.Id,
            TenantId = m.TenantId,
            TenantName = tenant.Name,
            Domain = m.Domain,
            Subdomain = m.Subdomain,
            IsActive = m.IsActive,
            Priority = m.Priority,
            CreationTime = m.CreationTime
        }).ToArray();
    }
}

public class TenantDomainMappingDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreateTenantDomainMappingDto
{
    public string Domain { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
    public int Priority { get; set; }
}
