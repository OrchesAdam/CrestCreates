using System;
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class JobIdTests
{
    [Fact]
    public void New_Should_CreateJobId_WithNewUuid()
    {
        // Act
        var jobId = JobId.New();

        // Assert
        jobId.Uuid.Should().NotBe(Guid.Empty);
        jobId.Name.Should().BeNull();
        jobId.Group.Should().BeNull();
    }

    [Fact]
    public void Create_Should_CreateJobId_WithNameAndGroup()
    {
        // Act
        var jobId = JobId.Create("MyJob", "MyGroup");

        // Assert
        jobId.Name.Should().Be("MyJob");
        jobId.Group.Should().Be("MyGroup");
        jobId.Uuid.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Create_WithDefaultGroup_Should_UseDefaultGroup()
    {
        // Act
        var jobId = JobId.Create("MyJob");

        // Assert
        jobId.Name.Should().Be("MyJob");
        jobId.Group.Should().Be("Default");
    }

    [Fact]
    public void Constructor_WithUuid_Should_SetUuid()
    {
        // Arrange
        var uuid = Guid.NewGuid();

        // Act
        var jobId = new JobId(uuid);

        // Assert
        jobId.Uuid.Should().Be(uuid);
        jobId.Name.Should().BeNull();
        jobId.Group.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAllParams_Should_SetAllProperties()
    {
        // Arrange
        var uuid = Guid.NewGuid();

        // Act
        var jobId = new JobId("TestJob", "TestGroup", uuid);

        // Assert
        jobId.Name.Should().Be("TestJob");
        jobId.Group.Should().Be("TestGroup");
        jobId.Uuid.Should().Be(uuid);
    }

    [Fact]
    public void ToString_WithUuid_Should_ReturnUuidString()
    {
        // Arrange
        var uuid = Guid.NewGuid();
        var jobId = new JobId(uuid);

        // Act
        var result = jobId.ToString();

        // Assert
        result.Should().Be(uuid.ToString());
    }

    [Fact]
    public void ToString_WithoutUuid_Should_ReturnGroupNameString()
    {
        // Arrange
        var jobId = JobId.Create("MyJob", "MyGroup");

        // Act
        var result = jobId.ToString();

        // Assert
        result.Should().Be("MyGroup/MyJob");
    }

    [Fact]
    public void JobIds_WithSameValues_Should_BeEqual()
    {
        // Arrange
        var uuid = Guid.NewGuid();
        var jobId1 = new JobId("TestJob", "TestGroup", uuid);
        var jobId2 = new JobId("TestJob", "TestGroup", uuid);

        // Assert
        jobId1.Should().Be(jobId2);
    }

    [Fact]
    public void JobIds_WithDifferentUuids_Should_NotBeEqual()
    {
        // Arrange
        var jobId1 = new JobId("TestJob", "TestGroup", Guid.NewGuid());
        var jobId2 = new JobId("TestJob", "TestGroup", Guid.NewGuid());

        // Assert
        jobId1.Should().NotBe(jobId2);
    }
}
