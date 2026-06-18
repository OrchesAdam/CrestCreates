using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Repositories;
using CrestCreates.FileManagement.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.FileManagement.Tests;

public class FileUrlServiceTests
{
    [Fact]
    public void GetPublicUrl_ReturnsCorrectUrl()
    {
        var options = new FileUrlOptions { BaseUrl = "/files" };
        var service = new FileUrlService(options);

        var entity = new FileEntity
        {
            Key = FileKey.Create(Guid.NewGuid(), ".txt"),
            TenantId = Guid.NewGuid(),
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 100,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Public
        };

        var url = service.GetPublicUrl(entity);
        Assert.StartsWith("/files/", url);
        Assert.Contains(".txt", url);
    }

    [Fact]
    public void GetPublicUrl_WithAbsoluteUrl_UsesAbsolutePrefix()
    {
        var options = new FileUrlOptions { BaseUrl = "/files", UseAbsoluteUrl = true, AbsoluteUrlPrefix = "https://cdn.example.com/files" };
        var service = new FileUrlService(options);

        var entity = new FileEntity
        {
            Key = FileKey.Create(Guid.NewGuid(), ".txt"),
            TenantId = Guid.NewGuid(),
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 100,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Public
        };

        var url = service.GetPublicUrl(entity);
        Assert.StartsWith("https://cdn.example.com/files/", url);
    }

    [Fact]
    public void GetStorageKeyFromUrl_ExtractsKey()
    {
        var options = new FileUrlOptions { BaseUrl = "/files" };
        var service = new FileUrlService(options);

        var entity = new FileEntity
        {
            Key = FileKey.Create(Guid.NewGuid(), ".txt"),
            TenantId = Guid.NewGuid(),
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 100,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Public
        };

        var url = service.GetPublicUrl(entity);
        var key = service.GetStorageKeyFromUrl(url);
        Assert.Equal(entity.Key.ToStorageKey(), key);
    }

    [Fact]
    public void GetStorageKeyFromUrl_ReturnsNullForInvalidUrl()
    {
        var options = new FileUrlOptions { BaseUrl = "/files" };
        var service = new FileUrlService(options);

        Assert.Null(service.GetStorageKeyFromUrl("https://other.com/files/test.txt"));
        Assert.Null(service.GetStorageKeyFromUrl(""));
        Assert.Null(service.GetStorageKeyFromUrl(null!));
    }
}
