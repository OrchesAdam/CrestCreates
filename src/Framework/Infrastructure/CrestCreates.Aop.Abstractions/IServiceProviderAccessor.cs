using System;

namespace CrestCreates.Aop.Abstractions;

/// <summary>
/// Contract implemented by every type whose methods are woven by the CrestCreates interceptors
/// (<c>[UnitOfWorkMo]</c>, <c>[CacheMo]</c>, <c>[PermissionMo]</c>). It hands the owning scope's
/// service provider to the interceptor without reflecting over the target.
/// </summary>
/// <remarks>
/// The formal CRUD services and <c>CrestAppServiceBase</c> expose this member as part of the
/// intercept contract: a woven type that does not implement it cannot reach the kernel, and the
/// interceptor fails closed instead of running the business method unmanaged.
/// </remarks>
public interface IServiceProviderAccessor
{
    IServiceProvider? ServiceProvider { get; }
}
