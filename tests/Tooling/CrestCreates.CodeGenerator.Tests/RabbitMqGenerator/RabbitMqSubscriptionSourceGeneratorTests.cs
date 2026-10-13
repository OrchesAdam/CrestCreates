using System.Linq;
using CrestCreates.CodeGenerator.RabbitMqGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.RabbitMqGenerator;

/// <summary>
/// RabbitMqSubscriptionSourceGenerator: full-symbol attribute matching, exact handler signature
/// validation, closed typed invokers (the canonical name is never spliced into a type position) and the
/// explicit AddRabbitMqSubscriptions() activation.
/// </summary>
public class RabbitMqSubscriptionSourceGeneratorTests
{
    private static readonly string[] References =
        ["CrestCreates.Domain.Shared", "CrestCreates.EventBus.RabbitMQ",
         "Microsoft.Extensions.DependencyInjection.Abstractions"];

    private const string ValidHandler = """
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;
        using MyApp.Orders;

        namespace MyApp.Orders;

        public sealed class OrderCreatedEvent
        {
            public string OrderId { get; set; } = string.Empty;
        }

        public sealed class OrderCreatedHandler
        {
            [RabbitMqSubscribe("orders.order-created", Version = 1, RoutingKey = "orders.order-created",
                Exchange = "crestcreates.events", Queue = "crestcreates.orders", PrefetchCount = 20)]
            public Task HandleOrderCreatedAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }
        """;

    [Fact]
    public void Valid_handler_generates_closed_binding_and_compiles()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            ValidHandler,
            additionalReferences: References);

        result.GetErrors().Should().BeEmpty(Describe(result));
        result.CompilationSuccess.Should().BeTrue(Describe(result));

        var generated = result.GetSourceByFileName("RabbitMqSubscriptionRegistration.g.cs")!.SourceText;
        generated.Should().Contain("IRabbitMqRegistrationContributor");
        generated.Should().Contain("EventName = \"orders.order-created\"");
        generated.Should().Contain("Version = 1");
        generated.Should().Contain("PayloadType = typeof(global::MyApp.Orders.OrderCreatedEvent)");
        generated.Should().Contain("HandlerType = typeof(global::MyApp.Orders.OrderCreatedHandler)");
        generated.Should().Contain("HandlerMethod = \"HandleOrderCreatedAsync\"");
        generated.Should().Contain("RoutingKey = \"orders.order-created\"");
        generated.Should().Contain("Exchange = \"crestcreates.events\"");
        generated.Should().Contain("Queue = \"crestcreates.orders\"");
        generated.Should().Contain("PrefetchCount = 20");
        generated.Should().Contain("AddRabbitMqSubscriptions");
        generated.Should().Contain("TryAddScoped");
        generated.Should().NotContain("Type.GetType");
        generated.Should().NotContain("GetMethod");
    }

    [Fact]
    public void Canonical_name_never_appears_in_a_type_position()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            ValidHandler,
            additionalReferences: References);

        var generated = result.GetSourceByFileName("RabbitMqSubscriptionRegistration.g.cs")!.SourceText;
        generated.Should().NotContain("(orders.order-created)");
        generated.Should().Contain("(global::MyApp.Orders.OrderCreatedEvent)payload");
    }

    [Fact]
    public void Same_short_name_attribute_from_another_namespace_is_ignored()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Other.Transport
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class RabbitMqSubscribeAttribute : Attribute
                {
                    public RabbitMqSubscribeAttribute(string eventType) { }
                }
            }

            namespace MyApp
            {
                using Other.Transport;

                public sealed class Payload { }

                public sealed class Handler
                {
                    [RabbitMqSubscribe("orders.order-created")]
                    public Task HandleAsync(Payload @event, CancellationToken cancellationToken) => Task.CompletedTask;
                }
            }
            """,
            additionalReferences: References);

        result.GetSourceByFileName("RabbitMqSubscriptionRegistration.g.cs").Should().BeNull();
    }

    [Theory]
    [InlineData("public void Handle(Payload @event, CancellationToken cancellationToken) { }", "must return exactly Task")]
    [InlineData("public Task Handle(Payload @event) => Task.CompletedTask;", "exact signature")]
    [InlineData("public Task Handle(Payload @event, CancellationToken cancellationToken, int extra) => Task.CompletedTask;", "exact signature")]
    [InlineData("public static Task Handle(Payload @event, CancellationToken cancellationToken) => Task.CompletedTask;", "instance method")]
    public void Invalid_signatures_report_CCCRMQ001(string method, string expected)
    {
        var source = $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public sealed class Payload { }

            public sealed class Handler
            {
                [RabbitMqSubscribe("orders.order-created")]
                {{method}}
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            source,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCRMQ001" && d.GetMessage().Contains(expected),
            Describe(result));
    }

    [Fact]
    public void Open_generic_handler_reports_CCCRMQ004()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public sealed class Payload { }

            public sealed class Handler<T>
            {
                [RabbitMqSubscribe("orders.order-created")]
                public Task HandleAsync(Payload @event, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCRMQ004", Describe(result));
    }

    [Fact]
    public void Private_handler_reports_CCCRMQ002()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public sealed class Payload { }

            public sealed class Handler
            {
                [RabbitMqSubscribe("orders.order-created")]
                private Task HandleAsync(Payload @event, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCRMQ002", Describe(result));
    }

    [Fact]
    public void Out_of_range_prefetch_reports_CCCRMQ005()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public sealed class Payload { }

            public sealed class Handler
            {
                [RabbitMqSubscribe("orders.order-created", PrefetchCount = 70000)]
                public Task HandleAsync(Payload @event, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCRMQ005", Describe(result));
    }

    [Fact]
    public void Empty_routing_key_override_reports_CCCRMQ005()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<RabbitMqSubscriptionSourceGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            public sealed class Payload { }

            public sealed class Handler
            {
                [RabbitMqSubscribe("orders.order-created", RoutingKey = "")]
                public Task HandleAsync(Payload @event, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCRMQ005", Describe(result));
    }

    [Fact]
    public void Cross_assembly_handler_binds_payload_from_referenced_contracts_image()
    {
        var contractsImage = SourceGeneratorTestHelper.EmitAssemblyImage(
            "CrossContracts",
            [
                """
                namespace Cross.Contracts;

                public sealed class CrossEvent
                {
                    public string OrderId { get; set; } = string.Empty;
                }
                """,
            ]);

        var result = SourceGeneratorTestHelper.RunGenerators(
            "CrossHandlers",
            [
                """
                using System.Threading;
                using System.Threading.Tasks;
                using CrestCreates.Domain.Shared.Attributes;
                using Cross.Contracts;

                namespace Cross.Handlers;

                public sealed class CrossEventHandler
                {
                    [RabbitMqSubscribe("cross.order-created")]
                    public Task HandleAsync(CrossEvent @event, CancellationToken cancellationToken) => Task.CompletedTask;
                }
                """,
            ],
            [new RabbitMqSubscriptionSourceGenerator()],
            additionalReferences: References,
            additionalMetadataReferences: [MetadataReference.CreateFromImage(contractsImage)]);

        result.GetErrors().Should().BeEmpty(Describe(result));
        var generated = result.GetSourceByFileName("RabbitMqSubscriptionRegistration.g.cs")!.SourceText;
        generated.Should().Contain("PayloadType = typeof(global::Cross.Contracts.CrossEvent)");
        generated.Should().Contain("Origin = \"CrossHandlers\"");
    }

    private static string Describe(SourceGeneratorResult result)
        => string.Join("\n", result.Diagnostics.Select(d => d.ToString()));
}
