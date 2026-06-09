using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class IdempotencyMiddlewareTests
{
    private static CapabilityExecutionContext CreateContext(string key = "idem_001")
    {
        return new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc",
            IdempotencyKey = key
        };
    }

    [Fact]
    public async Task Passthrough_WhenNoStore()
    {
        var middleware = new IdempotencyMiddleware(null);
        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("ok");
    }

    [Fact]
    public async Task Returns_Cached_OnDuplicate()
    {
        var store = new InMemoryIdempotenceStore();
        var cached = CapabilityExecutionResult.Success("cached", TimeSpan.FromMilliseconds(10));
        await store.StoreResultAsync("idem_001", cached);

        var middleware = new IdempotencyMiddleware(store);
        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("fresh", TimeSpan.Zero)));

        result.Output.Should().Be("cached");
    }

    [Fact]
    public async Task Stores_Result_AfterSuccess()
    {
        var store = new InMemoryIdempotenceStore();
        var middleware = new IdempotencyMiddleware(store);

        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("first", TimeSpan.Zero)));

        result.Output.Should().Be("first");

        var cached = await store.GetResultAsync("idem_001");
        cached.Should().NotBeNull();
        cached!.Output.Should().Be("first");
    }

    [Fact]
    public async Task Does_Not_Store_OnFailure()
    {
        var store = new InMemoryIdempotenceStore();
        var middleware = new IdempotencyMiddleware(store);

        await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));

        var cached = await store.GetResultAsync("idem_001");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task DifferentKeys_ProduceDifferentResults()
    {
        var store = new InMemoryIdempotenceStore();
        var middleware = new IdempotencyMiddleware(store);

        await middleware.InvokeAsync(CreateContext("key_A"), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("A", TimeSpan.Zero)));
        var r2 = await middleware.InvokeAsync(CreateContext("key_B"), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("B", TimeSpan.Zero)));

        r2.Output.Should().Be("B");
    }
}