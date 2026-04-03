using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// 标记实体需要生成查询构建器
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateQueryBuilderAttribute : Attribute
    {
        /// <summary>
        /// 可过滤的属性列表，默认为 null（表示所有属性都可过滤）
        /// </summary>
        public string[]? FilterableProperties { get; set; }

        /// <summary>
        /// 可排序的属性列表，默认为 null（表示所有属性都可排序）
        /// </summary>
        public string[]? SortableProperties { get; set; }

        /// <summary>
        /// 初始化 <see cref="GenerateQueryBuilderAttribute"/> 类的新实例
        /// </summary>
        public GenerateQueryBuilderAttribute()
        {
        }

        /// <summary>
        /// 初始化 <see cref="GenerateQueryBuilderAttribute"/> 类的新实例
        /// </summary>
        /// <param name="filterableProperties">可过滤的属性列表</param>
        public GenerateQueryBuilderAttribute(params string[] filterableProperties)
        {
            FilterableProperties = filterableProperties;
        }

        /// <summary>
        /// 初始化 <see cref="GenerateQueryBuilderAttribute"/> 类的新实例
        /// </summary>
        /// <param name="filterableProperties">可过滤的属性列表</param>
        /// <param name="sortableProperties">可排序的属性列表</param>
        public GenerateQueryBuilderAttribute(string[] filterableProperties, string[] sortableProperties)
        {
            FilterableProperties = filterableProperties;
            SortableProperties = sortableProperties;
        }
    }
}
