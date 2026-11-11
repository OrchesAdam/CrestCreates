using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Data.Abstractions;

/// <summary>
/// 受管工作单元接收强类型领域事件源的写入入口（SDK 立即执行 Provider 使用）。
/// 仓储在写入成功后登记实体，提交后通知阶段统一发布；不新增第二事件总线。
/// </summary>
public interface IUnitOfWorkDomainEventTracker
{
    void TrackEntity(IHasDomainEvents entity);
}
