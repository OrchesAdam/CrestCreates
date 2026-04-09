using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.AspNetCore.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IRoleAppService _roleAppService;

    public RolesController(IRoleAppService roleAppService)
    {
        _roleAppService = roleAppService;
    }

    [HttpPost]
    public async Task<ActionResult<IdentityRoleDto>> Create(
        [FromBody] CreateIdentityRoleDto input,
        CancellationToken cancellationToken = default)
    {
        var result = await _roleAppService.CreateAsync(input, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{roleId:guid}")]
    public async Task<ActionResult<IdentityRoleDto>> Get(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var result = await _roleAppService.GetAsync(roleId, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IdentityRoleDto>>> GetList(
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _roleAppService.GetListAsync(tenantId, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{roleId:guid}")]
    public async Task<ActionResult<IdentityRoleDto>> Update(
        Guid roleId,
        [FromBody] UpdateIdentityRoleDto input,
        CancellationToken cancellationToken = default)
    {
        var result = await _roleAppService.UpdateAsync(roleId, input, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{roleId:guid}/activation")]
    public async Task<ActionResult> SetActive(
        Guid roleId,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken = default)
    {
        await _roleAppService.SetActiveAsync(roleId, isActive, cancellationToken);
        return Ok(new { message = "角色状态更新成功" });
    }
}
