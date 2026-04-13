using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Services;

public interface IIdentitySecurityLogService
{
    Task WriteAsync(
        Guid? userId,
        string? userName,
        string? tenantId,
        string action,
        bool isSucceeded,
        string? detail = null,
        CancellationToken cancellationToken = default);
}

public sealed class IdentitySecurityLogServiceImpl : IIdentitySecurityLogService
{
    private readonly IIdentitySecurityLogRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<IdentitySecurityLogServiceImpl> _logger;

    public IdentitySecurityLogServiceImpl(
        IIdentitySecurityLogRepository repository,
        IHttpContextAccessor httpContextAccessor,
        ILogger<IdentitySecurityLogServiceImpl> logger)
    {
        _repository = repository;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task WriteAsync(
        Guid? userId,
        string? userName,
        string? tenantId,
        string action,
        bool isSucceeded,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        await _repository.InsertAsync(new IdentitySecurityLog(
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
            "Identity security action {Action} for user {UserId} succeeded: {IsSucceeded}",
            action,
            userId,
            isSucceeded);
    }
}
