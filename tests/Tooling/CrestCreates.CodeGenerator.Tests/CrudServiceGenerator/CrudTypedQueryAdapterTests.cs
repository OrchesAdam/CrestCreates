using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Tooling.CrudGeneration;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S3 (§8): the generated CRUD search must stop calling the reflective
/// <c>QueryExecutor&lt;T&gt;.ApplyFilters/ApplySorts</c> — including the default ordering — and go
/// through the generated typed query adapter instead. These baselines run the real precompile CLI
/// (<see cref="CrudPrecompileProducer"/>), compile the materialized bundle as ordinary input next to
/// the real sibling generators, and assert the emitted adapter shape: the static field/operator
/// switches, the per-kind <c>FilterValueReader</c> calls, the stable default ordering, the paging
/// bounds and the deterministic plan rejections. The assertion strings are the exact emitted text, so
/// a mutation that reintroduces the reflective call or drops a per-kind reader fails here.
/// </summary>
public class CrudTypedQueryAdapterTests
{
    /// <summary>
    /// Kind coverage declaration: audited entity (CreationTime present) with every supported field kind
    /// plus a nullable wrapper per family and one unsupported kind (byte[]), so the emitted matrix can
    /// be checked per kind, per nullability and per unsupported-kind rejection.
    /// </summary>
    private const string KindCoverageDeclarationSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Domain.Entities
        {
            public enum ProductStatus
            {
                Draft = 0,
                Active = 1,
                Retired = 2,
            }

            public class Product
            {
                public Guid Id { get; set; }
                public DateTime CreationTime { get; set; }
                public string Name { get; set; } = string.Empty;
                public string? Description { get; set; }
                public decimal Price { get; set; }
                public decimal? Discount { get; set; }
                public int Stock { get; set; }
                public long Views { get; set; }
                public double Weight { get; set; }
                public bool IsActive { get; set; }
                public Guid? OwnerId { get; set; }
                public ProductStatus Status { get; set; }
                public ProductStatus? LastStatus { get; set; }
                public byte[] Thumbnail { get; set; } = Array.Empty<byte>();
            }
        }

        namespace Shop.Application.Products
        {
            [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
            public partial class ProductAppService
            {
            }
        }
        """;

    /// <summary>
    /// The negative control: excluding the only property leaves the entity with no queryable field at
    /// all, so the adapter must materialize with rejection-only switches (Id stays protected on the
    /// mapper side, which keeps the declaration itself sound).
    /// </summary>
    private const string NoQueryableFieldsDeclarationSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Domain.Entities
        {
            public class Ticket
            {
                public Guid Id { get; set; }
            }
        }

        namespace Shop.Application.Tickets
        {
            [GenerateCrudService(typeof(Shop.Domain.Entities.Ticket),
                RoutePrefix = "/api/tickets",
                ExcludeProperties = new[] { "Id" })]
            public partial class TicketAppService
            {
            }
        }
        """;

    /// <summary>Declared sets restrict which fields the adapter emits filter/sort cases for.</summary>
    private const string DeclaredOverridesDeclarationSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Domain.Entities
        {
            public class Order
            {
                public Guid Id { get; set; }
                public string Number { get; set; } = string.Empty;
                public decimal Total { get; set; }
            }
        }

