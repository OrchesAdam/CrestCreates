#nullable enable
using System.Collections.Generic;
using System.Text;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Service contract artifact: the generated CRUD interface over the shared five-method contract.
/// </summary>
internal static class CrudServiceContractEmitter
{
    private static readonly IReadOnlyList<string> ServiceContractUsings =
    [
        "using System;",
        "using CrestCreates.Application.Contracts.Interfaces;",
    ];

    internal static CrudArtifact EmitServiceContract(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.InterfaceNamespace, ServiceContractUsings);
        Line(builder, Indent + "public partial interface " + model.ServiceInterfaceName
            + " : ICrudAppService<"
            + model.IdTypeDisplay + ", "
            + Qualified(model.ReadDtoFullName) + ", "
            + Qualified(model.CreateDtoFullName) + ", "
            + Qualified(model.UpdateDtoFullName) + ", "
            + Qualified(model.ListRequestFullName) + ">");
        Line(builder, Indent + "{");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.ServiceInterfaceName + ".g.cs", builder);
    }
}
