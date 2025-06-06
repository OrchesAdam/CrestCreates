using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Entities
{
    [Entity(
        GenerateRepository = true,
        GenerateAuditing = false,
        OrmProvider = "FreeSql",
        TableName = "TestOrders"
    )]
    public class TestOrder : AggregateRoot<long>
    {
        public string OrderNumber { get; private set; } = string.Empty;
        public Guid CustomerId { get; private set; }
        public decimal TotalAmount { get; private set; }
        public DateTime OrderDate { get; private set; }
        public OrderStatus Status { get; private set; }        public string Notes { get; private set; } = string.Empty;

        public TestOrder()
        {
            // ORM构造函数
        }

        public TestOrder(long id, string orderNumber, Guid customerId, decimal totalAmount)
            : base()
        {
            Id = id;
            SetOrderNumber(orderNumber);
            CustomerId = customerId;
            SetTotalAmount(totalAmount);
            OrderDate = DateTime.Now;
            Status = OrderStatus.Pending;
        }

        public void SetOrderNumber(string orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                throw new ArgumentException("订单号不能为空", nameof(orderNumber));
            
            OrderNumber = orderNumber.Trim();
        }

        public void SetTotalAmount(decimal totalAmount)
        {
            if (totalAmount < 0)
                throw new ArgumentException("总金额不能为负数", nameof(totalAmount));
            
            TotalAmount = totalAmount;
        }

        public void SetNotes(string notes)
        {
            Notes = notes?.Trim() ?? string.Empty;
        }

        public void ConfirmOrder()
        {
            if (Status != OrderStatus.Pending)
                throw new InvalidOperationException("只有待处理的订单才能确认");
            
            Status = OrderStatus.Confirmed;
        }

        public void ShipOrder()
        {
            if (Status != OrderStatus.Confirmed)
                throw new InvalidOperationException("只有已确认的订单才能发货");
            
            Status = OrderStatus.Shipped;
        }

        public void CompleteOrder()
        {
            if (Status != OrderStatus.Shipped)
                throw new InvalidOperationException("只有已发货的订单才能完成");
            
            Status = OrderStatus.Completed;
        }

        public void CancelOrder()
        {
            if (Status == OrderStatus.Completed)
                throw new InvalidOperationException("已完成的订单不能取消");
            
            Status = OrderStatus.Cancelled;
        }
    }

    public enum OrderStatus
    {
        Pending = 1,
        Confirmed = 2,
        Shipped = 3,
        Completed = 4,
        Cancelled = 5
    }
}
