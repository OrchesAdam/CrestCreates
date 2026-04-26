// framework/test/CrestCreates.CodeGenerator.Tests/CompensationExecutorGenerator/CompensationExecutorSourceGeneratorTests.cs
using Xunit;
using CrestCreates.CodeGenerator.CompensationExecutorGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.CompensationExecutorGenerator
{
    public class CompensationExecutorSourceGeneratorTests
    {
        [Fact]
        public void Should_Generate_Registry_For_Single_Executor()
        {
            var source = @"
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Attributes;

namespace TestNamespace
{
    [CompensationExecutor]
    public class OrderCompensationExecutor : ICompensationExecutor
    {
        public string Name => ""Order"";
        public Task ExecuteAsync(string? data) => Task.CompletedTask;
    }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CompensationExecutorSourceGenerator>(source);

            Assert.True(result.ContainsFile("CompensationExecutorRegistry.g.cs"));
            var generated = result.GetSourceByFileName("CompensationExecutorRegistry.g.cs");
            Assert.NotNull(generated);
            Assert.Contains("CompensationExecutorRegistry", generated.SourceText);
            Assert.Contains("OrderCompensationExecutor", generated.SourceText);
            Assert.Contains("AddCompensationExecutors", generated.SourceText);
        }

        [Fact]
        public void Should_Generate_Registry_For_Multiple_Executors()
        {
            var source = @"
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Attributes;

namespace TestNamespace
{
    [CompensationExecutor]
    public class OrderCompensationExecutor : ICompensationExecutor
    {
        public string Name => ""Order"";
        public Task ExecuteAsync(string? data) => Task.CompletedTask;
    }

    [CompensationExecutor]
    public class InventoryCompensationExecutor : ICompensationExecutor
    {
        public string Name => ""Inventory"";
        public Task ExecuteAsync(string? data) => Task.CompletedTask;
    }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CompensationExecutorSourceGenerator>(source);

            var generated = result.GetSourceByFileName("CompensationExecutorRegistry.g.cs");
            Assert.NotNull(generated);
            Assert.Contains("OrderCompensationExecutor", generated.SourceText);
            Assert.Contains("InventoryCompensationExecutor", generated.SourceText);
            Assert.Contains("GetExecutor", generated.SourceText);
            Assert.Contains("GetAll", generated.SourceText);
        }

        [Fact]
        public void Should_Not_Generate_When_No_Executors()
        {
            var source = @"
namespace TestNamespace
{
    public class SomeClass { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CompensationExecutorSourceGenerator>(source);

            Assert.False(result.ContainsFile("CompensationExecutorRegistry.g.cs"));
        }
    }
}
