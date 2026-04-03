using System;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// 标记实体需要生成仓储
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateRepositoryAttribute : Attribute
    {
        /// <summary>
        /// ORM 提供者类型，默认为 EfCore
        /// </summary>
        public OrmProvider OrmProvider { get; set; } = OrmProvider.EfCore;

        /// <summary>
        /// 是否生成仓储接口，默认为 true
        /// </summary>
        public bool GenerateInterface { get; set; } = true;

        /// <summary>
        /// 是否生成仓储实现类，默认为 true
        /// </summary>
        public bool GenerateImplementation { get; set; } = true;

        /// <summary>
        /// 初始化 <see cref="GenerateRepositoryAttribute"/> 类的新实例
        /// </summary>
        public GenerateRepositoryAttribute()
        {
        }

        /// <summary>
        /// 初始化 <see cref="GenerateRepositoryAttribute"/> 类的新实例
        /// </summary>
        /// <param name="ormProvider">ORM 提供者类型</param>
        public GenerateRepositoryAttribute(OrmProvider ormProvider)
        {
            OrmProvider = ormProvider;
        }
    }
}
