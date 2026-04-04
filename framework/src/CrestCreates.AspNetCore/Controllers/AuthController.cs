using System;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Auth;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.AspNetCore.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto input)
        {
            try
            {
                var result = await _authService.LoginAsync(input);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("refresh-token")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenDto>> RefreshToken([FromBody] RefreshTokenDto input)
        {
            try
            {
                var result = await _authService.RefreshTokenAsync(input);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<ActionResult> Logout()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await _authService.RevokeTokenAsync(userId);
            }
            return Ok(new { message = "登出成功" });
        }

        [HttpGet("me")]
        [Authorize]
        public ActionResult<UserInfoDto> GetCurrentUser()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var tenantId = User.FindFirst("tenantid")?.Value;
            var orgId = User.FindFirst("org_id")?.Value;
            var isSuperAdmin = User.FindFirst("is_super_admin")?.Value;
            var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(r => r.Value).ToArray();

            return Ok(new UserInfoDto
            {
                Id = Guid.TryParse(userId, out var id) ? id : Guid.Empty,
                UserName = userName ?? string.Empty,
                Email = email ?? string.Empty,
                TenantId = tenantId,
                OrganizationId = Guid.TryParse(orgId, out var org) ? org : null,
                IsSuperAdmin = bool.TryParse(isSuperAdmin, out var admin) && admin,
                Roles = roles
            });
        }

        [HttpPost("validate")]
        [AllowAnonymous]
        public async Task<ActionResult> ValidateToken([FromBody] ValidateTokenRequest request)
        {
            var isValid = await _authService.ValidateTokenAsync(request.Token);
            return Ok(new { isValid });
        }
    }

    public class ValidateTokenRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
