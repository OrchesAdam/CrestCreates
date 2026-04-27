using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class PhysicalFileProviderSecurityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PhysicalFileProvider _provider;

    public PhysicalFileProviderSecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vfs_security_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "safe"));
        File.WriteAllText(Path.Combine(_tempDir, "safe", "allowed.txt"), "allowed");

        var outsideDir = Path.Combine(_tempDir, "..", "outside_vfs");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(outsideDir, "secret.txt"), "secret");

        _provider = new PhysicalFileProvider("test", _tempDir);
    }

    [Fact]
    public async Task GetFileAsync_RejectsPathTraversal()
    {
        var path = VirtualPath.Create("test", "../../outside_vfs/secret.txt");
        var file = await _provider.GetFileAsync(path);
        Assert.Null(file);
    }

    [Fact]
    public async Task ExistsAsync_RejectsPathTraversal()
    {
        var path = VirtualPath.Create("test", "../../outside_vfs/secret.txt");
        Assert.False(await _provider.ExistsAsync(path));
    }

    [Fact]
    public async Task GetFileAsync_AllowsValidPath()
    {
        var path = VirtualPath.Create("test", "safe/allowed.txt");
        var file = await _provider.GetFileAsync(path);
        Assert.NotNull(file);
    }

    [Fact]
    public async Task GetDirectoryAsync_RejectsPathTraversal()
    {
        var path = VirtualPath.Create("test", "../../outside_vfs");
        var dir = await _provider.GetDirectoryAsync(path);
        Assert.Null(dir);
    }

    [Fact]
    public async Task DirectoryExistsAsync_RejectsPathTraversal()
    {
        var path = VirtualPath.Create("test", "../../outside_vfs");
        Assert.False(await _provider.DirectoryExistsAsync(path));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
        try { Directory.Delete(Path.Combine(_tempDir, "..", "outside_vfs"), true); } catch { }
    }
}
