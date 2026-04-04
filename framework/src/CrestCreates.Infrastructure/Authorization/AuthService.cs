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
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CrestCreates.Infrastructure.Authorization
{
    public class JwtOptions
    {
        public string SecretKey { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int AccessTokenExpirationMinutes { get; set; } = 60;
        public int RefreshTokenExpirationDays { get; set; } = 7;
    }

    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly JwtOptions _jwtOptions;

        public AuthService(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            IOptions<JwtOptions> jwtOptions)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _jwtOptions = jwtOptions.Value;
        }

        public async Task<LoginResultDto> LoginAsync(LoginDto input)
        {
            var userWithRoles = await _userRepository.GetUserWithRolesAsync(input.UserName);
            if (userWithRoles == null)
            {
                throw new UnauthorizedAccessException("用户名或密码错误");
            }

            var user = userWithRoles.User;

            if (!user.IsActive)
            {
                throw new UnauthorizedAccessException("用户已被禁用");
            }

            if (!string.IsNullOrEmpty(input.TenantId) && user.TenantId != input.TenantId)
            {
                throw new UnauthorizedAccessException("租户不匹配");
            }

            if (string.IsNullOrEmpty(user.PasswordHash) || 
                !_passwordHasher.VerifyPassword(user.PasswordHash, input.Password))
            {
                throw new UnauthorizedAccessException("用户名或密码错误");
            }

            var permissions = await _userRepository.GetUserPermissionsAsync(user.Id);
            var roles = userWithRoles.Roles.Select(r => r.Name).ToArray();

            var accessToken = GenerateAccessToken(user, roles, permissions);
            var refreshToken = GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
            await _userRepository.UpdateAsync(user);

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
            var user = await _userRepository.GetUserByRefreshTokenAsync(input.RefreshToken);
            if (user == null)
            {
                throw new UnauthorizedAccessException("无效的刷新令牌");
            }

            if (user.RefreshTokenExpiryTime < DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("刷新令牌已过期");
            }

            var userWithRoles = await _userRepository.GetUserWithRolesAsync(user.UserName);
            var permissions = await _userRepository.GetUserPermissionsAsync(user.Id);
            var roles = userWithRoles?.Roles.Select(r => r.Name).ToArray() ?? Array.Empty<string>();

            var accessToken = GenerateAccessToken(user, roles, permissions);
            var refreshToken = GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
            await _userRepository.UpdateAsync(user);

            return new TokenDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _jwtOptions.AccessTokenExpirationMinutes * 60,
                TokenType = "Bearer"
            };
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
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
                await _userRepository.UpdateAsync(user);
            }
        }

        private string GenerateAccessToken(User user, string[] roles, List<string> permissions)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim("preferred_username", user.UserName),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim("tenantid", user.TenantId ?? string.Empty),
                new Claim("is_super_admin", user.IsSuperAdmin.ToString().ToLower())
            };

            if (user.OrganizationId.HasValue)
            {
                claims.Add(new Claim("org_id", user.OrganizationId.Value.ToString()));
            }

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission));
            }

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

        private string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }
    }
}
