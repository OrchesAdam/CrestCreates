using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class BootstrapCoordinatorTests
{
    [Fact]
    public async Task Executes_tasks_in_dependency_order()
    {
        var order = new List<string>();
        var tasks = new List<IBootstrapTask>
        {
            new TestTask("A", [], () => order.Add("A")),
            new TestTask("B", ["A"], () => order.Add("B")),
            new TestTask("C", ["A", "B"], () => order.Add("C"))
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());
        await coordinator.StartAsync(CancellationToken.None);

        order.Should().ContainInOrder("A", "B", "C");
    }

    [Fact]
    public async Task Detects_circular_dependency()
    {
        var tasks = new List<IBootstrapTask>
        {
            new TestTask("A", ["B"], () => { }),
            new TestTask("B", ["A"], () => { })
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());

        var act = () => coordinator.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BootstrapDependencyException>();
        ex.Which.Cycle.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Continues_on_non_required_task_failure()
    {
        var order = new List<string>();
        var tasks = new List<IBootstrapTask>
        {
            new TestTask("A", [], () => order.Add("A")),
            new FailingTask("B", ["A"], isRequired: false),
            new TestTask("C", ["A"], () => order.Add("C"))
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());
        await coordinator.StartAsync(CancellationToken.None);

        order.Should().Contain("A");
        order.Should().Contain("C");
    }

    [Fact]
    public async Task Throws_on_required_task_failure()
    {
        var tasks = new List<IBootstrapTask>
        {
            new FailingTask("A", [], isRequired: true)
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());

        var act = () => coordinator.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private class TestTask : IBootstrapTask
    {
        private readonly Action _action;
        public TestTask(string taskId, string[] deps, Action action)
        {
            TaskId = taskId;
            Dependencies = deps;
            _action = action;
        }
        public string TaskId { get; }
        public Type ServiceType => typeof(TestTask);
        public IReadOnlyList<string> Dependencies { get; }
        public bool IsRequired => true;
        public Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
        {
            _action();
            return Task.CompletedTask;
        }
    }

    private class FailingTask : IBootstrapTask
    {
        public FailingTask(string taskId, string[] deps, bool isRequired)
        {
            TaskId = taskId;
            Dependencies = deps;
            IsRequired = isRequired;
        }
        public string TaskId { get; }
        public Type ServiceType => typeof(FailingTask);
        public IReadOnlyList<string> Dependencies { get; }
        public bool IsRequired { get; }
        public Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
            => throw new InvalidOperationException("Bootstrap failed");
    }
}
