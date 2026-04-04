using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.OrmProviders.Abstract.Abstractions
{
    /// <summary>
    /// 软删除接口
    /// </summary>
    public interface ISoftDelete<TId> : IEntity<TId> where TId : IEquatable<TId>
    {
        /// <summary>
        /// 是否已删除
        /// </summary>
        bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        DateTime? DeletionTime { get; set; }

        /// <summary>
        /// 删除人ID
        /// </summary>
        Guid? DeleterId { get; set; }
    }
}
