#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// §8: the compile-time typed query adapter of one CRUD service. Filters and sorts resolve through
/// static switches over the field name to strongly-typed lambdas; filter values are read with
/// <c>FilterValueReader</c> per the field's frozen static kind; paging uses the existing
/// <c>PagedRequestDto</c> bounds. Unknown or non-supported fields, unsupported operators and
/// invalid sort directions are rejected with <c>QueryPlanException</c> before the query executes —
/// no reflection, no dynamic LINQ and no unfiltered fallback.
/// </summary>
internal static class CrudTypedQueryAdapterEmitter
{
    private static readonly IReadOnlyList<string> TypedQueryAdapterUsings =
    [
        "using System;",
        "using System.Collections.Generic;",
        "using System.Linq;",
        "using CrestCreates.Application.Contracts.DTOs.Common;",
        "using CrestCreates.Application.Contracts.Query;",
    ];

    internal static CrudArtifact EmitTypedQueryAdapter(CrudGenerationModel model)
    {
        var entity = model.EntityQualifiedDisplay;
        var entityName = model.EntityClassName;
        var adapter = model.TypedQueryAdapterName;
        var listRequest = Qualified(model.ListRequestFullName);
        var readDto = Qualified(model.ReadDtoFullName);
        var mappings = Qualified(model.MappingsNamespace + "." + model.MappingClassName);

        var builder = StartDocument(model);
        OpenNamespace(builder, model.ImplementationNamespace, TypedQueryAdapterUsings);

        Line(builder, Indent + "/// <summary>");
        Line(builder, Indent + "/// §8: the compile-time typed query adapter of the generated " + entityName
            + " CRUD search.");
        Line(builder, Indent + "/// Field names resolve through static switches to strongly-typed lambdas, filter values are");
        Line(builder, Indent + "/// read with FilterValueReader per the field's static CLR kind, and unknown or non-supported");
        Line(builder, Indent + "/// fields, operators or sort directions are rejected with QueryPlanException before the query");
        Line(builder, Indent + "/// executes. No reflection and no dynamic LINQ participate in this path.");
        Line(builder, Indent + "/// </summary>");
        Line(builder, Indent + "internal static class " + adapter);
        Line(builder, Indent + "{");

        EmitAdapterExecute(builder, entity, listRequest, readDto, mappings);
        EmitAdapterFilters(builder, model, entity, entityName);
        EmitAdapterSorts(builder, model, entity, entityName);

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(adapter + ".g.cs", builder);
    }

    private static void EmitAdapterExecute(
        StringBuilder builder,
        string entity,
        string listRequest,
        string readDto,
        string mappings)
    {
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Executes one bounded search: filters, stable ordering, paging and the generated projection.</summary>");
        Line(builder, NestedIndent + "public static PagedResultDto<" + readDto + "> Execute(");
        Line(builder, NestedIndent + "    IQueryable<" + entity + "> query,");
        Line(builder, NestedIndent + "    " + listRequest + " input)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    ArgumentNullException.ThrowIfNull(query);");
        Line(builder, NestedIndent + "    ArgumentNullException.ThrowIfNull(input);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    query = ApplyFilters(query, input.Filters);");
        Line(builder, NestedIndent + "    query = ApplySorts(query, input.Sorts);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var totalCount = query.Count();");
        Line(builder, NestedIndent + "    var entities = query.Skip(input.GetSkipCount()).Take(input.PageSize).ToList();");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var dtos = new List<" + readDto + ">(entities.Count);");
        Line(builder, NestedIndent + "    foreach (var entity in entities)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        dtos.Add(" + mappings + ".ToTarget(entity));");
        Line(builder, NestedIndent + "    }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    return new PagedResultDto<" + readDto
            + ">(dtos, totalCount, input.PageIndex, input.PageSize);");
        Line(builder, NestedIndent + "}");
    }

