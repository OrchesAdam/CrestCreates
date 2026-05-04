using System;
using CrestCreates.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Domain.Tests;

public class ConcurrencyExceptionTests
{
    [Fact]
    public void CrestConcurrencyException_HasPlatformErrorInfo()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ex = new CrestConcurrencyException("Book", id);

        ex.ErrorCode.Should().Be("Crest.Concurrency.Conflict");
        ex.HttpStatusCode.Should().Be(409);
        ex.EntityType.Should().Be("Book");
        ex.EntityId.Should().Be(id);
        ex.Details.Should().Contain("Book");
        ex.Message.Should().Be("Concurrency conflict.");
    }

    [Fact]
    public void CrestPreconditionRequiredException_HasPlatformErrorInfo()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var ex = new CrestPreconditionRequiredException("Book", id);

        ex.ErrorCode.Should().Be("Crest.Concurrency.PreconditionRequired");
        ex.HttpStatusCode.Should().Be(428);
        ex.EntityType.Should().Be("Book");
        ex.EntityId.Should().Be(id);
        ex.Details.Should().Contain("If-Match");
        ex.Message.Should().Be("Precondition required.");
    }
}
