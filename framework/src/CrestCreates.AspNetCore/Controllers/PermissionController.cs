using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.AspNetCore.Controllers
{
    [ApiController]
    [Route("api/permissions")]
    [Authorize]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionGrantRepository _permissionGrantRepository;

        public PermissionController(IPermissionGrantRepository permissionGrantRepository)
        {
            _permissionGrantRepository = permissionGrantRepository;
        }

        [HttpPost("grant/role")]
        public async Task<ActionResult> GrantToRole([FromBody] GrantPermissionRequest request)
        {
            var grant = new PermissionGrant(
                Guid.NewGuid(),
                request.PermissionName,
                PermissionGrant.ProviderNames.Role,
                request.ProviderKey,
                request.TenantId);

            await _permissionGrantRepository.InsertAsync(grant);
            return Ok(new { message = "权限授予成功" });
        }

        [HttpPost("grant/user")]
        public async Task<ActionResult> GrantToUser([FromBody] GrantPermissionRequest request)
        {
            var grant = new PermissionGrant(
                Guid.NewGuid(),
                request.PermissionName,
                PermissionGrant.ProviderNames.User,
                request.ProviderKey,
                request.TenantId);

            await _permissionGrantRepository.InsertAsync(grant);
            return Ok(new { message = "权限授予成功" });
        }

        [HttpDelete("revoke")]
        public async Task<ActionResult> RevokePermission([FromBody] RevokePermissionRequest request)
        {
            var grant = await _permissionGrantRepository.FindAsync(
                request.PermissionName,
                request.ProviderName,
                request.ProviderKey);

            if (grant != null)
            {
                await _permissionGrantRepository.DeleteAsync(grant);
            }

            return Ok(new { message = "权限撤销成功" });
        }

        [HttpGet("role/{roleId}")]
        public async Task<ActionResult<List<PermissionGrant>>> GetRolePermissions(string roleId)
        {
            var grants = await _permissionGrantRepository.GetListByProviderAsync(
                PermissionGrant.ProviderNames.Role,
                roleId);
            return Ok(grants);
        }

        [HttpGet("user/{userId}")]
        public async Task<ActionResult<List<PermissionGrant>>> GetUserPermissions(string userId)
        {
            var grants = await _permissionGrantRepository.GetListByProviderAsync(
                PermissionGrant.ProviderNames.User,
                userId);
            return Ok(grants);
        }
    }

    public class GrantPermissionRequest
    {
        public string PermissionName { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string ProviderKey { get; set; } = string.Empty;
        public string? TenantId { get; set; }
    }

    public class RevokePermissionRequest
    {
        public string PermissionName { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string ProviderKey { get; set; } = string.Empty;
    }
}
