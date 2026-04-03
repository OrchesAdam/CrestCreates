namespace CrestCreates.Domain.Shared.Enums
{
    /// <summary>
    /// ORM 提供者类型
    /// </summary>
    public enum OrmProvider
    {
        /// <summary>
        /// Entity Framework Core
        /// </summary>
        EfCore = 0,

        /// <summary>
        /// SqlSugar
        /// </summary>
        SqlSugar = 1,

        /// <summary>
        /// FreeSql
        /// </summary>
        FreeSql = 2
    }
}
