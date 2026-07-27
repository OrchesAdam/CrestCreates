using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Model;

public sealed class JsonContractRootOrigin
{
    public required JsonContractRootSourceKind SourceKind { get; init; }
    public required string DeclaringSurface { get; init; }
    public string MemberSignature { get; init; } = string.Empty;
    public string DeclarationName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public Location? Location { get; init; }

    public string Identity => string.Join(
        "|",
        SourceKind,
        DeclaringSurface,
        MemberSignature,
        DeclarationName,
        RoleName);

    public bool IsReturn => SourceKind is JsonContractRootSourceKind.InterfaceReturn
        or JsonContractRootSourceKind.AgentToolOutput
        or JsonContractRootSourceKind.McpToolOutput;

    public string ToDisplayString()
    {
        if (SourceKind == JsonContractRootSourceKind.Explicit)
            return $"Explicit:{DeclaringSurface}";

        if (SourceKind is JsonContractRootSourceKind.InterfaceParameter or JsonContractRootSourceKind.InterfaceReturn)
            return $"{SourceKind}:{DeclaringSurface}::{MemberSignature}";

        return $"{SourceKind}:{DeclaringSurface}::{DeclarationName}.{RoleName}";
    }
}
