using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class EntityAttribute : Attribute
    {
        public bool GenerateRepository { get; set; } = true;
        public bool GenerateAuditing { get; set; } = true;
        public string OrmProvider { get; set; } = "EfCore"; // EfCore, SqlSugar, FreeSql
        public string? TableName { get; set; } // 自定义表名

        /// <summary>
        /// 是否生成权限类，默认为 true
        /// </summary>
        public bool GeneratePermissions { get; set; } = true;

        /// <summary>
        /// 自定义权限列表，如果设置则使用自定义权限而非默认权限
        /// </summary>
        public string[]? CustomPermissions { get; set; }
    }
}