    private static void EmitAdapterFilters(
        StringBuilder builder,
        CrudGenerationModel model,
        string entity,
        string entityName)
    {
        var cases = new List<(string Name, CrudFieldModel Field)>();
        foreach (var name in model.FilterableFields)
        {
            var field = model.ReadFields.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (field is null || field.Kind == CrudFieldKind.Unsupported || cases.Any(item =>
                    string.Equals(item.Name, name, StringComparison.Ordinal)))
            {
                continue;
            }

            cases.Add((name, field));
        }

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Applies every request filter; a field without a supported typed reader is rejected, never ignored.</summary>");
        Line(builder, NestedIndent + "private static IQueryable<" + entity + "> ApplyFilters("
            + "IQueryable<" + entity + "> query, List<FilterDescriptor>? filters)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (filters is null || filters.Count == 0)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        return query;");
        Line(builder, NestedIndent + "    }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    foreach (var filter in filters)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        if (filter is null)");
        Line(builder, NestedIndent + "        {");
        Line(builder, NestedIndent + "            throw new QueryPlanException(");
        Line(builder, NestedIndent + "                \"Crest.Crud.InvalidFilterField\",");
        Line(builder, NestedIndent + "                \"Filter descriptors of " + entityName + " must not be null.\");");
        Line(builder, NestedIndent + "        }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "        query = ApplyFilter(query, filter);");
        Line(builder, NestedIndent + "    }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    return query;");
        Line(builder, NestedIndent + "}");

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "private static IQueryable<" + entity + "> ApplyFilter("
            + "IQueryable<" + entity + "> query, FilterDescriptor filter)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    switch (filter.Field)");
        Line(builder, NestedIndent + "    {");
        foreach (var (name, _) in cases)
        {
            Line(builder, TripleIndent + "case \"" + Escape(name) + "\":");
            Line(builder, TripleIndent + "    return ApplyFilter_" + name + "(query, filter);");
        }

        Line(builder, TripleIndent + "default:");
        Line(builder, TripleIndent + "    throw new QueryPlanException(");
        Line(builder, TripleIndent + "        \"Crest.Crud.InvalidFilterField\",");
        Line(builder, TripleIndent + "        \"Filter field '\" + filter.Field + \"' is not filterable on "
            + entityName + " by the generated query adapter.\");");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "}");

