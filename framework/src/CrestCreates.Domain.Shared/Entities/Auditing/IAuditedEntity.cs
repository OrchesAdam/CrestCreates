using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing
{
    /// <summary>
    /// 审计实体接口（非泛型基接口）
    /// </summary>
    public interface IAuditedEntity : IHasCreator
    {
        /// <summary>
        /// 创建时间
        /// </summary>
        DateTime CreationTime { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        DateTime? LastModificationTime { get; set; }

        /// <summary>
        /// 最后修改人ID
        /// </summary>
        Guid? LastModifierId { get; set; }
    }

    /// <summary>
    /// 审计实体接口（泛型版本）
    /// </summary>
    public interface IAuditedEntity<TId> : IAuditedEntity, IEntity<TId> where TId : IEquatable<TId>
    {
    }
}
