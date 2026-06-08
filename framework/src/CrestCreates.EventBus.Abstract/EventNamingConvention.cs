using System;
using System.Text.RegularExpressions;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Abstract;

public static class EventNamingConvention
{
    public static string GetRoutingKey<TEvent>() where TEvent : IDomainEvent
    {
        return PascalToLowerUnderscore(typeof(TEvent).Name);
    }

    public static string GetRoutingKey(Type eventType)
    {
        return PascalToLowerUnderscore(eventType.Name);
    }

    public static string GetRoutingKey(string boundedContext, string aggregate, string action)
    {
        return $"{ToLowerKebab(boundedContext)}.{ToLowerKebab(aggregate)}.{ToLowerKebab(action)}";
    }

    public static string GetTopic<TEvent>() where TEvent : IDomainEvent
    {
        return typeof(TEvent).Name;
    }

    public static string GetTopic(Type eventType)
    {
        return eventType.Name;
    }

    public static string GetTopic(string boundedContext)
    {
        return $"{ToLowerKebab(boundedContext)}.events";
    }

    public static string GetExchange(string boundedContext)
    {
        return $"crestcreates.{ToLowerKebab(boundedContext)}.events";
    }

    public static string GetQueue(string serviceName, string routingKey)
    {
        return $"{ToLowerKebab(serviceName)}.{routingKey}";
    }

    public static string GetConsumerGroup(string serviceName, string boundedContext)
    {
        return $"{ToLowerKebab(serviceName)}.{ToLowerKebab(boundedContext)}";
    }

    public static string GetDeadLetterQueue(string queue)
    {
        return $"{queue}.dlq";
    }

    public static string GetDeadLetterTopic(string topic)
    {
        return $"{topic}.dlq";
    }

    private static readonly Regex PascalCaseRegex = new("([a-z])([A-Z])", RegexOptions.Compiled);

    private static string PascalToLowerUnderscore(string pascalCase)
    {
        return PascalCaseRegex.Replace(pascalCase, "$1_$2").ToLowerInvariant();
    }

    private static string ToLowerKebab(string input)
    {
        return input.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
    }
}