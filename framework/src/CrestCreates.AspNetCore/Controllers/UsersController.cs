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
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserAppService _userAppService;

    public UsersController(IUserAppService userAppService)
    {
        _userAppService = userAppService;
    }

    [HttpPost]
    public async Task<ActionResult<IdentityUserDto>> Create(
        [FromBody] CreateIdentityUserDto input,
        CancellationToken cancellationToken = default)
    {
        var result = await _userAppService.CreateAsync(input, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<IdentityUserDto>> Get(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = await _userAppService.GetAsync(userId, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IdentityUserDto>>> GetList(
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _userAppService.GetListAsync(tenantId, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{userId:guid}")]
    public async Task<ActionResult<IdentityUserDto>> Update(
        Guid userId,
        [FromBody] UpdateIdentityUserDto input,
        CancellationToken cancellationToken = default)
    {
        var result = await _userAppService.UpdateAsync(userId, input, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{userId:guid}/activation")]
    public async Task<ActionResult> SetActive(
        Guid userId,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken = default)
    {
        await _userAppService.SetActiveAsync(userId, isActive, cancellationToken);
        return Ok(new { message = "用户状态更新成功" });
    }

    [HttpPut("{userId:guid}/password")]
    public async Task<ActionResult> ChangePassword(
        Guid userId,
        [FromBody] ChangeIdentityPasswordDto input,
        CancellationToken cancellationToken = default)
    {
        await _userAppService.ChangePasswordAsync(userId, input, cancellationToken);
        return Ok(new { message = "密码修改成功" });
    }

    [HttpPost("{userId:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult> AssignRole(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        await _userAppService.AssignRoleAsync(userId, roleId, cancellationToken);
        return Ok(new { message = "角色分配成功" });
    }

    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult> RemoveRole(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        await _userAppService.RemoveRoleAsync(userId, roleId, cancellationToken);
        return Ok(new { message = "角色移除成功" });
    }

    [HttpGet("{userId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<IdentityUserRoleDto>>> GetRoles(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = await _userAppService.GetRolesAsync(userId, cancellationToken);
        return Ok(result);
    }
}
