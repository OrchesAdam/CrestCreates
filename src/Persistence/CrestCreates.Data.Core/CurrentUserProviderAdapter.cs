using System;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;

namespace CrestCreates.Data.Core;

/// <summary>
/// The single typed adapter from the platform <see cref="ICurrentUser"/> to the provider-facing
/// <see cref="ICurrentUserProvider"/>. Providers must not maintain a second user context.
/// </summary>
public sealed class CurrentUserProviderAdapter : ICurrentUserProvider
{
    private readonly ICurrentUser _currentUser;

    public CurrentUserProviderAdapter(ICurrentUser currentUser)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public Guid? GetCurrentUserId()
    {
        return Guid.TryParse(_currentUser.Id, out var userId) ? userId : null;
    }
}
