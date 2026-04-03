using System;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// 统一的实体特性，用于标记实体需要生成相关代码
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateEntityAttribute : Attribute
    {
        #region Repository 配置

        /// <summary>
        /// 是否生成仓储，默认为 true
        /// </summary>
        public bool GenerateRepository { get; set; } = true;

        /// <summary>
        /// 是否生成仓储接口，默认为 true
        /// </summary>
        public bool GenerateRepositoryInterface { get; set; } = true;

        /// <summary>
        /// 是否生成仓储实现类，默认为 true
        /// </summary>
        public bool GenerateRepositoryImplementation { get; set; } = true;

        /// <summary>
        /// ORM 提供者类型，默认为 EfCore
        /// </summary>
        public OrmProvider OrmProvider { get; set; } = OrmProvider.EfCore;

        #endregion

        #region CRUD Service 配置

        /// <summary>
        /// 是否生成 CRUD 服务，默认为 true
        /// </summary>
        public bool GenerateCrudService { get; set; } = true;

        /// <summary>
        /// 是否生成 DTO 类，默认为 true
        /// </summary>
        public bool GenerateDto { get; set; } = true;

        /// <summary>
        /// 需要排除的属性列表（用于 DTO 生成）
        /// </summary>
        public string[]? ExcludeProperties { get; set; }

        #endregion

        #region Query 配置

        /// <summary>
        /// 是否生成查询扩展方法，默认为 true
        /// </summary>
        public bool GenerateQueryExtensions { get; set; } = true;

        /// <summary>
        /// 可过滤的属性列表，默认为 null（表示所有属性都可过滤）
        /// </summary>
        public string[]? FilterableProperties { get; set; }

        /// <summary>
        /// 可排序的属性列表，默认为 null（表示所有属性都可排序）
        /// </summary>
        public string[]? SortableProperties { get; set; }

        #endregion

        #region Controller 配置

        /// <summary>
        /// 是否生成控制器，默认为 false
        /// </summary>
        public bool GenerateController { get; set; } = false;

        /// <summary>
        /// 控制器路由
        /// </summary>
        public string? ControllerRoute { get; set; }

        #endregion

        #region 基类模式配置

        /// <summary>
        /// 是否生成基类而非完整实现，默认为 true
        /// </summary>
        public bool GenerateAsBaseClass { get; set; } = true;

        #endregion

        #region AOP 配置

        /// <summary>
        /// 是否启用事务切面，默认为 true
        /// </summary>
        public bool EnableTransaction { get; set; } = true;

        /// <summary>
        /// 是否启用日志切面，默认为 true
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// 是否启用验证切面，默认为 true
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// 是否启用缓存切面，默认为 false
        /// </summary>
        public bool EnableCaching { get; set; } = false;

        /// <summary>
        /// 自定义切面类型数组
        /// </summary>
        public Type[]? CustomMoAttributes { get; set; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化 <see cref="GenerateEntityAttribute"/> 类的新实例
        /// </summary>
        public GenerateEntityAttribute()
        {
        }

        /// <summary>
        /// 初始化 <see cref="GenerateEntityAttribute"/> 类的新实例
        /// </summary>
        /// <param name="ormProvider">ORM 提供者类型</param>
        public GenerateEntityAttribute(OrmProvider ormProvider)
        {
            OrmProvider = ormProvider;
        }

        #endregion
    }
}