        namespace Shop.Application.Orders
        {
            [GenerateCrudService(typeof(Shop.Domain.Entities.Order),
                RoutePrefix = "/api/orders",
                FilterableProperties = new[] { "Number" },
                SortableProperties = new[] { "Total" })]
            public partial class OrderAppService
            {
            }
        }
        """;

    private static readonly string[] DeclaredReferenceNames =
    [
        "System.Runtime",
        "netstandard",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Linq",
        "System.Linq.Expressions",
        // The adapter is the first generated artifact that needs the Queryable extension assembly
        // (System.Linq.Queryable.dll) — the precompile bundle must compile as ordinary input.
        "System.Linq.Queryable",
        "System.Threading.Tasks",
        "System.ComponentModel",
        "System.ComponentModel.Annotations",
        "System.Text.Json",
        // The STJ-generated context tree closes over JavaScriptEncoder (System.Text.Encodings.Web).
        "System.Text.Encodings.Web",
        "CrestCreates.Domain",
        "CrestCreates.Authorization.Abstractions",
        "CrestCreates.Application.Contracts",
        "CrestCreates.Core.Abstractions",
        "CrestCreates.DynamicApi",
        "CrestCreates.DynamicApi.Abstractions",
        "CrestCreates.Capability.Abstractions",
        "CrestCreates.Metadata.Abstractions",
        // The capability descriptor contract exposes SchemaDescriptor members.
        "CrestCreates.Schema.Abstractions",
        "CrestCreates.Validation",
        // Reusable[Aop.Interceptors.UnitOfWorkMo] needs its attribute assembly.
        "CrestCreates.Aop",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Mvc.Core",
        "Rougamo",
    ];

    /// <summary>
    /// The harness closure of this baseline is deliberately wider than the naming baselines': these
    /// tests assert that the bundle compiles as ordinary input (<c>BundleCompileErrors</c> empty), so
    /// every reference the emitted surface actually needs must be present instead of being reported as
    /// a closure gap.
    /// </summary>
    private static IReadOnlyList<MetadataReference> ClosureReferences()
    {
        var referencePaths = new List<string> { typeof(object).Assembly.Location };
        referencePaths.Add(typeof(GenerateCrudServiceAttribute).Assembly.Location);
        foreach (var referenceName in DeclaredReferenceNames)
        {
            try
            {
                referencePaths.Add(System.Reflection.Assembly.Load(referenceName).Location);
            }
            catch (Exception)
            {
                // Unavailable optional reference: the probe documents support only for the closure
                // that is actually present.
            }
        }

        referencePaths.AddRange(CrudConsumerFrameworkClosure.AspNetCoreSharedFrameworkAssemblies());
        return referencePaths
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static CrudPrecompileProducer.Output Run(string source) =>
        CrudPrecompileProducer.Run(source, ClosureReferences());

    private static string BundleOf(CrudPrecompileProducer.Output producer) =>
        string.Join("\n", producer.BundleTrees.Select(tree => tree.ToString()));

    private static void AssertNoReflectiveQueryExecutor(string bundle) =>
        bundle.Should().NotContain("QueryExecutor",
            "the formal CRUD search must never route through the reflective QueryExecutor<T>");

    [Fact]
    public void Bundle_must_materialize_the_typed_query_adapter_without_the_reflective_query_executor()
    {
        var producer = Run(KindCoverageDeclarationSource);
        producer.Diagnostics.Should().BeEmpty(
            "the kind-coverage declaration must build a clean model: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));

        // The model carries the frozen kind and the declared-or-default sets the adapter emits from.
        var model = producer.Models.Should().ContainSingle().Subject;
        model.EntityHasCreationTime.Should().BeTrue(
            "the entity declares CreationTime, so the stable default ordering is CreationTime desc");
        model.FilterableFields.Should().Equal(model.QueryableFields,
            "an absent FilterableProperties declaration means every queryable field");
        model.SortableFields.Should().Equal(model.QueryableFields,
            "an absent SortableProperties declaration means every queryable field");
        model.ReadFields.Single(field => field.Name == "Description").IsNullable.Should().BeTrue(
            "string? is a nullable field and must get null-comparison cases instead of a rejection");
        model.ReadFields.Single(field => field.Name == "Description").Kind.Should().Be(CrudFieldKind.String);
        model.ReadFields.Single(field => field.Name == "Price").Kind.Should().Be(CrudFieldKind.Decimal);
        model.ReadFields.Single(field => field.Name == "Stock").Kind.Should().Be(CrudFieldKind.Int32);
        model.ReadFields.Single(field => field.Name == "Views").Kind.Should().Be(CrudFieldKind.Int64);
        model.ReadFields.Single(field => field.Name == "Weight").Kind.Should().Be(CrudFieldKind.Double);
        model.ReadFields.Single(field => field.Name == "IsActive").Kind.Should().Be(CrudFieldKind.Bool);
        model.ReadFields.Single(field => field.Name == "OwnerId").Kind.Should().Be(CrudFieldKind.Guid);
        model.ReadFields.Single(field => field.Name == "Status").Kind.Should().Be(CrudFieldKind.Enum);
        model.ReadFields.Single(field => field.Name == "Status").EnumTypeDisplay
            .Should().Be("global::Shop.Domain.Entities.ProductStatus");
        model.ReadFields.Single(field => field.Name == "Thumbnail").Kind.Should().Be(CrudFieldKind.Unsupported,
            "byte[] has no bounded reader, so it is filterable/sortable nowhere");

        var bundle = BundleOf(producer);

        // The adapter type itself and the one call site GetListAsync now uses.
        bundle.Should().Contain("internal static class ProductTypedQueryAdapter",
            "the search execution lives in the generated, strongly-typed adapter"); 
        bundle.Should().Contain(
            "public static PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto> Execute(",
            "the adapter entry takes the paged request and returns the paged result");
        bundle.Should().Contain("global::Shop.Application.Products.ProductTypedQueryAdapter.Execute(query, input);",
            "GetListAsync must call the adapter instead of the reflective executor");

        // The reflective path is gone from the bundle, guards included: the adapter's switches are the
        // one plan-rejection authority.
        AssertNoReflectiveQueryExecutor(bundle);
        bundle.Should().NotContain("EnsureAllowedFilterFields");
        bundle.Should().NotContain("EnsureAllowedSortFields");
        bundle.Should().NotContain("AllowedQueryFields");

        // §8 stable default ordering: CreationTime desc with the Id desc tiebreak.
        bundle.Should().Contain(
            "return query.OrderByDescending(entity => entity.CreationTime).ThenByDescending(entity => entity.Id);",
            "the default ordering must be part of the adapter, not of the reflective ApplySorts");

        // Paging reuses the PagedRequestDto bounds.
        bundle.Should().Contain(
            "var entities = query.Skip(input.GetSkipCount()).Take(input.PageSize).ToList();",
            "paging comes from the existing PagedRequestDto bounds");

        // One bounded reader per supported field kind, chosen from the frozen static kind.
        string[] requiredReaderCalls =
        [
            "FilterValueReader.ReadString(filter.Value, filter.Field)",
            "FilterValueReader.ReadBool(filter.Value, filter.Field)",
            "FilterValueReader.ReadInt32(filter.Value, filter.Field)",
            "FilterValueReader.ReadInt64(filter.Value, filter.Field)",
            "FilterValueReader.ReadDecimal(filter.Value, filter.Field)",
            "FilterValueReader.ReadDouble(filter.Value, filter.Field)",
            "FilterValueReader.ReadGuid(filter.Value, filter.Field)",
            "FilterValueReader.ReadDateTime(filter.Value, filter.Field)",
            "FilterValueReader.ReadEnum<global::Shop.Domain.Entities.ProductStatus>(filter.Value, filter.Field)",
        ];
        foreach (var readerCall in requiredReaderCalls)
        {
            bundle.Should().Contain(readerCall, "every supported kind gets its own typed reader call");
        }

        // The operator matrix in exact emitted lambdas: string matching, comparisons, In/NotIn with
        // the same-kind array reader, the null branch of nullable fields and the lifted nullable In.
        // decimal is the one kind emitted operator-free: the S4 native probe showed the runtime cannot
        // resolve decimal's user-defined operators inside AOT expression trees, while member
        // Equals/CompareTo and HasValue construct fine (identical semantics).
        string[] requiredExpressions =
        [
            "return query.Where(entity => entity.Name == value);",
            "return query.Where(entity => entity.Name.Contains(value));",
            "return query.Where(entity => entity.Description != null && entity.Description.Contains(value));",
            "return query.Where(entity => entity.Description == null);",
            "return query.Where(entity => entity.Description != null);",
            "return query.Where(entity => entity.Price.Equals(value));",
            "return query.Where(entity => !entity.Price.Equals(value));",
            "return query.Where(entity => entity.Price.CompareTo(value) > 0);",
            "return query.Where(entity => entity.Price.CompareTo(value) >= 0);",
            "return query.Where(entity => entity.Price.CompareTo(value) < 0);",
            "return query.Where(entity => entity.Price.CompareTo(value) <= 0);",
            "return query.Where(entity => entity.Discount.HasValue && entity.Discount.Value.Equals(value));",
            "return query.Where(entity => !entity.Discount.HasValue || !entity.Discount.Value.Equals(value));",
            "return query.Where(entity => !entity.Discount.HasValue);",
            "return query.Where(entity => entity.Discount.HasValue);",
            "return query.Where(entity => entity.Stock >= value);",
            "return query.Where(entity => entity.IsActive == value);",
            "var values = FilterValueReader.ReadArray<global::System.Guid>(filter.Value, filter.Field, FilterValueReader.ReadGuid);",
            "return query.Where(entity => values.Contains(entity.Name));",
            "return query.Where(entity => entity.OwnerId.HasValue && values.Contains(entity.OwnerId.Value));",
            "return query.Where(entity => !entity.OwnerId.HasValue || !values.Contains(entity.OwnerId.Value));",
            "return query.Where(entity => entity.LastStatus.HasValue && values.Contains(entity.LastStatus.Value));",
        ];
        foreach (var expression in requiredExpressions)
        {
            bundle.Should().Contain(expression, "the operator matrix must be emitted as static typed lambdas");
        }

        // Sorts: static switch, strong-typed OrderBy/OrderByDescending/ThenBy/ThenByDescending.
        bundle.Should().Contain("case \"Name\" when sort.Direction == SortDirection.Ascending:");
        bundle.Should().Contain("case \"Price\" when sort.Direction == SortDirection.Descending:");
        bundle.Should().Contain("? source.OrderByDescending(entity => entity.Price)");
        bundle.Should().Contain(": ordered.ThenBy(entity => entity.Name);");

        // Per-kind operator sets: bool has no In/NotIn case (its method must not contain an array read),
        // and the unsupported kind gets no case at all.
        const string fieldMethodPrefix = "private static IQueryable<global::Shop.Domain.Entities.Product> ";
        var isActiveMethod = SectionBetween(
            bundle, fieldMethodPrefix + "ApplyFilter_IsActive(", fieldMethodPrefix + "ApplyFilter_OwnerId(");
        isActiveMethod.Should().NotContain("FilterValueReader.ReadArray<bool>",
            "bool supports Equals/NotEquals only");
        isActiveMethod.Should().NotContain("FilterOperator.GreaterThan",
            "bool has no ordering comparisons");
        bundle.Should().NotContain("ApplyFilter_Thumbnail");
        bundle.Should().NotContain("case \"Thumbnail\"");

        // Plan rejections are the adapter's dedicated exception, with the retired guard codes kept.
        bundle.Should().Contain("\"Crest.Crud.InvalidFilterField\"");
        bundle.Should().Contain("\"Crest.Crud.UnsupportedFilterOperator\"");
        bundle.Should().Contain("\"Crest.Crud.InvalidSortField\"");
        bundle.Should().Contain("throw new QueryPlanException(");
    }

    [Fact]
    public void Bundle_with_the_typed_query_adapter_must_compile_as_ordinary_input()
    {
        // The bundle is compiled next to the official STJ generator and the ObjectMapping generator,
        // with a closure that carries every reference the emitted surface actually needs (see
        // ClosureReferences). A non-empty result means the emitted adapter is not valid ordinary C#.
        var producer = Run(KindCoverageDeclarationSource);

        producer.BundleCompileErrors.Should().BeEmpty(
            "the materialized bundle must compile as ordinary input; observed: "
            + string.Join("\n---\n", producer.BundleCompileErrors));
    }

    [Fact]
    public void Entity_without_queryable_fields_must_still_emit_adapter_rejections_and_no_reflective_executor()
    {
        // Negative control: with ExcludeProperties removing the only field, no (filter, operator) or
        // sort combination is supported. The adapter must still exist — with rejection-only switches —
        // and the bundle must not fall back to the reflective executor.
        var producer = Run(NoQueryableFieldsDeclarationSource);
        producer.Diagnostics.Should().BeEmpty(
            "the control declaration must build a clean model: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));

        var model = producer.Models.Should().ContainSingle().Subject;
        model.QueryableFields.Should().BeEmpty("the control exists to have no queryable field at all");
        model.EntityHasCreationTime.Should().BeFalse();

        var bundle = BundleOf(producer);
        bundle.Should().Contain("internal static class TicketTypedQueryAdapter",
            "even the empty matrix must materialize the adapter the service calls");
        AssertNoReflectiveQueryExecutor(bundle);

        // No supported field: no per-field method and no sort case may exist...
        bundle.Should().NotContain("FilterValueReader.Read");
        bundle.Should().NotContain("ApplyFilter_");
        bundle.Should().NotContain("when sort.Direction");

        // ...while both deterministic rejection paths stay present, and the default ordering falls back
        // to Id desc because the entity has no CreationTime.
        bundle.Should().Contain("return query.OrderByDescending(entity => entity.Id);");
        bundle.Should().Contain("\"Crest.Crud.InvalidFilterField\"");
        bundle.Should().Contain("\"Crest.Crud.InvalidSortField\"");
        bundle.Should().Contain("throw new QueryPlanException(");
    }

    [Fact]
    public void Declared_filterable_and_sortable_sets_must_restrict_the_adapter_switches()
    {
        // FilterableProperties/SortableProperties restrict the emitted matrix: they can narrow the
        // queryable fields but never widen them, and the other queryable fields must not get cases.
        var producer = Run(DeclaredOverridesDeclarationSource);
        producer.Diagnostics.Should().BeEmpty(
            "the declared sets name real fields, so no CCCRUD007 may fire: "
            + string.Join(" | ", producer.Diagnostics.Select(issue => issue.ToDisplayString())));

        var model = producer.Models.Should().ContainSingle().Subject;
        model.QueryableFields.Should().Equal("Id", "Number", "Total");
        model.FilterableFields.Should().Equal("Number");
        model.SortableFields.Should().Equal("Total");

        var bundle = BundleOf(producer);
        AssertNoReflectiveQueryExecutor(bundle);
        bundle.Should().Contain("case \"Number\":");
        bundle.Should().Contain("ApplyFilter_Number(");
        bundle.Should().NotContain("ApplyFilter_Total(",
            "Total is queryable but not declared filterable");
        bundle.Should().Contain("case \"Total\" when sort.Direction == SortDirection.Ascending:");
        bundle.Should().NotContain("case \"Number\" when",
            "Number is queryable but not declared sortable");
        bundle.Should().NotContain("case \"Id\" when",
            "Id is queryable but not declared sortable");
    }

    /// <summary>The exact text between two anchors of the emitted bundle, so a field method's matrix can be checked in isolation.</summary>
    private static string SectionBetween(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0, $"'{start}' must be part of the emitted adapter");
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        to.Should().BeGreaterThan(from, $"'{end}' must follow '{start}' in the emitted adapter");
        return text[from..to];
    }
}
