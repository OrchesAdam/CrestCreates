using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// 标记实体需要生成 CRUD 服务
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateCrudServiceAttribute : Attribute
    {
        /// <summary>
        /// 是否生成 DTO 类，默认为 true
        /// </summary>
        public bool GenerateDto { get; set; } = true;

        /// <summary>
        /// 是否生成控制器，默认为 false
        /// </summary>
        public bool GenerateController { get; set; } = false;

        /// <summary>
        /// 服务路由前缀，默认为空字符串
        /// </summary>
        public string ServiceRoute { get; set; } = string.Empty;

        /// <summary>
        /// 初始化 <see cref="GenerateCrudServiceAttribute"/> 类的新实例
        /// </summary>
        public GenerateCrudServiceAttribute()
        {
        }

        /// <summary>
        /// 初始化 <see cref="GenerateCrudServiceAttribute"/> 类的新实例
        /// </summary>
        /// <param name="serviceRoute">服务路由前缀</param>
        public GenerateCrudServiceAttribute(string serviceRoute)
        {
            ServiceRoute = serviceRoute;
        }

        /// <summary>
        /// 初始化 <see cref="GenerateCrudServiceAttribute"/> 类的新实例
        /// </summary>
        /// <param name="generateController">是否生成控制器</param>
        public GenerateCrudServiceAttribute(bool generateController)
        {
            GenerateController = generateController;
        }

        /// <summary>
        /// 初始化 <see cref="GenerateCrudServiceAttribute"/> 类的新实例
        /// </summary>
        /// <param name="generateController">是否生成控制器</param>
        /// <param name="serviceRoute">服务路由前缀</param>
        public GenerateCrudServiceAttribute(bool generateController, string serviceRoute)
        {
            GenerateController = generateController;
            ServiceRoute = serviceRoute;
        }
    }
}
