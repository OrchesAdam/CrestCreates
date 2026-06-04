using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// ORM 提供者类型枚举
    /// </summary>
    public enum OrmProvider
    {
        /// <summary>
        /// Entity Framework Core
        /// </summary>
        EfCore,

        /// <summary>
        /// SqlSugar ORM
        /// </summary>
        SqlSugar,

        /// <summary>
        /// FreeSql ORM
        /// </summary>
        FreeSql
    }
}
