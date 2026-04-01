using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using CrestCreates.Identity.Services;

namespace CrestCreates.Identity.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IIdentityService _identityService;

        public AuthController(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _identityService.LoginAsync(request.Email, request.Password);
            if (!result.Success)
            {
                return Unauthorized("Invalid email or password");
            }

            return Ok(new LoginResponse
            {
                Token = result.Token,
                RefreshToken = result.RefreshToken,
                User = new UserDto
                {
                    Id = result.User.Id,
                    FirstName = result.User.FirstName,
                    LastName = result.User.LastName,
                    Email = result.User.Email
                }
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var result = await _identityService.RegisterAsync(request.FirstName, request.LastName, request.Email, request.Password);
            if (!result.Success)
            {
                return BadRequest("Registration failed");
            }

            return Ok(new RegisterResponse
            {
                User = new UserDto
                {
                    Id = result.User.Id,
                    FirstName = result.User.FirstName,
                    LastName = result.User.LastName,
                    Email = result.User.Email
                }
            });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var result = await _identityService.RefreshTokenAsync(request.RefreshToken);
            if (!result.Success)
            {
                return Unauthorized("Invalid refresh token");
            }

            return Ok(new RefreshTokenResponse
            {
                Token = result.Token,
                RefreshToken = result.RefreshToken
            });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var token = await _identityService.GeneratePasswordResetTokenAsync(request.Email);
            if (token == null)
            {
                return BadRequest("Email not found");
            }

            // 这里应该发送邮件给用户，包含重置密码的链接
            // 暂时返回token，实际应用中应该通过邮件发送
            return Ok(new ForgotPasswordResponse
            {
                Token = token
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var result = await _identityService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
            if (!result)
            {
                return BadRequest("Password reset failed");
            }

            return Ok("Password reset successfully");
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var result = await _identityService.ChangePasswordAsync(request.UserId, request.CurrentPassword, request.NewPassword);
            if (!result)
            {
                return BadRequest("Password change failed");
            }

            return Ok("Password changed successfully");
        }
    }

    // 请求和响应模型
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public UserDto User { get; set; }
    }

    public class RegisterRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RegisterResponse
    {
        public UserDto User { get; set; }
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; }
    }

    public class RefreshTokenResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; }
    }

    public class ForgotPasswordResponse
    {
        public string Token { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    public class ChangePasswordRequest
    {
        public string UserId { get; set; }
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }

    public class UserDto
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
    }
}