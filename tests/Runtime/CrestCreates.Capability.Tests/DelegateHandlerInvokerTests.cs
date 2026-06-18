using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class DelegateHandlerInvokerTests
{
    [Fact]
    public async Task InvokeAsync_PassesInputAndReturnsOutput()
    {
        var invoker = new DelegateHandlerInvoker((input, ct) =>
            Task.FromResult<object?>($"ECHO: {input}"));

        var result = await invoker.InvokeAsync("hello", CancellationToken.None);

        result.Should().Be("ECHO: hello");
    }

    [Fact]
    public async Task InvokeAsync_NullInput_PassesThrough()
    {
        var invoker = new DelegateHandlerInvoker((input, ct) =>
            Task.FromResult<object?>(input));

        var result = await invoker.InvokeAsync(null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_PropagatesCancellation()
    {
        var invoker = new DelegateHandlerInvoker(async (input, ct) =>
        {
            await Task.Delay(500, ct);
            return "done";
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await invoker.Invoking(i => i.InvokeAsync("test", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
