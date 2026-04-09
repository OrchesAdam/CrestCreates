using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.AspNetCore.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly ITenantAppService _tenantAppService;

    public TenantsController(ITenantAppService tenantAppService)
    {
        _tenantAppService = tenantAppService;
    }

    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(
        [FromBody] CreateTenantDto input,
        CancellationToken cancellationToken = default)
    {
        var result = await _tenantAppService.CreateAsync(input, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<TenantDto>> Get(
        string name,
        CancellationToken cancellationToken = default)
    {
        var result = await _tenantAppService.GetAsync(name, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TenantDto>>> GetList(
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _tenantAppService.GetListAsync(isActive, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{name}")]
    public async Task<ActionResult<TenantDto>> Update(
        string name,
        [FromBody] UpdateTenantDto input,
        CancellationToken cancellationToken = default)
    {
        var result = await _tenantAppService.UpdateAsync(name, input, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{name}/activation")]
    public async Task<ActionResult> SetActive(
        string name,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken = default)
    {
        await _tenantAppService.SetActiveAsync(name, isActive, cancellationToken);
        return Ok(new { message = "租户状态更新成功" });
    }
}
