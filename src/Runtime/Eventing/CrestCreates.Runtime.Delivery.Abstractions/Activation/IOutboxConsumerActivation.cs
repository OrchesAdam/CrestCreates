namespace CrestCreates.Runtime.Delivery.Abstractions.Activation;

public interface IOutboxConsumerActivation<TSelf>
    where TSelf : class, IOutboxConsumerActivation<TSelf>
{
    static abstract TSelf CreateOutboxConsumer(IServiceProvider services);
}
