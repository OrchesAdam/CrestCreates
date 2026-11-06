using System.Linq;
using CrestCreates.CodeGenerator.EntityGenerator;
using CrestCreates.CodeGenerator.RepositoryGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.RepositoryGenerator;

/// <summary>
/// #130 S1 compile-time entry baselines (G01-G04). The red cases expect the frozen diagnostics:
/// today both generators silently accept non-EF/unknown providers (the entity generator even emits
/// direct SDK CRUD). The EF positive control proves the harness reaches the same generation path
/// without an unrelated compile failure.
/// </summary>
public sealed class Issue130ProviderDiagnosticsBaselineTests
{
    private const string EntityBaseStub = """
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
""";

    private static string EntityDeclaration(string provider)
        => $$"""
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [Entity(OrmProvider = "{{provider}}", GeneratePermissions = false)]
    public class Product : Entity<long>
    {
        public string Name { get; set; } = string.Empty;
    }
}
""";

    [Fact]
    public void G03_EntitySourceGenerator_EfCore_positive_control_generates_without_provider_diagnostics()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EntitySourceGenerator>(
            EntityDeclaration("EfCore"),
            new[] { EntityBaseStub });

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == RepositoryGeneratorDiagnosticCodes.NonEfProviderNotSupported);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == RepositoryGeneratorDiagnosticCodes.UnknownProvider);
        Assert.True(result.ContainsFile("EfCoreProductRepository.g.cs"), DescribeFiles(result));
    }

    [Fact]
    public void G01_EntitySourceGenerator_FreeSql_repository_request_reports_non_ef_provider_error()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EntitySourceGenerator>(
            EntityDeclaration("FreeSql"),
            new[] { EntityBaseStub });

        var diagnostic = result.Diagnostics.FirstOrDefault(
            d => d.Id == RepositoryGeneratorDiagnosticCodes.NonEfProviderNotSupported);
        Assert.True(diagnostic != null, "Generated files: " + DescribeFiles(result));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Contains("FreeSql", diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.False(result.ContainsFile("FreeSqlProductRepository.g.cs"), DescribeFiles(result));
    }

    [Fact]
    public void G02_EntitySourceGenerator_unknown_provider_reports_unknown_provider_error_instead_of_ef_default()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EntitySourceGenerator>(
            EntityDeclaration("Dapper"),
            new[] { EntityBaseStub });

        var diagnostic = result.Diagnostics.FirstOrDefault(
            d => d.Id == RepositoryGeneratorDiagnosticCodes.UnknownProvider);
        Assert.True(diagnostic != null, "Generated files: " + DescribeFiles(result));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Contains("Dapper", diagnostic.GetMessage(), System.StringComparison.Ordinal);
        Assert.False(result.ContainsFile("EfCoreProductRepository.g.cs"), DescribeFiles(result));
    }

    [Fact]
    public void G01_RepositorySourceGenerator_FreeSql_request_reports_non_ef_provider_error()
    {
        const string source = """
using System;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Enums;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.FreeSql)]
    public class FreeSqlThing : Entity<Guid>
    {
        public string Name { get; set; } = string.Empty;
    }
}
""";

        var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
            source,
            new[] { EntityBaseStub });

        var diagnostic = result.Diagnostics.FirstOrDefault(
            d => d.Id == RepositoryGeneratorDiagnosticCodes.NonEfProviderNotSupported);
        Assert.NotNull(diagnostic);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Contains("FreeSql", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    private static string DescribeFiles(SourceGeneratorResult result)
        => string.Join(", ", result.GeneratedSources.Select(source => source.FileName));
}
