using System;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Domain.Shared.Exceptions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Domain.Tests.Exceptions;

public class CrestBusinessExceptionTests
{
    [Fact]
    public void String_Constructor_Preserves_ErrorCode()
    {
        var exception = new CrestBusinessException("Test.Code", "Test message");

        exception.ErrorCode.Should().Be("Test.Code");
        exception.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public void Typed_Constructor_Accepts_ErrorCode_And_Preserves_Wire_Code()
    {
        var exception = new CrestBusinessException(
            new ErrorCode("Crest.FeatureManagement.InvalidValue"),
            "Invalid feature value.");

        exception.ErrorCode.Should().Be("Crest.FeatureManagement.InvalidValue");
        exception.ErrorCodeValue.Should().Be(new ErrorCode("Crest.FeatureManagement.InvalidValue"));
        exception.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public void Typed_Constructor_Rejects_Default_ErrorCode()
    {
        var act = () => new CrestBusinessException(default(ErrorCode), "Invalid feature value.");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Error code is empty.");
    }

    [Fact]
    public void CrestErrorCodes_Match_Wire_Values()
    {
        CrestErrorCodes.InternalError.Value.Should().Be("Crest.InternalError");
        CrestErrorCodes.AuthUnauthorized.Value.Should().Be("Crest.Auth.Unauthorized");
        CrestErrorCodes.AuthForbidden.Value.Should().Be("Crest.Auth.Forbidden");
        CrestErrorCodes.ValidationFailed.Value.Should().Be("Crest.Validation.Failed");
        CrestErrorCodes.ConcurrencyConflict.Value.Should().Be("Crest.Concurrency.Conflict");
        CrestErrorCodes.ConcurrencyPreconditionRequired.Value.Should().Be("Crest.Concurrency.PreconditionRequired");
    }
}
