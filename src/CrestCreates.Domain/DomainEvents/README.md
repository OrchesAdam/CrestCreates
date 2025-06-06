# Domain Events

This directory contains the domain events for the CrestCreates application. Domain events are used to represent significant occurrences within the domain that other parts of the system may be interested in. 

## Key Concepts

- **Domain Events**: These are events that signify a change in the state of the domain model. They are typically used to trigger side effects in other parts of the application.
- **Event Handlers**: These are components that listen for domain events and execute logic in response to those events.

## Usage

When a significant change occurs in the domain, a domain event should be raised. Other components can subscribe to these events to perform actions such as updating projections, sending notifications, or triggering workflows.

## Example

```csharp
public class OrderCreatedEvent : INotification
{
    public int OrderId { get; }
    public DateTime CreatedAt { get; }

    public OrderCreatedEvent(int orderId)
    {
        OrderId = orderId;
        CreatedAt = DateTime.UtcNow;
    }
}
```

In this example, an `OrderCreatedEvent` is defined to represent the creation of an order. Other parts of the system can listen for this event and respond accordingly.