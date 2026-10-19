using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 S2.1: the shared hand-written projection model is built from the real attributes and
/// real symbols. The anchor never contributes methods of its own and referenced types are recorded
/// separately from declared ownership.
/// </summary>
public sealed class AppServiceHttpProjectionModelTests
{
    private const string ContractAndAnchor = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Contracts
        {
            public sealed class WidgetDto { public Guid Id { get; set; } }
            public sealed class CreateWidgetDto { public string Name { get; set; } = string.Empty; }

            public interface IWidgetAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Widget.Get" })]
                Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Create" })]
                Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "assign", Kind = HttpProjectionOperationKind.Command, Permissions = new[] { "Widget.Update" })]
                Task<WidgetDto> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{id}", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Update" })]
                Task<WidgetDto> UpdateAsync(Guid id, CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "by-customer/{customerId}", Kind = HttpProjectionOperationKind.Query)]
                Task<List<WidgetDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
            }
        }

        namespace Shop.Http
        {
            [CapabilityCompatibilityProjection(
                ContractType = typeof(Shop.Contracts.IWidgetAppService),
                RoutePrefix = "api/widgets",
                CapabilityIdPrefix = "compat.appservice.widget")]
            public static class WidgetHttpProjection
            {
                public static string NotAContractMethod() => "never projected";
            }
        }
        """;

    [Fact]
    public void Contract_anchor_projects_the_frozen_wire_matrix()
    {
        var result = Build(ContractAndAnchor);

        result.HasErrors.Should().BeFalse(string.Join(" | ", result.Diagnostics.Select(d => d.ToDisplayString())));
        var model = result.Model!;
        model.ContractName.Should().Be("IWidgetAppService");
        model.RoutePrefix.Should().Be("api/widgets");
        model.CapabilityIdPrefix.Should().Be("compat.appservice.widget");
        model.Actions.Select(a => a.Name).Should().BeEquivalentTo(
            new[] { "GetByIdAsync", "CreateAsync", "AssignAsync", "UpdateAsync", "GetByCustomerAsync" },
            "the anchor's own public method must never be projected");

        var getById = model.Actions.Single(a => a.Name == "GetByIdAsync");
        getById.HttpMethod.Should().Be("GET");
        getById.RoutePattern.Should().Be("{id}");
        getById.OperationKind.Should().Be(AppServiceHttpOperationKind.Query);
        getById.Permissions.Should().BeEquivalentTo("Widget.Get");
        getById.RouteIdentityParameterName.Should().Be("id");
        getById.ScalarInputs.Should().ContainSingle(p => p.Binding == "route" && p.Name == "id");
        getById.InputEnvelopeFullName.Should().BeNull("a single route identity needs no composed envelope");
        getById.CancellationTokenParameterName.Should().Be("cancellationToken");

        var create = model.Actions.Single(a => a.Name == "CreateAsync");
        create.HttpMethod.Should().Be("POST");
        create.RoutePattern.Should().BeEmpty();
        create.BodyPolicy.Should().Be(AppServiceHttpBodyPolicy.Required);
        create.BodyTypeFullName.Should().Contain("CreateWidgetDto");
        create.InputEnvelopeFullName.Should().BeNull("a single body parameter binds directly");

        var update = model.Actions.Single(a => a.Name == "UpdateAsync");
        update.InputEnvelopeFullName.Should().Be(
            "CrestCreates.Generated.Shop_002EHttp__Shop_002EContracts_002EIWidgetAppService__Update_CompatibilityInput",
            "the envelope name carries the full owner identity (assembly + contract), not the short service name");
        update.BodyPolicy.Should().Be(AppServiceHttpBodyPolicy.Required);
        update.RouteIdentityParameterName.Should().Be("id");

        var assign = model.Actions.Single(a => a.Name == "AssignAsync");
        assign.HttpMethod.Should().Be("GET", "the frozen compatibility matrix keeps the GET write routes");
        assign.OperationKind.Should().Be(AppServiceHttpOperationKind.Command,
            "a GET route may still be a command; the verb alone never decides");
        assign.ScalarInputs.Should().BeEquivalentTo(new[]
        {
            new { Name = "id", Binding = "query" },
            new { Name = "agentId", Binding = "query" },
        });
        assign.InputEnvelopeFullName.Should().Be(
            "CrestCreates.Generated.Shop_002EHttp__Shop_002EContracts_002EIWidgetAppService__Assign_CompatibilityInput",
            "two query parameters must be preserved through the composed envelope; dropping them was the S22A-R1-01 P1");

        var byCustomer = model.Actions.Single(a => a.Name == "GetByCustomerAsync");
        byCustomer.RoutePattern.Should().Be("by-customer/{customerId}");
        byCustomer.RouteIdentityParameterName.Should().Be("customerId");

        model.Actions.Select(a => a.CapabilityId).Should().OnlyHaveUniqueItems();
        model.Actions.Select(a => a.EndpointId).Should().OnlyHaveUniqueItems();
        model.Actions.Select(a => a.ClosedContractMethodSignature).Should().OnlyHaveUniqueItems();

        model.ReferencedTypes.Should().BeEmpty(
            "contract and DTOs live in the owner compilation, so they are declared, not referenced");
    }

    [Fact]
    public void Referenced_contract_and_dtos_are_recorded_separately()
    {
        var referencedImage = SourceGeneratorTestHelper.EmitAssemblyImage(
            "Shop.Referenced",
            new[]
            {
                """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using CrestCreates.Domain.Shared.Attributes;

                namespace Shop.Referenced
                {
                    public sealed class QuoteDto { public Guid Id { get; set; } }

                    public interface IQuoteService
                    {
                        [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Quote.Get" })]
                        Task<QuoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
                    }
                }
                """,
            },
            additionalReferences: new[] { "CrestCreates.Domain.Shared" });

        var compilation = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "Shop.Http",
            new[]
            {
                """
                using CrestCreates.Domain.Shared.Attributes;

                namespace Shop.Http
                {
                    [CapabilityCompatibilityProjection(
                        ContractType = typeof(Shop.Referenced.IQuoteService),
                        RoutePrefix = "api/quotes")]
                    public static class QuoteHttpProjection { }
                }
                """,
            },
            new[] { MetadataReference.CreateFromImage(referencedImage) });

        var anchor = compilation.GetTypeByMetadataName("Shop.Http.QuoteHttpProjection")!;
        var result = AppServiceHttpProjectionModelBuilder.Build(compilation, anchor);

        result.HasErrors.Should().BeFalse(string.Join(" | ", result.Diagnostics.Select(d => d.ToDisplayString())));
        var model = result.Model!;
        model.ContractAssemblyName.Should().Be("Shop.Referenced");
        model.ReferencedTypes.Should().Contain(reference =>
            reference.IsContract && reference.AssemblyName == "Shop.Referenced"
            && reference.FullMetadataName == "Shop.Referenced.IQuoteService");
        model.ReferencedTypes.Should().Contain(reference =>
            !reference.IsContract && reference.FullMetadataName == "Shop.Referenced.QuoteDto");
        AppServiceHttpProjectionEmitter.PlanJsonRoots(
                model,
                AppServiceHttpEmissionOptions.ForContract(model.ContractFullName) with
                {
                    JsonContextFullName = "global::Review.JsonStub",
                })
            .Select(root => root.ClrTypeDisplay ?? root.TypeDisplay)
            .Should().Contain(name => name.Contains("QuoteDto"));
    }

    [Fact]
    public void Contract_type_must_be_a_closed_interface()
    {
        var result = Build("""
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public sealed class NotAnInterface { }

                [CapabilityCompatibilityProjection(ContractType = typeof(NotAnInterface))]
                public static class BadHttpProjection { }
            }
            """);

        result.Model.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(d =>
            d.Id == AppServiceHttpDiagnosticCodes.UnsupportedDeclaration
            && d.Severity == AppServiceHttpDiagnosticSeverity.Error);
    }

    [Fact]
    public void Legacy_static_mode_is_left_to_the_legacy_front_end()
    {
        var result = Build("""
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public interface ILegacyService
                {
                    Task<string> PingAsync(CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(
                    ContractType = typeof(ILegacyService),
                    RegistrationMode = HttpProjectionRegistrationMode.LegacyStatic)]
                public static class LegacyHttpProjection { }
            }
            """);

        result.Model.Should().BeNull("the formal model only owns explicit-host projections");
        result.Diagnostics.Should().BeEmpty("declining ownership is not a diagnostic");
    }

    [Fact]
    public void Missing_cancellation_token_is_a_warning_not_a_silent_pass()
    {
        var result = Build("""
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public interface INoTokenService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "ping")]
                    Task<string> PingAsync();
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(INoTokenService))]
                public static class NoTokenHttpProjection { }
            }
            """);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d =>
            d.Id == AppServiceHttpDiagnosticCodes.ActionDeclarationContradiction
            && d.Severity == AppServiceHttpDiagnosticSeverity.Warning
            && d.Message.Contains("CancellationToken"));
    }

    [Fact]
    public void Duplicate_method_and_route_is_rejected()
    {
        var result = Build("""
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public interface IDuplicateService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "same")]
                    Task<string> FirstAsync(CancellationToken cancellationToken = default);

                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "same")]
                    Task<string> SecondAsync(CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(IDuplicateService))]
                public static class DuplicateHttpProjection { }
            }
            """);

        result.Diagnostics.Should().Contain(d =>
            d.Id == AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict
            && d.Severity == AppServiceHttpDiagnosticSeverity.Error);
    }

    [Fact]
    public void Implementation_and_contract_must_not_declare_conflicting_values()
    {
        var result = Build("""
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public interface IConflictService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "conflict")]
                    Task<string> RunAsync(CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(IConflictService))]
                public sealed class ConflictFacade : IConflictService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "other")]
                    public Task<string> RunAsync(CancellationToken cancellationToken = default)
                        => Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(d =>
            d.Id == AppServiceHttpDiagnosticCodes.ActionDeclarationContradiction
            && d.Severity == AppServiceHttpDiagnosticSeverity.Error
            && d.Message.Contains("different values"));
    }

    private static AppServiceHttpProjectionBuildResult Build(string source)
    {
        var compilation = SourceGeneratorTestHelper.CreateAnalysisCompilation("Shop.Http", new[] { source });
        var anchor = compilation.GetTypeByMetadataName("Shop.Http.WidgetHttpProjection")
                     ?? compilation.GetTypeByMetadataName("Shop.Http.BadHttpProjection")
                     ?? compilation.GetTypeByMetadataName("Shop.Http.LegacyHttpProjection")
                     ?? compilation.GetTypeByMetadataName("Shop.Http.NoTokenHttpProjection")
                     ?? compilation.GetTypeByMetadataName("Shop.Http.DuplicateHttpProjection")
                     ?? compilation.GetTypeByMetadataName("Shop.Http.ConflictFacade");
        anchor.Should().NotBeNull("the test source must declare its anchor");
        return AppServiceHttpProjectionModelBuilder.Build(compilation, anchor!);
    }
}
