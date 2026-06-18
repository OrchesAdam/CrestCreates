using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class PhysicalFileProviderDirectoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PhysicalFileProvider _provider;

    public PhysicalFileProviderDirectoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vfs_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir1"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir2"));
        File.WriteAllText(Path.Combine(_tempDir, "root.txt"), "root");
        File.WriteAllText(Path.Combine(_tempDir, "subdir1", "file1.txt"), "file1");
        File.WriteAllText(Path.Combine(_tempDir, "subdir2", "file2.txt"), "file2");

        _provider = new PhysicalFileProvider("test", _tempDir);
    }

    [Fact]
    public async Task GetDirectoryAsync_ReturnsDirectory_WhenExists()
    {
        var path = VirtualPath.Create("test", "subdir1");
        var dir = await _provider.GetDirectoryAsync(path);

        Assert.NotNull(dir);
        Assert.Equal("subdir1", dir.Name);
        Assert.True(dir.Exists);
    }

    [Fact]
    public async Task GetDirectoryAsync_ReturnsNull_WhenNotExists()
    {
        var path = VirtualPath.Create("test", "nonexistent");
        var dir = await _provider.GetDirectoryAsync(path);

        Assert.Null(dir);
    }

    [Fact]
    public async Task DirectoryExistsAsync_ReturnsTrue_WhenExists()
    {
        var path = VirtualPath.Create("test", "subdir1");
        Assert.True(await _provider.DirectoryExistsAsync(path));
    }

    [Fact]
    public async Task DirectoryExistsAsync_ReturnsFalse_WhenNotExists()
    {
        var path = VirtualPath.Create("test", "nonexistent");
        Assert.False(await _provider.DirectoryExistsAsync(path));
    }

    [Fact]
    public async Task GetDirectoryAsync_GetFiles_ReturnsFilesInDirectory()
    {
        var path = VirtualPath.Create("test", "subdir1");
        var dir = await _provider.GetDirectoryAsync(path);

        Assert.NotNull(dir);
        var files = await dir.GetFilesAsync();
        Assert.Single(files);
        Assert.Equal("file1.txt", files.First().FileName);
    }

    [Fact]
    public async Task GetDirectoryAsync_GetDirectories_ReturnsSubDirectories()
    {
        var path = VirtualPath.Create("test", ".");
        var dir = await _provider.GetDirectoryAsync(path);

        Assert.NotNull(dir);
        var subdirs = await dir.GetDirectoriesAsync();
        Assert.Equal(2, subdirs.Count());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
