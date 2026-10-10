using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 精确 guard：工作单元装配主链（Data.Abstractions）不得重新引入运行时类型解析/程序集扫描。
/// 仅扫描该项目的生产源码，允许注释中提及历史模式；不误伤其它项目的合法 SDK 隔离代码。
/// </summary>
public class UnitOfWorkReflectionGuardTests
{
    private static readonly string[] ForbiddenPatterns =
    {
        "Type.GetType(",
        "AppDomain.CurrentDomain.GetAssemblies",
        "Assembly.GetType(",
        "Activator.CreateInstance(",
        "GetConstructors("
    };

    [Fact]
    public void UnitOfWork_assembly_mainline_does_not_use_runtime_type_resolution()
    {
        var projectDirectory = Path.Combine(
            FindRepoRoot(), "src", "Persistence", "CrestCreates.Data.Abstractions");

        var violations = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (File: file, Line: index + 1, Text: line)))
            .Where(entry => ForbiddenPatterns.Any(pattern =>
                entry.Text.Contains(pattern, StringComparison.Ordinal)))
            .Select(entry =>
                $"{Path.GetRelativePath(projectDirectory, entry.File)}:{entry.Line}: {entry.Text.Trim()}")
            .ToArray();

        violations.Should().BeEmpty(
            "the unified unit-of-work assembly must stay free of runtime type resolution and assembly scanning; " +
            "provider construction belongs to provider packages via typed bindings");
    }

    private static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
