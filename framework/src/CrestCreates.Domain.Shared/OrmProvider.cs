namespace CrestCreates.Domain.Shared
{
    /// <summary>
    /// ORM 提供者类型
    /// </summary>
    public enum OrmProvider
    {
        /// <summary>
        /// Entity Framework Core
        /// </summary>
        EfCore,
        
        /// <summary>
        /// SqlSugar
        /// </summary>
        SqlSugar,
        
        /// <summary>
        /// FreeSql
        /// </summary>
        FreeSql
    }
}