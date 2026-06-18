using System;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Services
{
    /// <summary>
    /// 订单服务 - 测试自动生成服务接口功能
    /// 这个服务没有显式定义接口，ServiceGenerator 会自动生成 IOrderService 接口
    /// </summary>
    [CrestService(
        Lifetime = CrestServiceAttribute.ServiceLifetime.Scoped,
        GenerateController = true,
        Route = "api/orders"
    )]
    public class OrderService
    {
        public async Task<OrderDto> GetByIdAsync(Guid id)
        {
            await Task.CompletedTask;
            return new OrderDto { Id = id, OrderNumber = "ORD-001" };
        }

        public async Task<OrderDto> CreateAsync(CreateOrderDto dto)
        {
            await Task.CompletedTask;
            return new OrderDto 
            { 
                Id = Guid.NewGuid(), 
                OrderNumber = dto.OrderNumber,
                Amount = dto.Amount 
            };
        }

        public async Task<bool> CancelAsync(Guid id)
        {
            await Task.CompletedTask;
            return true;
        }
    }

    #region DTOs

    public class OrderDto
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateOrderDto
    {
        public string OrderNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    #endregion
}
