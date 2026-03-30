using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// 模块特性
    /// 用于标记一个类为模块，模块会被自动注册到应用程序中
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ModuleAttribute : Attribute
    {
        /// <summary>
        /// 初始化 ModuleAttribute
        /// </summary>
        public ModuleAttribute()
        {
        }

        /// <summary>
        /// 初始化 ModuleAttribute 并指定依赖的模块类型
        /// </summary>
        /// <param name="dependsOn">依赖的模块类型数组</param>
        public ModuleAttribute(params Type[] dependsOn)
        {
            DependsOn = dependsOn;
        }

        /// <summary>
        /// 依赖的模块类型
        /// 指定当前模块依赖的其他模块，这些模块将在当前模块之前初始化
        /// </summary>
        public Type[]? DependsOn { get; set; }

        /// <summary>
        /// 是否自动注册模块中的服务
        /// 默认为 true，会自动扫描并注册模块中的服务
        /// </summary>
        public bool AutoRegisterServices { get; set; } = true;

        /// <summary>
        /// 模块加载顺序优先级
        /// 数值越小优先级越高（越早加载）
        /// 默认为 0，负数表示高优先级，正数表示低优先级
        /// 注意：此优先级在满足依赖关系的前提下生效
        /// </summary>
        public int Order { get; set; } = 0;
    }
}
