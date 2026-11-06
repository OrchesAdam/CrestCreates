#nullable enable
using System.Text;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Object-mapping seed artifacts consumed by the compile-time object-mapping generator.
/// </summary>
internal static class CrudMappingEmitter
{
    private const string ObjectMappingNamespace = "CrestCreates.Domain.Shared.ObjectMapping";

    internal static CrudArtifact EmitObjectMappingSeeds(CrudGenerationModel model)
    {
        var attribute = "global::" + ObjectMappingNamespace + ".GenerateObjectMapping";
        var direction = "global::" + ObjectMappingNamespace + ".MapDirection.";

        var builder = StartDocument(model);
        OpenNamespace(builder, model.MappingsNamespace);
        Line(builder, Indent + "[" + attribute + "(typeof(" + model.EntityQualifiedDisplay + "), "
            + "typeof(" + Qualified(model.ReadDtoFullName) + "))]");
        Line(builder, Indent + "[" + attribute + "(typeof(" + Qualified(model.CreateDtoFullName) + "), "
            + "typeof(" + model.EntityQualifiedDisplay + "), Direction = " + direction + "Create)]");
        Line(builder, Indent + "[" + attribute + "(typeof(" + Qualified(model.UpdateDtoFullName) + "), "
            + "typeof(" + model.EntityQualifiedDisplay + "), Direction = " + direction + "Apply)]");
        Line(builder, Indent + "public static partial class " + model.MappingClassName);
        Line(builder, Indent + "{");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.MappingClassName + ".g.cs", builder);
    }
}
