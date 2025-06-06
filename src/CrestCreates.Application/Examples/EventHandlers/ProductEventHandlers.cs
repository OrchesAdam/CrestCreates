using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediatR;
using CrestCreates.Domain.Examples;

namespace CrestCreates.Application.Examples.EventHandlers
{
    public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
    {
        private readonly ILogger<ProductCreatedEventHandler> _logger;
        
        public ProductCreatedEventHandler(ILogger<ProductCreatedEventHandler> logger)
        {
            _logger = logger;
        }
        
        public Task Handle(ProductCreatedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("新产品创建: {ProductName} (ID: {ProductId})", 
                notification.ProductName, notification.ProductId);
                
            // 这里可以添加其他业务逻辑，如发送通知、更新索引等
            
            return Task.CompletedTask;
        }
    }
    
    public class ProductPriceChangedEventHandler : INotificationHandler<ProductPriceChangedEvent>
    {
        private readonly ILogger<ProductPriceChangedEventHandler> _logger;
        
        public ProductPriceChangedEventHandler(ILogger<ProductPriceChangedEventHandler> logger)
        {
            _logger = logger;
        }
        
        public Task Handle(ProductPriceChangedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("产品价格变更: ID {ProductId}, 新价格 {NewPrice}", 
                notification.ProductId, notification.NewPrice);
                
            // 这里可以添加其他业务逻辑
            
            return Task.CompletedTask;
        }
    }
    
    public class ProductOutOfStockEventHandler : INotificationHandler<ProductOutOfStockEvent>
    {
        private readonly ILogger<ProductOutOfStockEventHandler> _logger;
        
        public ProductOutOfStockEventHandler(ILogger<ProductOutOfStockEventHandler> logger)
        {
            _logger = logger;
        }
        
        public Task Handle(ProductOutOfStockEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogWarning("产品库存不足: ID {ProductId}", notification.ProductId);
            
            // 这里可以添加其他业务逻辑，如发送补货通知等
            
            return Task.CompletedTask;
        }
    }
}
