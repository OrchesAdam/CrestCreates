using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.AuditLog;

public class AuditLogAppServiceTests
{
    [Fact]
    public async Task GetListAsync_ShouldReturnPagedResults_WithAllFields()
    {
        // Given
        var auditLogs = new List<CrestCreates.Domain.AuditLog.AuditLog>
        {
            new CrestCreates.Domain.AuditLog.AuditLog(Guid.NewGuid())
            {
                ExecutionTime = DateTime.UtcNow.AddMinutes(-5),
                Duration = 150,
                UserId = "user-1",
                UserName = "alice",
                TenantId = "tenant-1",
                HttpMethod = "POST",
                Url = "http://localhost/api/login",
                ServiceName = "AuthService",
                MethodName = "LoginAsync",
                Status = 0,
                ClientIpAddress = "127.0.0.1"
            },
            new CrestCreates.Domain.AuditLog.AuditLog(Guid.NewGuid())
            {
                ExecutionTime = DateTime.UtcNow,
                Duration = 80,
                UserId = "user-2",
                UserName = "bob",
                TenantId = "tenant-1",
                HttpMethod = "GET",
                Url = "http://localhost/api/books",
                ServiceName = "BookAppService",
                MethodName = "GetListAsync",
                Status = 0,
                ClientIpAddress = "127.0.0.2"
            }
        };

        var mockRepository = new Mock<IAuditLogRepository>();
        mockRepository
            .Setup(r => r.GetPagedListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CrestCreates.Domain.AuditLog.AuditLog>(
                auditLogs, 2, 0, 10));

        var appService = new AuditLogAppService(mockRepository.Object);

        var request = new AuditLogPagedRequestDto
        {
            PageIndex = 0,
            PageSize = 10
        };

        // When
        var result = await appService.GetListAsync(request);

        // Then
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.PageIndex.Should().Be(0);
        result.PageSize.Should().Be(10);

        var first = result.Items.First();
        first.UserId.Should().Be("user-1");
        first.UserName.Should().Be("alice");
        first.HttpMethod.Should().Be("POST");
        first.Url.Should().Be("http://localhost/api/login");
        first.ServiceName.Should().Be("AuthService");
        first.MethodName.Should().Be("LoginAsync");
        first.Status.Should().Be(0);
    }

    [Fact]
    public async Task GetListAsync_ShouldPassFiltersToRepository()
    {
        // Given
        var mockRepository = new Mock<IAuditLogRepository>();
        PagedResult<CrestCreates.Domain.AuditLog.AuditLog>? capturedResult = null;
        mockRepository
            .Setup(r => r.GetPagedListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime?, DateTime?, string?, string?, string?, int?, string?, string?, int, int, CancellationToken>(
                (s, e, uid, un, tid, st, hm, kw, pi, ps, ct) =>
                {
                    capturedResult = new PagedResult<CrestCreates.Domain.AuditLog.AuditLog>(
                        new List<CrestCreates.Domain.AuditLog.AuditLog>(), 0, pi, ps);
                })
            .ReturnsAsync(() => capturedResult!);

        var appService = new AuditLogAppService(mockRepository.Object);

        var request = new AuditLogPagedRequestDto
        {
            PageIndex = 2,
            PageSize = 20,
            StartTime = new DateTime(2024, 1, 1),
            EndTime = new DateTime(2024, 12, 31),
            UserId = "user-1",
            UserName = "alice",
            TenantId = "tenant-1",
            Status = 0,
            HttpMethod = "POST",
            Keyword = "login"
        };

        // When
        await appService.GetListAsync(request);

        // Then
        mockRepository.Verify(r => r.GetPagedListAsync(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            "user-1",
            "alice",
            "tenant-1",
            0,
            "POST",
            "login",
            2,
            20,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetListAsync_ShouldReturnEmpty_WhenNoResults()
    {
        // Given
        var mockRepository = new Mock<IAuditLogRepository>();
        mockRepository
            .Setup(r => r.GetPagedListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CrestCreates.Domain.AuditLog.AuditLog>(
                new List<CrestCreates.Domain.AuditLog.AuditLog>(), 0, 0, 10));

        var appService = new AuditLogAppService(mockRepository.Object);

        var request = new AuditLogPagedRequestDto { PageIndex = 0, PageSize = 10 };

        // When
        var result = await appService.GetListAsync(request);

        // Then
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }
}
