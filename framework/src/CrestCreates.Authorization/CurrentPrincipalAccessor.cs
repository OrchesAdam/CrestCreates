using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Authorization.Abstractions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Threading;

namespace CrestCreates.Authorization;

public class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AsyncLocal<ClaimsPrincipal> _currentPrincipal = new();

    public CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal Principal
    {
        get
        {
            if (_currentPrincipal.Value != null)
            {
                return _currentPrincipal.Value;
            }

            return _httpContextAccessor?.HttpContext?.User ?? new ClaimsPrincipal();
        }
    }

    public IDisposable Change(ClaimsPrincipal principal)
    {
        var oldPrincipal = _currentPrincipal.Value;
        _currentPrincipal.Value = principal;

        return new DisposeAction(() =>
        {
            _currentPrincipal.Value = oldPrincipal;
        });
    }

    private class DisposeAction : IDisposable
    {
        private readonly Action _action;

        public DisposeAction(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            _action();
        }
    }
}