        foreach (var (_, field) in cases)
        {
            EmitAdapterFilterField(builder, entity, entityName, field);
        }
    }

    private static void EmitAdapterFilterField(
        StringBuilder builder,
        string entity,
        string entityName,
        CrudFieldModel field)
    {
        var reader = FilterValueReaderCall(field);
        var member = "entity." + field.Name;

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "private static IQueryable<" + entity + "> ApplyFilter_" + field.Name
            + "(IQueryable<" + entity + "> query, FilterDescriptor filter)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    switch (filter.Operator)");
        Line(builder, NestedIndent + "    {");

        switch (field.Kind)
        {
            case CrudFieldKind.String:
                EmitEqualityCases(builder, field, member, reader);
                EmitStringMatchCases(builder, field, member, reader);
                EmitInCases(builder, field, member);
                break;
            case CrudFieldKind.Bool:
                EmitEqualityCases(builder, field, member, reader);
                break;
            case CrudFieldKind.Guid:
            case CrudFieldKind.Enum:
                EmitEqualityCases(builder, field, member, reader);
                EmitInCases(builder, field, member);
                break;
            default:
                EmitEqualityCases(builder, field, member, reader);
                EmitComparisonCases(builder, field, member, reader);
                EmitInCases(builder, field, member);
                break;
        }

        if (field.IsNullable)
        {
            EmitNullCases(builder, field, member);
        }

        Line(builder, TripleIndent + "default:");
        Line(builder, TripleIndent + "    throw new QueryPlanException(");
        Line(builder, TripleIndent + "        \"Crest.Crud.UnsupportedFilterOperator\",");
        Line(builder, TripleIndent + "        \"Filter operator '\" + filter.Operator + \"' is not supported for field '\" + "
            + "filter.Field + \"' of " + entityName + ".\");");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "}");
    }

    /// <summary>
    /// Equals/NotEquals for every supported kind; the reader call fixes the value's static type.
    /// <see cref="decimal"/> filters are emitted operator-free on purpose: the runtime resolves
    /// user-defined operators for <c>==</c> inside expression trees by reflection, and that resolution
    /// deterministically fails under NativeAOT (S4 native gate probe: only decimal-eq/gt fail while
    /// member Equals/CompareTo construct fine). The semantics are identical.
    /// </summary>
    private static void EmitEqualityCases(StringBuilder builder, CrudFieldModel field, string member, string reader)
    {
        foreach (var operatorName in new[] { "Equals", "NotEquals" })
        {
            Line(builder, TripleIndent + "case FilterOperator." + operatorName + ":");
            Line(builder, TripleIndent + "{");
            Line(builder, QuadIndent + "var value = " + reader + "(filter.Value, filter.Field);");
            Line(builder, QuadIndent + "return query.Where(entity => "
                + EqualityExpression(field, member, operatorName) + ");");
            Line(builder, TripleIndent + "}");
        }
    }

    private static string EqualityExpression(CrudFieldModel field, string member, string operatorName)
    {
        if (field.Kind != CrudFieldKind.Decimal)
        {
            return member + (operatorName == "Equals" ? " == " : " != ") + "value";
        }

        var target = field.IsNullable ? member + ".Value" : member;
        return field.IsNullable
            ? operatorName == "Equals"
                ? member + ".HasValue && " + target + ".Equals(value)"
                : "!" + member + ".HasValue || !" + target + ".Equals(value)"
            : (operatorName == "Equals" ? string.Empty : "!") + target + ".Equals(value)";
    }

    private static void EmitComparisonCases(StringBuilder builder, CrudFieldModel field, string member, string reader)
    {
        foreach (var (operatorName, comparison) in new[]
        {
            ("GreaterThan", ">"),
            ("GreaterThanOrEqual", ">="),
            ("LessThan", "<"),
            ("LessThanOrEqual", "<="),
        })
        {
            Line(builder, TripleIndent + "case FilterOperator." + operatorName + ":");
            Line(builder, TripleIndent + "{");
            Line(builder, QuadIndent + "var value = " + reader + "(filter.Value, filter.Field);");
            Line(builder, QuadIndent + "return query.Where(entity => "
                + ComparisonExpression(field, member, comparison) + ");");
            Line(builder, TripleIndent + "}");
        }
    }

    private static string ComparisonExpression(CrudFieldModel field, string member, string comparison)
    {
        if (field.Kind != CrudFieldKind.Decimal)
        {
            return member + " " + comparison + " value";
        }

        var target = field.IsNullable ? member + ".Value" : member;
        var compare = target + ".CompareTo(value) " + comparison + " 0";
        return field.IsNullable ? member + ".HasValue && " + compare : compare;
    }

    private static void EmitStringMatchCases(
        StringBuilder builder,
        CrudFieldModel field,
        string member,
        string reader)
    {
        foreach (var method in new[] { "Contains", "StartsWith", "EndsWith" })
        {
            Line(builder, TripleIndent + "case FilterOperator." + method + ":");
            Line(builder, TripleIndent + "{");
            Line(builder, QuadIndent + "var value = " + reader + "(filter.Value, filter.Field);");
            Line(builder, QuadIndent + "return query.Where(entity => "
                + (field.IsNullable ? member + " != null && " : string.Empty)
                + member + "." + method + "(value));");
            Line(builder, TripleIndent + "}");
        }
    }

    /// <summary>
    /// In/NotIn read a same-kind scalar array with the field's own reader. Nullable fields keep an
    /// explicit null branch: a null member is never "in" a set, and "not in" keeps the null rows.
    /// </summary>
    private static void EmitInCases(StringBuilder builder, CrudFieldModel field, string member)
    {
        var elementType = FilterArrayElementType(field);
        var reader = FilterValueReaderCall(field);
        var arrayCall = "FilterValueReader.ReadArray<" + elementType + ">(filter.Value, filter.Field, " + reader + ")";

        var inExpression = field.IsNullable
            ? field.Kind == CrudFieldKind.String
                ? member + " != null && values.Contains(" + member + ")"
                : member + ".HasValue && values.Contains(" + member + ".Value)"
            : "values.Contains(" + member + ")";
        var notInExpression = field.IsNullable
            ? field.Kind == CrudFieldKind.String
                ? member + " == null || !values.Contains(" + member + ")"
                : "!" + member + ".HasValue || !values.Contains(" + member + ".Value)"
            : "!values.Contains(" + member + ")";

        foreach (var (operatorName, expression) in new[]
        {
            ("In", inExpression),
            ("NotIn", notInExpression),
        })
        {
            Line(builder, TripleIndent + "case FilterOperator." + operatorName + ":");
            Line(builder, TripleIndent + "{");
            Line(builder, QuadIndent + "var values = " + arrayCall + ";");
            Line(builder, QuadIndent + "return query.Where(entity => " + expression + ");");
            Line(builder, TripleIndent + "}");
        }
    }

    private static void EmitNullCases(StringBuilder builder, CrudFieldModel field, string member)
    {
        // Nullable decimal uses the HasValue form as well: the S4 native probe showed decimal's
        // user-defined operators cannot be resolved inside AOT expression trees, and `== null` on a
        // nullable decimal takes the same operator resolution path.
        if (field.Kind == CrudFieldKind.Decimal)
        {
            Line(builder, TripleIndent + "case FilterOperator.IsNull:");
            Line(builder, TripleIndent + "    return query.Where(entity => !" + member + ".HasValue);");
            Line(builder, TripleIndent + "case FilterOperator.IsNotNull:");
            Line(builder, TripleIndent + "    return query.Where(entity => " + member + ".HasValue);");
            return;
        }

        Line(builder, TripleIndent + "case FilterOperator.IsNull:");
        Line(builder, TripleIndent + "    return query.Where(entity => " + member + " == null);");
        Line(builder, TripleIndent + "case FilterOperator.IsNotNull:");
        Line(builder, TripleIndent + "    return query.Where(entity => " + member + " != null);");
    }

    private static void EmitAdapterSorts(
        StringBuilder builder,
        CrudGenerationModel model,
        string entity,
        string entityName)
    {
        var cases = new List<string>();
        foreach (var name in model.SortableFields)
        {
            var field = model.ReadFields.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (field is null || field.Kind == CrudFieldKind.Unsupported
                || cases.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            cases.Add(name);
        }

        // §8: the stable default ordering — CreationTime descending with an Id descending tiebreak when
        // the entity has CreationTime, otherwise Id descending.
        var defaultOrdering = model.EntityHasCreationTime
            ? "query.OrderByDescending(entity => entity.CreationTime).ThenByDescending(entity => entity.Id)"
            : "query.OrderByDescending(entity => entity.Id)";

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Applies the requested sorts, or the stable default ordering when none were requested.</summary>");
        Line(builder, NestedIndent + "private static IQueryable<" + entity + "> ApplySorts("
            + "IQueryable<" + entity + "> query, List<SortDescriptor>? sorts)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (sorts is null || sorts.Count == 0)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        return " + defaultOrdering + ";");
        Line(builder, NestedIndent + "    }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    IOrderedQueryable<" + entity + ">? ordered = null;");
        Line(builder, NestedIndent + "    foreach (var sort in sorts)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        if (sort is null)");
        Line(builder, NestedIndent + "        {");
        Line(builder, NestedIndent + "            throw new QueryPlanException(");
        Line(builder, NestedIndent + "                \"Crest.Crud.InvalidSortField\",");
        Line(builder, NestedIndent + "                \"Sort descriptors of " + entityName + " must not be null.\");");
        Line(builder, NestedIndent + "        }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "        ordered = ApplySort(query, ordered, sort);");
        Line(builder, NestedIndent + "    }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    return ordered ?? query;");
        Line(builder, NestedIndent + "}");

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "private static IOrderedQueryable<" + entity + "> ApplySort(");
        Line(builder, NestedIndent + "    IQueryable<" + entity + "> source,");
        Line(builder, NestedIndent + "    IOrderedQueryable<" + entity + ">? ordered,");
        Line(builder, NestedIndent + "    SortDescriptor sort)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    switch (sort.Field)");
        Line(builder, NestedIndent + "    {");
        foreach (var name in cases)
        {
            Line(builder, TripleIndent + "case \"" + Escape(name) + "\" when sort.Direction == SortDirection.Ascending:");
            Line(builder, TripleIndent + "    return ordered is null");
            Line(builder, TripleIndent + "        ? source.OrderBy(entity => entity." + name + ")");
            Line(builder, TripleIndent + "        : ordered.ThenBy(entity => entity." + name + ");");
            Line(builder, TripleIndent + "case \"" + Escape(name) + "\" when sort.Direction == SortDirection.Descending:");
            Line(builder, TripleIndent + "    return ordered is null");
            Line(builder, TripleIndent + "        ? source.OrderByDescending(entity => entity." + name + ")");
            Line(builder, TripleIndent + "        : ordered.ThenByDescending(entity => entity." + name + ");");
        }

        Line(builder, TripleIndent + "default:");
        Line(builder, TripleIndent + "    throw new QueryPlanException(");
        Line(builder, TripleIndent + "        \"Crest.Crud.InvalidSortField\",");
        Line(builder, TripleIndent + "        \"Sort field '\" + sort.Field + \"' with direction '\" + sort.Direction + \"' is not supported by the generated "
            + entityName + " query adapter.\");");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "}");
    }

    /// <summary>The bounded FilterValueReader call for a field's frozen static kind.</summary>
    private static string FilterValueReaderCall(CrudFieldModel field) => field.Kind switch
    {
        CrudFieldKind.String => "FilterValueReader.ReadString",
        CrudFieldKind.Bool => "FilterValueReader.ReadBool",
        CrudFieldKind.Int32 => "FilterValueReader.ReadInt32",
        CrudFieldKind.Int64 => "FilterValueReader.ReadInt64",
        CrudFieldKind.Decimal => "FilterValueReader.ReadDecimal",
        CrudFieldKind.Double => "FilterValueReader.ReadDouble",
        CrudFieldKind.Guid => "FilterValueReader.ReadGuid",
        CrudFieldKind.DateTime => "FilterValueReader.ReadDateTime",
        CrudFieldKind.Enum => "FilterValueReader.ReadEnum<" + field.EnumTypeDisplay + ">",
        _ => throw new InvalidOperationException(
            "Field '" + field.Name + "' has no typed filter reader (kind " + field.Kind + ")."),
    };

    /// <summary>The element type of the same-kind array a supported In/NotIn field reads.</summary>
    private static string FilterArrayElementType(CrudFieldModel field) => field.Kind switch
    {
        CrudFieldKind.String => "string",
        CrudFieldKind.Int32 => "int",
        CrudFieldKind.Int64 => "long",
        CrudFieldKind.Decimal => "decimal",
        CrudFieldKind.Double => "double",
        CrudFieldKind.Guid => "global::System.Guid",
        CrudFieldKind.DateTime => "global::System.DateTime",
        CrudFieldKind.Enum => field.EnumTypeDisplay!,
        _ => throw new InvalidOperationException(
            "Field '" + field.Name + "' has no supported array reader (kind " + field.Kind + ")."),
    };
}
