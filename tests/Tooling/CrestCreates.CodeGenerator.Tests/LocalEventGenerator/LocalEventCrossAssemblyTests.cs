using System.Collections.Generic;
using System.Linq;
using CrestCreates.CodeGenerator.LocalEventGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.LocalEventGenerator;

/// <summary>
/// R1-LE-01：codec ownership 必须跟随「事件所属程序集」，而不是「引用该事件的 handler 程序集」。
/// 这些用例在真实两程序集编译下验证：仅引用他人 replayable 事件的 handler-only 程序集既不需要
/// JSON 工具链，也不得发出本项目的 codec contribution 调用。
/// </summary>
public class LocalEventCrossAssemblyTests
{
    private static readonly string[] EventingReferences = ["CrestCreates.EventBus.Abstractions", "System.Text.Json"];

    private const string CrossContractsSource = """
        using CrestCreates.EventBus.Abstractions;

        namespace Cross.Contracts;

        [LocalEvent(Replayable = true, Version = 2)]
        public sealed class CrossEvent : ILocalEvent
        {
            public string OrderId { get; set; } = string.Empty;
        }
        """;

    private const string CrossHandlersSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.EventBus.Abstractions;
        using Cross.Contracts;

        namespace Cross.Handlers;

        public sealed class CrossEventHandler : ILocalEventHandler<CrossEvent>
        {
            public Task HandleAsync(CrossEvent @event, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
        """;

    private static MetadataReference ContractsImage()
        => MetadataReference.CreateFromImage(
            SourceGeneratorTestHelper.EmitAssemblyImage("CrossContracts", [CrossContractsSource], EventingReferences));

    [Fact]
    public void HandlerOnlyAssembly_DoesNotRequireToolchain_NorEmitCodecContribution()
    {
        var result = SourceGeneratorTestHelper.RunGenerators(
            "CrossHandlers",
            [CrossHandlersSource],
            [new LocalEventSourceGenerator()],
            additionalReferences: EventingReferences,
            additionalMetadataReferences: [ContractsImage()]);

        // 修复前：把所有 replayable declaration 当成本项目拥有 → 无工具链时误报 CCCLE004 并编译失败。
        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CCCLE004");
        result.GetErrors().Should().BeEmpty(Describe(result));
        result.CompilationSuccess.Should().BeTrue(Describe(result));

        var generated = result.GetSourceByFileName("LocalEventRegistration.g.cs")!;
        generated.SourceText.Should().Contain("global::Cross.Contracts.CrossEvent");
        generated.SourceText.Should().Contain("typeof(global::Cross.Handlers.CrossEventHandler)");
        generated.SourceText.Should().NotContain("LocalEventJsonCodecContributions.Register");
    }

    [Fact]
    public void HandlerOnlyAssembly_WithToolchain_StillDoesNotEmitCodecContribution()
    {
        var result = SourceGeneratorTestHelper.RunGenerators(
            "CrossHandlers",
            [CrossHandlersSource],
            [new LocalEventSourceGenerator()],
            additionalReferences: EventingReferences,
            globalAnalyzerOptions: new Dictionary<string, string>
            {
                ["build_property.CrestCreatesLocalEventJsonContracts"] = "true",
            },
            additionalMetadataReferences: [ContractsImage()]);

        // 修复前：有工具链时无条件生成 Register() 调用，而 handler 程序集没有自己的 replay roots，
        // 解析到事件程序集的 internal 类 → CS0122，编译失败。
        result.GetErrors().Should().BeEmpty(Describe(result));
        result.CompilationSuccess.Should().BeTrue(Describe(result));

        var generated = result.GetSourceByFileName("LocalEventRegistration.g.cs")!;
        generated.SourceText.Should().Contain("global::Cross.Contracts.CrossEvent");
        generated.SourceText.Should().NotContain("LocalEventJsonCodecContributions.Register");
    }

    [Fact]
    public void OwningAssembly_WithToolchain_EmitsCodecContribution()
    {
        // 对照：事件所属程序集在有工具链时仍须发出 codec contribution（ownership 判定没有误伤 owner）。
        // 这里不断言 CompilationSuccess：codec contribution 目标类由 JsonContracts BuildTasks 生成，不在本单元 harness 内。
        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            CrossContractsSource,
            additionalReferences: EventingReferences,
            globalAnalyzerOptions: new Dictionary<string, string>
            {
                ["build_property.CrestCreatesLocalEventJsonContracts"] = "true",
            });

        result.GetSourceByFileName("LocalEventRegistration.g.cs")!
            .SourceText.Should().Contain("LocalEventJsonCodecContributions.Register();");
    }

    [Fact]
    public void OwningAssembly_WithoutToolchain_ReportsCCCLE004()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            CrossContractsSource,
            additionalReferences: EventingReferences);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCCLE004" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string Describe(SourceGeneratorResult result)
        => string.Join("\n", result.GetErrors().Select(error => error.ToString()));
}
