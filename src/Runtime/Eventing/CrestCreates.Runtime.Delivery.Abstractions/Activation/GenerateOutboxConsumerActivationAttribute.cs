namespace CrestCreates.Runtime.Delivery.Abstractions.Activation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateOutboxConsumerActivationAttribute : Attribute { }
