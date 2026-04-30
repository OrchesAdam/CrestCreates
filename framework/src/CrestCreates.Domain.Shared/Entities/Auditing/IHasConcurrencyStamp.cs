namespace CrestCreates.Domain.Shared.Entities.Auditing;

/// <summary>
/// 并发戳接口，用于乐观并发控制
/// </summary>
public interface IHasConcurrencyStamp
{
    /// <summary>
    /// 并发戳，每次实体更新时自动变更
    /// </summary>
    string ConcurrencyStamp { get; set; }
}
