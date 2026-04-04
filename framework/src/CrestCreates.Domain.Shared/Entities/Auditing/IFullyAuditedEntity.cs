using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing;

/// <summary>
/// 完整审计实体接口（非泛型基接口）
/// </summary>
public interface IFullyAuditedEntity : IAuditedEntity, ISoftDelete
{
}

/// <summary>
/// 完整审计实体接口（泛型版本）
/// </summary>
public interface IFullyAuditedEntity<TId> : IFullyAuditedEntity, IAuditedEntity<TId> where TId : IEquatable<TId>
{
}