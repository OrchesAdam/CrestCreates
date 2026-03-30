using System;

namespace CrestCreates.Domain.Entities;

/// <summary>
/// 审计实体接口
/// </summary>
public interface IAuditedEntity<TId> : IEntity<TId>    where TId : IEquatable<TId>
{
    /// <summary>
    /// 创建时间
    /// </summary>
    DateTime CreationTime { get; set; }

    /// <summary>
    /// 创建人ID
    /// </summary>
    Guid? CreatorId { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    DateTime? LastModificationTime { get; set; }

    /// <summary>
    /// 最后修改人ID
    /// </summary>
    Guid? LastModifierId { get; set; }
}