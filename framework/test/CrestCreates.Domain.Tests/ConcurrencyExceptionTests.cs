using System;
using CrestCreates.Domain.Exceptions;
using Xunit;

namespace CrestCreates.Domain.Tests;

public class ConcurrencyExceptionTests
{
    [Fact]
    public void CrestConcurrencyException_HasEntityTypeAndId()
    {
        var ex = new CrestConcurrencyException("Book", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Equal("Book", ex.EntityType);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), ex.EntityId);
        Assert.Contains("Book", ex.Message);
        Assert.Contains("concurrency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrestPreconditionRequiredException_HasEntityTypeAndId()
    {
        var ex = new CrestPreconditionRequiredException("Book", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        Assert.Equal("Book", ex.EntityType);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), ex.EntityId);
        Assert.Contains("If-Match", ex.Message);
    }
}
