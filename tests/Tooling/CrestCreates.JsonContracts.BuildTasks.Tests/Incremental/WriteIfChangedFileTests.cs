using CrestCreates.JsonContracts.BuildTasks.Generation;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Incremental;

/// <summary>Case IDs: B14, F12</summary>
public class WriteIfChangedFileTests : IDisposable
{
    private readonly string _tempDir;

    public WriteIfChangedFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WriteIfChanged_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void WriteIfChanged_NewFileWritesSuccessfully()
    {
        var outputPath = Path.Combine(_tempDir, "output.cs");
        var bytes = "Hello World"u8.ToArray();
        var tempDir = Path.Combine(_tempDir, "tmp");
        Directory.CreateDirectory(tempDir);

        var result = WriteIfChangedFile.WriteIfChanged(outputPath, bytes, tempDir);

        result.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllBytes(outputPath).Should().Equal(bytes);
    }

    [Fact]
    public void WriteIfChanged_UnchangedContentDoesNotRewrite()
    {
        var outputPath = Path.Combine(_tempDir, "output.cs");
        var bytes = "Hello World"u8.ToArray();
        var tempDir = Path.Combine(_tempDir, "tmp");
        Directory.CreateDirectory(tempDir);

        WriteIfChangedFile.WriteIfChanged(outputPath, bytes, tempDir);
        var originalWriteTime = File.GetLastWriteTimeUtc(outputPath);

        Thread.Sleep(10);
        var result = WriteIfChangedFile.WriteIfChanged(outputPath, bytes, tempDir);

        result.Should().BeFalse();
        File.GetLastWriteTimeUtc(outputPath).Should().Be(originalWriteTime);
    }

    [Fact]
    public void WriteIfChanged_ChangedContentRewrites()
    {
        var outputPath = Path.Combine(_tempDir, "output.cs");
        var bytes1 = "Hello World"u8.ToArray();
        var bytes2 = "Hello World 2"u8.ToArray();
        var tempDir = Path.Combine(_tempDir, "tmp");
        Directory.CreateDirectory(tempDir);

        WriteIfChangedFile.WriteIfChanged(outputPath, bytes1, tempDir);
        var result = WriteIfChangedFile.WriteIfChanged(outputPath, bytes2, tempDir);

        result.Should().BeTrue();
        File.ReadAllBytes(outputPath).Should().Equal(bytes2);
    }

    [Fact]
    public void WriteIfChanged_PreservesPreviousFileOnFailure()
    {
        var outputPath = Path.Combine(_tempDir, "output.cs");
        var originalBytes = "Original"u8.ToArray();
        var tempDir = Path.Combine(_tempDir, "tmp");
        Directory.CreateDirectory(tempDir);

        WriteIfChangedFile.WriteIfChanged(outputPath, originalBytes, tempDir);

        var invalidTempDir = "/nonexistent/path/that/should/fail";
        var act = () => WriteIfChangedFile.WriteIfChanged(outputPath, "New content"u8.ToArray(), invalidTempDir);

        act.Should().Throw<Exception>();
        File.ReadAllBytes(outputPath).Should().Equal(originalBytes);
    }

    [Fact]
    public void WriteIfChanged_RejectsEmptyOutputPath()
    {
        var act = () => WriteIfChangedFile.WriteIfChanged("", "content"u8.ToArray(), _tempDir);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteIfChanged_RejectsEmptyTemporaryDirectory()
    {
        var outputPath = Path.Combine(_tempDir, "output.cs");
        var act = () => WriteIfChangedFile.WriteIfChanged(outputPath, "content"u8.ToArray(), "");
        act.Should().Throw<ArgumentException>();
    }
}
