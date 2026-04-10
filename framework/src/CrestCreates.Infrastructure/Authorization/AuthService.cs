using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Auth;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CrestCreates.Infrastructure.Authorization
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IIdentitySecurityLogRepository _identitySecurityLogRepository;
        private readonly IPermissionGrantManager _permissionGrantManager;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IIdentityClaimsBuilder _identityClaimsBuilder;
        private readonly ICurrentUser _currentUser;
        private readonly ICurrentTenant _currentTenant;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IdentityAuthenticationOptions _identityOptions;
        private readonly JwtOptions _jwtOptions;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IIdentitySecurityLogRepository identitySecurityLogRepository,
            IPermissionGrantManager permissionGrantManager,
            IPasswordHasher passwordHasher,
            IIdentityClaimsBuilder identityClaimsBuilder,
            ICurrentUser currentUser,
            ICurrentTenant currentTenant,
            IHttpContextAccessor httpContextAccessor,
            IOptions<JwtOptions> jwtOptions,
            IOptions<IdentityAuthenticationOptions> identityOptions,
            ILogger<AuthService> logger)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _identitySecurityLogRepository = identitySecurityLogRepository;
            _permissionGrantManager = permissionGrantManager;
            _passwordHasher = passwordHasher;
            _identityClaimsBuilder = identityClaimsBuilder;
            _currentUser = currentUser;
            _currentTenant = currentTenant;
            _httpContextAccessor = httpContextAccessor;
            _jwtOptions = jwtOptions.Value;
            _identityOptions = identityOptions.Value;
            _logger = logger;
        }

        public async Task<LoginResultDto> LoginAsync(LoginDto input)
        {
            var normalizedUserName = NormalizeRequired(input.UserName, nameof(input.UserName));
            var normalizedPassword = NormalizeRequired(input.Password, nameof(input.Password));
            var normalizedTenantId = NormalizeOptional(input.TenantId);

            var user = await _userRepository.FindByUserNameAsync(normalizedUserName);
            if (user == null)
            {
                await WriteSecurityLogAsync(
                    userId: null,
                    userName: normalizedUserName,
                    tenantId: normalizedTenantId,
                    action: "Login",
                    isSucceeded: false,
                    detail: "用户名或密码错误");
                throw new UnauthorizedAccessException("用户名或密码错误");
            }

            if (!string.IsNullOrWhiteSpace(_currentTenant.Id) &&
                !string.Equals(user.TenantId, _currentTenant.Id, StringComparison.Ordinal))
            {
                await WriteSecurityLogAsync(
                    user.Id,
                    user.UserName,
                    user.TenantId,
                    "Login",
                    false,
                    "租户不匹配");
                throw new UnauthorizedAccessException("租户不匹配");
            }

            if (!user.IsActive)
            {
                await WriteSecurityLogAsync(
                    user.Id,
                    user.UserName,
                    user.TenantId,
                    "Login",
                    false,
                    "用户已被禁用");
                throw new UnauthorizedAccessException("用户已被禁用");
            }

            if (IsLockedOut(user))
            {
                await WriteSecurityLogAsync(
                    user.Id,
                    user.UserName,
                    user.TenantId,
                    "Login",
                    false,
                    "用户已被锁定");
                throw new UnauthorizedAccessException("用户已被锁定");
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                !_passwordHasher.VerifyPassword(user.PasswordHash, normalizedPassword))
            {
                await HandleFailedLoginAsync(user);
                throw new UnauthorizedAccessException("用户名或密码错误");
            }

            var roles = await GetRoleNamesAsync(user.Id);
            var permissions = await _permissionGrantManager.GetEffectivePermissionsAsync(
                user.Id.ToString(),
                roles,
                user.TenantId);

            var accessToken = GenerateAccessToken(_identityClaimsBuilder.Build(user, roles, permissions));
            var refreshToken = await RotateRefreshTokenAsync(user);

            user.AccessFailedCount = 0;
            user.LockoutEndTime = null;
            user.LastLoginTime = DateTime.UtcNow;
            user.LastModificationTime = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);

            await WriteSecurityLogAsync(
                user.Id,
                user.UserName,
                user.TenantId,
                "Login",
                true,
                null);

            return new LoginResultDto
            {
                Token = new TokenDto
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresIn = _jwtOptions.AccessTokenExpirationMinutes * 60,
                    TokenType = "Bearer"
                },
                User = new UserInfoDto
                {
                    Id = user.Id,
                    UserName = user.UserName,
                    Email = user.Email,
                    Phone = user.Phone,
                    TenantId = user.TenantId,
                    OrganizationId = user.OrganizationId,
                    IsSuperAdmin = user.IsSuperAdmin,
                    Roles = roles
                }
            };
        }

        public async Task<TokenDto> RefreshTokenAsync(RefreshTokenDto input)
        {
            var refreshTokenValue = NormalizeRequired(input.RefreshToken, nameof(input.RefreshToken));
            var currentToken = await _refreshTokenRepository.FindByTokenAsync(refreshTokenValue);
            if (currentToken == null || currentToken.RevokedTime.HasValue || currentToken.ExpirationTime <= DateTime.UtcNow)
            {
                await WriteSecurityLogAsync(
                    userId: null,
                    userName: null,
                    tenantId: null,
                    action: "RefreshToken",
                    isSucceeded: false,
                    detail: "无效的刷新令牌");
                throw new UnauthorizedAccessException("无效的刷新令牌");
            }

            var user = await _userRepository.GetAsync(currentToken.UserId);
            if (user == null || !user.IsActive)
            {
                await WriteSecurityLogAsync(
                    currentToken.UserId,
                    null,
                    currentToken.TenantId,
                    "RefreshToken",
                    false,
                    "用户不存在或已禁用");
                throw new UnauthorizedAccessException("用户不存在或已禁用");
            }

            if (IsLockedOut(user))
            {
                await WriteSecurityLogAsync(
                    user.Id,
                    user.UserName,
                    user.TenantId,
                    "RefreshToken",
                    false,
                    "用户已被锁定");
                throw new UnauthorizedAccessException("用户已被锁定");
            }

            var roles = await GetRoleNamesAsync(user.Id);
            var permissions = await _permissionGrantManager.GetEffectivePermissionsAsync(
                user.Id.ToString(),
                roles,
                user.TenantId);

            var accessToken = GenerateAccessToken(_identityClaimsBuilder.Build(user, roles, permissions));
            var refreshToken = await RotateRefreshTokenAsync(user);

            await WriteSecurityLogAsync(
                user.Id,
                user.UserName,
                user.TenantId,
                "RefreshToken",
                true,
                null);

            return new TokenDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _jwtOptions.AccessTokenExpirationMinutes * 60,
                TokenType = "Bearer"
            };
        }

        public Task<UserInfoDto> GetCurrentUserAsync()
        {
            if (!_currentUser.IsAuthenticated)
            {
                throw new UnauthorizedAccessException("当前用户未认证");
            }

            var currentUser = new UserInfoDto
            {
                Id = Guid.TryParse(_currentUser.Id, out var userId) ? userId : Guid.Empty,
                UserName = _currentUser.UserName,
                Email = _currentUser.FindClaimValue(ClaimTypes.Email),
                Phone = _currentUser.FindClaimValue(ClaimTypes.MobilePhone),
                TenantId = NormalizeOptional(_currentUser.TenantId),
                OrganizationId = _currentUser.OrganizationId,
                IsSuperAdmin = _currentUser.IsSuperAdmin,
                Roles = _currentUser.Roles
            };

            return Task.FromResult(currentUser);
        }

        public Task<bool> ValidateTokenAsync(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtOptions.SecretKey);

            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public async Task RevokeTokenAsync(string userId)
        {
            if (!Guid.TryParse(userId, out var id))
            {
                throw new ArgumentException("无效的用户ID");
            }

            var user = await _userRepository.GetAsync(id);
            if (user != null)
            {
                await _refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);
                await WriteSecurityLogAsync(
                    user.Id,
                    user.UserName,
                    user.TenantId,
                    "Logout",
                    true,
                    null);
            }
        }

        private async Task HandleFailedLoginAsync(User user)
        {
            user.AccessFailedCount += 1;
            user.LastModificationTime = DateTime.UtcNow;

            if (user.LockoutEnabled && user.AccessFailedCount >= _identityOptions.MaxAccessFailedCount)
            {
                user.LockoutEndTime = DateTime.UtcNow.AddMinutes(_identityOptions.LockoutMinutes);
            }

            await _userRepository.UpdateAsync(user);
            await WriteSecurityLogAsync(
                user.Id,
                user.UserName,
                user.TenantId,
                "Login",
                false,
                user.LockoutEndTime.HasValue && user.LockoutEndTime > DateTime.UtcNow
                    ? "密码错误次数过多，用户已锁定"
                    : "用户名或密码错误");
        }

        private async Task<string[]> GetRoleNamesAsync(Guid userId)
        {
            var roles = await _roleRepository.GetByUserIdAsync(userId);
            return roles
                .Where(role => !string.IsNullOrWhiteSpace(role.Name))
                .Select(role => role.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<string> RotateRefreshTokenAsync(User user)
        {
            var refreshTokenValue = GenerateRefreshToken();

            await _refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);
            await _refreshTokenRepository.InsertAsync(
                new RefreshToken(
                    Guid.NewGuid(),
                    user.Id,
                    refreshTokenValue,
                    DateTime.UtcNow,
                    DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
                    user.TenantId));

            return refreshTokenValue;
        }

        private async Task WriteSecurityLogAsync(
            Guid? userId,
            string? userName,
            string? tenantId,
            string action,
            bool isSucceeded,
            string? detail)
        {
            await _identitySecurityLogRepository.InsertAsync(new IdentitySecurityLog(
                Guid.NewGuid(),
                action,
                isSucceeded,
                DateTime.UtcNow)
            {
                UserId = userId,
                UserName = userName,
                TenantId = tenantId,
                Detail = detail,
                ClientIpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
            });

            _logger.LogInformation(
                "Identity action {Action} for user {UserId} succeeded: {IsSucceeded}",
                action,
                userId,
                isSucceeded);
        }

        private string GenerateAccessToken(IEnumerable<Claim> claims)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static bool IsLockedOut(User user)
        {
            return user.LockoutEndTime.HasValue && user.LockoutEndTime.Value > DateTime.UtcNow;
        }

        private static string NormalizeRequired(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("参数不能为空", parameterName);
            }

            return value.Trim();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }
    }
}
