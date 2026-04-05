using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Permissions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

public class PermissionSyncServiceTests : IDisposable
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<PermissionSyncService>> _loggerMock;
    private readonly string _testManifestPath;

    public PermissionSyncServiceTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<PermissionSyncService>>();
        _testManifestPath = Path.Combine(Path.GetTempPath(), $"test_manifest_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_testManifestPath))
        {
            File.Delete(_testManifestPath);
        }
    }

    private PermissionSyncService CreateService(PermissionSyncOptions? options = null)
    {
        options ??= new PermissionSyncOptions
        {
            EnableSync = true,
            ManifestPath = _testManifestPath,
            SyncToDatabase = true,
            SyncToAuthorizationCenter = false
        };

        _serviceProviderMock
            .Setup(x => x.GetService(typeof(IPermissionRepository)))
            .Returns(_permissionRepositoryMock.Object);

        return new PermissionSyncService(
            Options.Create(options),
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _httpClientFactoryMock.Object
        );
    }

    private void CreateTestManifest(List<EntityPermissionInfo>? permissions = null)
    {
        var manifest = new EntityPermissionsManifest
        {
            Version = "1.0",
            GeneratedAt = DateTime.UtcNow,
            Permissions = permissions ?? new List<EntityPermissionInfo>
            {
                new()
                {
                    EntityName = "Book",
                    ClassName = "BookPermissions",
                    Namespace = "LibraryManagement.Domain.Permissions",
                    Permissions = new List<string> { "Book.Create", "Book.Update", "Book.Delete" }
                },
                new()
                {
                    EntityName = "Member",
                    ClassName = "MemberPermissions",
                    Namespace = "LibraryManagement.Domain.Permissions",
                    Permissions = new List<string> { "Member.Create", "Member.Update" }
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        File.WriteAllText(_testManifestPath, json);
    }

    [Fact]
    public async Task SyncToDatabaseAsync_WhenSyncDisabled_ShouldNotSync()
    {
        var options = new PermissionSyncOptions
        {
            EnableSync = false,
            ManifestPath = _testManifestPath
        };

        var service = CreateService(options);

        await service.SyncToDatabaseAsync();

        _permissionRepositoryMock.Verify(
            x => x.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncToDatabaseAsync_WhenManifestNotFound_ShouldLogWarning()
    {
        var service = CreateService();

        await service.SyncToDatabaseAsync();

        _permissionRepositoryMock.Verify(
            x => x.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncToDatabaseAsync_WhenPermissionNotExists_ShouldInsertNewPermission()
    {
        CreateTestManifest();
        _permissionRepositoryMock
            .Setup(x => x.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        var service = CreateService();

        await service.SyncToDatabaseAsync();

        _permissionRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task SyncToDatabaseAsync_WhenPermissionExists_ShouldNotInsert()
    {
        CreateTestManifest();
        var existingPermission = new Permission
        {
            Name = "Book.Create",
            DisplayName = "Book.Create",
            IsEnabled = true
        };
        typeof(Permission).GetProperty("Id")?.SetValue(existingPermission, Guid.NewGuid());

        _permissionRepositoryMock
            .Setup(x => x.FindByNameAsync("Book.Create", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPermission);
        _permissionRepositoryMock
            .Setup(x => x.FindByNameAsync(It.Is<string>(s => s != "Book.Create"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        var service = CreateService();

        await service.SyncToDatabaseAsync();

        _permissionRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public async Task SyncToDatabaseAsync_WhenRepositoryNotAvailable_ShouldLogError()
    {
        CreateTestManifest();
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(IPermissionRepository)))
            .Returns((object?)null);

        var options = new PermissionSyncOptions
        {
            EnableSync = true,
            ManifestPath = _testManifestPath,
            SyncToDatabase = true
        };

        var service = new PermissionSyncService(
            Options.Create(options),
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _httpClientFactoryMock.Object
        );

        await service.SyncToDatabaseAsync();

        _permissionRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncToAuthorizationCenterAsync_WhenUrlNotConfigured_ShouldNotSync()
    {
        var options = new PermissionSyncOptions
        {
            EnableSync = true,
            ManifestPath = _testManifestPath,
            AuthorizationCenterUrl = null,
            SyncToAuthorizationCenter = true
        };

        var service = CreateService(options);

        await service.SyncToAuthorizationCenterAsync();

        _httpClientFactoryMock.Verify(
            x => x.CreateClient(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncToAuthorizationCenterAsync_WhenConfigured_ShouldSendHttpRequest()
    {
        CreateTestManifest();

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(httpMessageHandlerMock.Object);

        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new PermissionSyncOptions
        {
            EnableSync = true,
            ManifestPath = _testManifestPath,
            AuthorizationCenterUrl = "https://auth-center.example.com",
            AuthorizationCenterApiKey = "test-api-key",
            SyncToAuthorizationCenter = true
        };

        var service = CreateService(options);

        await service.SyncToAuthorizationCenterAsync();

        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("api/permissions/sync")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllAsync_ShouldCallBothSyncMethods()
    {
        CreateTestManifest();
        _permissionRepositoryMock
            .Setup(x => x.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new PermissionSyncOptions
        {
            EnableSync = true,
            ManifestPath = _testManifestPath,
            AuthorizationCenterUrl = "https://auth-center.example.com",
            SyncToDatabase = true,
            SyncToAuthorizationCenter = true
        };

        var service = CreateService(options);

        await service.SyncAllAsync();

        _permissionRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());

        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SyncToDatabaseAsync_WithInvalidManifest_ShouldHandleError()
    {
        File.WriteAllText(_testManifestPath, "invalid json content");

        var service = CreateService();

        var act = async () => await service.SyncToDatabaseAsync();

        await act.Should().NotThrowAsync();
    }
}
