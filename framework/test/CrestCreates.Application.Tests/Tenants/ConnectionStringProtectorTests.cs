using System;
using System.Collections.Generic;
using CrestCreates.MultiTenancy;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class ConnectionStringProtectorTests
{
    private readonly ConnectionStringProtector _protector;

    public ConnectionStringProtectorTests()
    {
        _protector = new ConnectionStringProtector();
    }

    [Fact]
    public void Protect_WithValidConnectionString_ReturnsBase64Encoded()
    {
        var connectionString = "Server=localhost;Database=TestDb;";

        var protected_ = _protector.Protect(connectionString);

        protected_.Should().NotBeNullOrEmpty();
        protected_.Should().NotBe(connectionString);
    }

    [Fact]
    public void Unprotect_WithProtectedConnectionString_ReturnsOriginal()
    {
        var original = "Server=localhost;Database=TestDb;";
        var protected_ = _protector.Protect(original);

        var result = _protector.Unprotect(protected_);

        result.Should().Be(original);
    }

    [Fact]
    public void Unprotect_WithNull_ReturnsNull()
    {
        var result = _protector.Unprotect(null);

        result.Should().BeNull();
    }

    [Fact]
    public void Mask_WithShortConnectionString_ReturnsMaskedValue()
    {
        var connectionString = "short";

        var masked = _protector.Mask(connectionString);

        masked.Should().Be("***");
    }

    [Fact]
    public void Mask_WithLongConnectionString_MasksMiddle()
    {
        var connectionString = "Server=localhost;Database=VeryLongTestDatabaseName;";

        var masked = _protector.Mask(connectionString);

        masked.Should().StartWith("Serv");
        masked.Should().EndWith("ame;");
        masked.Should().Contain("****");
    }

    [Fact]
    public void Mask_WithEmptyConnectionString_ReturnsEmpty()
    {
        var masked = _protector.Mask(string.Empty);

        masked.Should().BeEmpty();
    }
}
