using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Message;
using CrestCreates.Runtime.Delivery.Options;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Delivery.Tests;

public sealed class OutboxMessageFactoryTests
{
    [Fact]
    public void Factory_detaches_payload_and_sorts_required_consumers()
    {
        var factory = new DefaultOutboxMessageFactory();
        var payload = new byte[] { 1, 2, 3 };
        var message = factory.Create("m-1", "tenant-a", "contract/v1", "payload/v1", payload, ["z", "a", "a"]);
        payload[0] = 99;

        message.Payload.Should().Equal(1, 2, 3);
        message.Metadata.RequiredConsumerIds.Should().Equal("a", "z");
        OutboxMessageIntegrity.Matches(message).Should().BeTrue();
    }

    [Fact]
    public void Options_reject_timeout_that_can_outlive_lease()
    {
        var options = new OutboxDeliveryOptions { LeaseDuration = TimeSpan.FromSeconds(1), HandlerTimeout = TimeSpan.FromSeconds(1) };
        options.Invoking(value => value.Validate()).Should().Throw<ArgumentOutOfRangeException>();
    }
}
