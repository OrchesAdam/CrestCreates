using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRegistryTests
{
    private static HumanTaskDescriptor CreateTask(string id, string name, int version)
    {
        return new HumanTaskDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1)
        };
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = new HumanTaskRegistry();
        var task = CreateTask("ht_01", "manager.approval", 1);
        registry.Register(task);

        var result = registry.GetById("ht_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("manager.approval");
    }

    [Fact]
    public void GetAll_Returns_All_Tasks()
    {
        var registry = new HumanTaskRegistry();
        registry.Register(CreateTask("ht_01", "task.a", 1));
        registry.Register(CreateTask("ht_02", "task.b", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }
}
