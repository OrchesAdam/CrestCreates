using System;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing;

/// <summary>
/// 带租户、组织、创建人、审计的实体接口
/// </summary>
/// <typeparam name="TId"></typeparam>
public interface IMustHaveTenantOrganization<TId> : IMustHaveTenant, IMayHaveOrganization, IHasCreator, IAuditedEntity<TId> where TId : IEquatable<TId>
{
}