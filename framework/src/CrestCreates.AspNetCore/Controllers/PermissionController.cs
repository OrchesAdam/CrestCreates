using CrestCreates.Application.Contracts.DTOs.Permissions;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.AspNetCore.Controllers
{
    [ApiController]
    [Route("api/permissions")]
    [Authorize]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionGrantAppService _permissionGrantAppService;

        public PermissionController(IPermissionGrantAppService permissionGrantAppService)
        {
            _permissionGrantAppService = permissionGrantAppService;
        }

        [HttpPost("users/{userId}/grants")]
        public async Task<ActionResult> GrantToUser(
            string userId,
            [FromBody] PermissionGrantChangeDto input,
            CancellationToken cancellationToken = default)
        {
            await _permissionGrantAppService.GrantToUserAsync(userId, input, cancellationToken);
            return Ok(new { message = "权限授予成功" });
        }

        [HttpDelete("users/{userId}/grants")]
        public async Task<ActionResult> RevokeFromUser(
            string userId,
            [FromBody] PermissionGrantChangeDto input,
            CancellationToken cancellationToken = default)
        {
            await _permissionGrantAppService.RevokeFromUserAsync(userId, input, cancellationToken);
            return Ok(new { message = "权限撤销成功" });
        }

        [HttpGet("users/{userId}/grants")]
        public async Task<ActionResult<IReadOnlyList<PermissionGrantDto>>> GetUserGrants(
            string userId,
            CancellationToken cancellationToken = default)
        {
            var result = await _permissionGrantAppService.GetUserGrantsAsync(userId, cancellationToken);
            return Ok(result);
        }

        [HttpGet("users/{userId}/effective")]
        public async Task<ActionResult<UserEffectivePermissionsDto>> GetUserEffectivePermissions(
            string userId,
            [FromQuery] string? tenantId = null,
            CancellationToken cancellationToken = default)
        {
            var result = await _permissionGrantAppService.GetUserEffectivePermissionsAsync(
                userId,
                tenantId,
                cancellationToken);
            return Ok(result);
        }

        [HttpPost("roles/{roleName}/grants")]
        public async Task<ActionResult> GrantToRole(
            string roleName,
            [FromBody] PermissionGrantChangeDto input,
            CancellationToken cancellationToken = default)
        {
            await _permissionGrantAppService.GrantToRoleAsync(roleName, input, cancellationToken);
            return Ok(new { message = "权限授予成功" });
        }

        [HttpDelete("roles/{roleName}/grants")]
        public async Task<ActionResult> RevokeFromRole(
            string roleName,
            [FromBody] PermissionGrantChangeDto input,
            CancellationToken cancellationToken = default)
        {
            await _permissionGrantAppService.RevokeFromRoleAsync(roleName, input, cancellationToken);
            return Ok(new { message = "权限撤销成功" });
        }

        [HttpGet("roles/{roleName}/grants")]
        public async Task<ActionResult<IReadOnlyList<PermissionGrantDto>>> GetRoleGrants(
            string roleName,
            CancellationToken cancellationToken = default)
        {
            var result = await _permissionGrantAppService.GetRoleGrantsAsync(roleName, cancellationToken);
            return Ok(result);
        }
    }
}
