using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.EventStore
{
    public class EventRetryService
    {
        private readonly IEventRetryStore _retryStore;
        private readonly IEventBus _eventBus;

        public EventRetryService(IEventRetryStore retryStore, IEventBus eventBus)
        {
            _retryStore = retryStore;
            _eventBus = eventBus;
        }

        public async Task ProcessRetryEventsAsync(CancellationToken cancellationToken = default)
        {
            var retryEvents = await _retryStore.GetRetryEventsAsync(cancellationToken);

            foreach (var (retryEvent, retryCount) in retryEvents)
            {
                try
                {
                    await _eventBus.PublishAsync(retryEvent, cancellationToken);
                    // 注意：这里需要修改RemoveRetryEventAsync方法的参数
                    // 暂时使用默认的Guid，后续需要修改接口设计
                    await _retryStore.RemoveRetryEventAsync(Guid.NewGuid(), cancellationToken);
                }
                catch (Exception)
                {
                    // 可以根据需要实现重试次数限制和错误处理
                    if (retryCount < 5) // 最多重试5次
                    {
                        await _retryStore.AddRetryEventAsync(retryEvent, retryCount + 1, cancellationToken);
                    }
                    else
                    {
                        // 超过重试次数，移除事件或记录到死信队列
                        // 暂时使用默认的Guid，后续需要修改接口设计
                        await _retryStore.RemoveRetryEventAsync(Guid.NewGuid(), cancellationToken);
                    }
                }
            }
        }
    }
}