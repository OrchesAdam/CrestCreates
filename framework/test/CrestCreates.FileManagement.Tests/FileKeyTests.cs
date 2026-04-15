using CrestCreates.FileManagement.Models;
using FluentAssertions;
using Xunit;

namespace CrestCreates.FileManagement.Tests;

public class FileKeyTests
{
    [Fact]
    public void Create_GeneratesValidKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var extension = ".pdf";

        // Act
        var key = FileKey.Create(tenantId, extension);

        // Assert
        key.TenantId.Should().Be(tenantId);
        key.Year.Should().Be(DateTimeOffset.UtcNow.Year);
        key.Extension.Should().Be(".pdf");
        key.FileGuid.Should().NotBeEmpty();
    }

    [Fact]
    public void ToStorageKey_ReturnsCorrectFormat()
    {
        // Arrange
        var tenantId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var key = new FileKey
        {
            TenantId = tenantId,
            Year = 2026,
            FileGuid = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            Extension = ".pdf"
        };

        // Act
        var storageKey = key.ToStorageKey();

        // Assert
        storageKey.Should().Be("550e8400-e29b-41d4-a716-446655440000/2026/a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf");
    }

    [Fact]
    public void Parse_ValidStorageKey_ReturnsFileKey()
    {
        // Arrange
        var storageKey = "550e8400-e29b-41d4-a716-446655440000/2026/a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf";

        // Act
        var key = FileKey.Parse(storageKey);

        // Assert
        key.Should().NotBeNull();
        key!.TenantId.Should().Be(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        key.Year.Should().Be(2026);
        key.Extension.Should().Be(".pdf");
    }

    [Fact]
    public void Parse_InvalidStorageKey_ReturnsNull()
    {
        // Arrange
        var invalidKey = "invalid/key/format";

        // Act
        var result = FileKey.Parse(invalidKey);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000/2026/a1b2c3d4.pdf")]
    [InlineData("../dangerous/path")]
    [InlineData("")]
    public void Parse_VariousInvalidKeys_ReturnsNull(string invalidKey)
    {
        // Act
        var result = FileKey.Parse(invalidKey);

        // Assert
        result.Should().BeNull();
    }
}
