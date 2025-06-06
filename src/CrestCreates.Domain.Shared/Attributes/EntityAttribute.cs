using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class EntityAttribute : Attribute
    {
        public bool GenerateRepository { get; set; } = true;
        public bool GenerateAuditing { get; set; } = true;
        public string OrmProvider { get; set; } = "EfCore"; // EfCore, SqlSugar, FreeSql
        public string TableName { get; set; } // 自定义表名
    }
}
