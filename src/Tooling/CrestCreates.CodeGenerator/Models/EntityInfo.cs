using System;
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models
{
    /// <summary>
    /// 实体信息模型，用于在源生成器之间传递实体相关数据
    /// </summary>
    public class EntityInfo
    {
        /// <summary>
        /// 实体名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 实体命名空间
        /// </summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>
        /// 实体完整类型名称（包含命名空间）
        /// </summary>
        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

        /// <summary>
        /// ID 类型名称
        /// </summary>
        public string IdType { get; set; } = "Guid";

        /// <summary>
        /// 实体属性列表
        /// </summary>
        public List<PropertyInfo> Properties { get; set; } = new List<PropertyInfo>();

        /// <summary>
        /// 基类信息
        /// </summary>
        public BaseClassInfo? BaseClass { get; set; }

        /// <summary>
        /// 是否是完全审计实体（包含软删除）
        /// </summary>
        public bool IsFullyAudited { get; set; }

        #region 特性配置

        /// <summary>
        /// 是否生成仓储
        /// </summary>
        public bool GenerateRepository { get; set; } = true;

        /// <summary>
        /// 是否生成仓储接口
        /// </summary>
        public bool GenerateRepositoryInterface { get; set; } = true;

        /// <summary>
        /// 是否生成仓储实现类
        /// </summary>
        public bool GenerateRepositoryImplementation { get; set; } = true;

        /// <summary>
        /// ORM 提供者
        /// </summary>
        public string OrmProvider { get; set; } = "EfCore";

        /// <summary>
        /// 是否生成 CRUD 服务
        /// </summary>
        public bool GenerateCrudService { get; set; } = true;

        /// <summary>
        /// 是否生成 DTO
        /// </summary>
        public bool GenerateDto { get; set; } = true;

        /// <summary>
        /// 需要排除的属性列表
        /// </summary>
        public string[]? ExcludeProperties { get; set; }

        /// <summary>
        /// 是否生成查询扩展方法
        /// </summary>
        public bool GenerateQueryExtensions { get; set; } = true;

        /// <summary>
        /// 可过滤的属性列表
        /// </summary>
        public string[]? FilterableProperties { get; set; }

        /// <summary>
        /// 可排序的属性列表
        /// </summary>
        public string[]? SortableProperties { get; set; }

        /// <summary>
        /// 是否生成控制器
        /// </summary>
        public bool GenerateController { get; set; } = false;

        /// <summary>
        /// 控制器路由
        /// </summary>
        public string? ControllerRoute { get; set; }

        /// <summary>
        /// 是否生成基类
        /// </summary>
        public bool GenerateAsBaseClass { get; set; } = true;

        /// <summary>
        /// 是否启用事务切面
        /// </summary>
        public bool EnableTransaction { get; set; } = true;

        /// <summary>
        /// 是否启用日志切面
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// 是否启用验证切面
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// 是否启用缓存切面
        /// </summary>
        public bool EnableCaching { get; set; } = false;

        /// <summary>
        /// 自定义切面类型名称数组
        /// </summary>
        public string[]? CustomMoAttributes { get; set; }

        #endregion
    }

    /// <summary>
    /// 属性信息
    /// </summary>
    public class PropertyInfo
    {
        /// <summary>
        /// 属性名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 属性类型名称
        /// </summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>
        /// 是否是可空类型
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// 是否是字符串类型
        /// </summary>
        public bool IsString { get; set; }

        /// <summary>
        /// 是否是数值类型
        /// </summary>
        public bool IsNumeric { get; set; }

        /// <summary>
        /// 是否是日期时间类型
        /// </summary>
        public bool IsDateTime { get; set; }

        /// <summary>
        /// 是否是枚举类型
        /// </summary>
        public bool IsEnum { get; set; }

        /// <summary>
        /// 是否是集合类型
        /// </summary>
        public bool IsCollection { get; set; }

        /// <summary>
        /// 是否可以过滤
        /// </summary>
        public bool CanFilter => !IsCollection;

        /// <summary>
        /// 是否可以排序
        /// </summary>
        public bool CanSort => !IsCollection;
    }

    /// <summary>
    /// 基类信息
    /// </summary>
    public class BaseClassInfo
    {
        /// <summary>
        /// 基类名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 基类完整类型名称
        /// </summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>
        /// 是否是审计实体
        /// </summary>
        public bool IsAudited { get; set; }

        /// <summary>
        /// 是否是聚合根
        /// </summary>
        public bool IsAggregateRoot { get; set; }

        /// <summary>
        /// 是否支持软删除
        /// </summary>
        public bool IsSoftDelete { get; set; }
    }
}
