using CrestCreates.AuditLogging.Services;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.AuditLogging.Tests.Services;

public class AuditLogServiceTests
{
    [Fact]
    public async Task GetListAsync_ShouldUseRepositoryFindAndApplyPaging()
    {
        var repository = new Mock<IRepository<AuditLog, Guid>>();
        repository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditLog, bool>>>(), default))
            .ReturnsAsync(new List<AuditLog>
            {
                new(Guid.NewGuid()) { UserId = "u1", CreationTime = DateTime.UtcNow.AddMinutes(-1) },
                new(Guid.NewGuid()) { UserId = "u1", CreationTime = DateTime.UtcNow }
            });
        var service = new AuditLogService(repository.Object);

        var result = await service.GetListAsync(userId: "u1", skip: 0, take: 1);

        result.Should().HaveCount(1);
        repository.Verify(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditLog, bool>>>(), default), Times.Once);
        repository.Verify(x => x.GetAllAsync(default), Times.Never);
    }

    [Fact]
    public async Task GetCountAsync_ShouldCountFilteredResult()
    {
        var repository = new Mock<IRepository<AuditLog, Guid>>();
        repository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditLog, bool>>>(), default))
            .ReturnsAsync(new List<AuditLog> { new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()) });
        var service = new AuditLogService(repository.Object);

        var count = await service.GetCountAsync(userId: "u1");

        count.Should().Be(3);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteEachMatchedLog()
    {
        var oldLogs = new List<AuditLog> { new(Guid.NewGuid()), new(Guid.NewGuid()) };
        var repository = new Mock<IRepository<AuditLog, Guid>>();
        repository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditLog, bool>>>(), default))
            .ReturnsAsync(oldLogs);
        var service = new AuditLogService(repository.Object);

        await service.DeleteAsync(DateTime.UtcNow);

        repository.Verify(x => x.DeleteAsync(It.IsAny<AuditLog>(), default), Times.Exactly(2));
    }
}
