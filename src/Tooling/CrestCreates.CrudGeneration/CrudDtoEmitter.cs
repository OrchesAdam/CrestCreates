#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Contracts DTO artifacts: read/create/update contracts and the bounded list request.
/// </summary>
internal static class CrudDtoEmitter
{
    private static readonly IReadOnlyList<string> ReadDtoUsings = ["using System;"];

    internal static CrudArtifact EmitReadDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, ReadDtoUsings);
        Line(builder, Indent + "public partial class " + model.ReadDtoName);
        Line(builder, Indent + "{");
        foreach (var field in model.ReadFields)
        {
            EmitField(builder, field, validationAttributes: false);
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.ReadDtoName + ".g.cs", builder);
    }

    internal static CrudArtifact EmitCreateDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, DtoUsings(model.CreateFields));
        Line(builder, Indent + "public partial class " + model.CreateDtoName);
        Line(builder, Indent + "{");
        foreach (var field in model.CreateFields)
        {
            EmitField(builder, field, validationAttributes: true);
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.CreateDtoName + ".g.cs", builder);
    }

    internal static CrudArtifact EmitUpdateDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, DtoUsings(model.UpdateFields));
        Line(builder, Indent + "public partial class " + model.UpdateDtoName);
        Line(builder, Indent + "{");
        foreach (var field in model.UpdateFields)
        {
            EmitField(builder, field, validationAttributes: true);
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.UpdateDtoName + ".g.cs", builder);
    }

    internal static CrudArtifact EmitListRequestDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, ReadDtoUsings);
        Line(builder, Indent + "public partial class " + model.ListRequestDtoName
            + " : global::" + CrudResponseContract.PagedResultNamespace + ".PagedRequestDto");
        Line(builder, Indent + "{");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.ListRequestDtoName + ".g.cs", builder);
    }

    private static IEnumerable<string> DtoUsings(IReadOnlyList<CrudFieldModel> fields)
    {
        yield return "using System;";
        if (fields.Any(field => field.IsRequiredString))
        {
            yield return "using System.ComponentModel.DataAnnotations;";
        }
    }

    private static void EmitField(StringBuilder builder, CrudFieldModel field, bool validationAttributes)
    {
        if (validationAttributes && field.IsRequiredString)
        {
            Line(builder, NestedIndent + "[Required]");
            Line(builder, NestedIndent + "[StringLength(255)]");
        }

        Line(builder, NestedIndent + "public " + field.TypeDisplay + " " + field.Name + " { get; set; }");
        Line(builder, string.Empty);
    }
}
