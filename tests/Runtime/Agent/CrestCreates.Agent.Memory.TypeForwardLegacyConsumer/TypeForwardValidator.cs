using System;
using CrestCreates.Agent.Memory.Tools;

namespace TypeForwardLegacyConsumer;

/// <summary>
/// Simulates a consumer compiled against the OLD CrestCreates.Agent.Memory.Tools.Abstractions
/// assembly (before TypeForward migration). This class instantiates forwarded DTOs/enums
/// and invokes members to prove true binary compatibility.
///
/// The output DLL is loaded by TypeForwardBinaryCompatibilityTests via AssemblyLoadContext
/// without recompilation — verifying that pre-compiled consumer binaries resolve
/// TypeForwarded types across assembly boundaries.
/// </summary>
public static class TypeForwardValidator
{
    public static bool ValidateAll()
    {
        try
        {
            ValidateEnumValues();
            ValidateDtoConstruction();
            ValidateEnumConverter();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateEnumValues()
    {
        // AgentMemoryToolOperationStatus
        var status = AgentMemoryToolOperationStatus.Completed;
        if (status != AgentMemoryToolOperationStatus.Completed) throw new InvalidOperationException();

        // AgentMemoryToolKind
        var kind = AgentMemoryToolKind.ProjectFact;
        if (kind != AgentMemoryToolKind.ProjectFact) throw new InvalidOperationException();

        // AgentMemoryToolConfidence
        var confidence = AgentMemoryToolConfidence.High;
        if (confidence != AgentMemoryToolConfidence.High) throw new InvalidOperationException();

        // AgentMemoryToolMemoryStatus
        var memStatus = AgentMemoryToolMemoryStatus.Active;
        if (memStatus != AgentMemoryToolMemoryStatus.Active) throw new InvalidOperationException();

        // AgentMemoryToolSourceKind
        var sourceKind = AgentMemoryToolSourceKind.ConversationTurn;
        if (sourceKind != AgentMemoryToolSourceKind.ConversationTurn) throw new InvalidOperationException();

        // AgentMemoryToolDiagnosticSeverity
        var severity = AgentMemoryToolDiagnosticSeverity.Error;
        if (severity != AgentMemoryToolDiagnosticSeverity.Error) throw new InvalidOperationException();

        // Security enums
        var resourceKind = AgentMemoryResourceKind.Memory;
        if (resourceKind != AgentMemoryResourceKind.Memory) throw new InvalidOperationException();

        var artifactState = AgentMemorySecurityArtifactState.Active;
        if (artifactState != AgentMemorySecurityArtifactState.Active) throw new InvalidOperationException();

        var artifactKind = AgentMemorySecurityArtifactKind.ResourceHandle;
        if (artifactKind != AgentMemorySecurityArtifactKind.ResourceHandle) throw new InvalidOperationException();

        var disposition = PreparedArtifactDisposition.CreatedByBatch;
        if (disposition != PreparedArtifactDisposition.CreatedByBatch) throw new InvalidOperationException();
    }

    private static void ValidateDtoConstruction()
    {
        // BuildAgentMemoryPackInput
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10000,
            MinimumConfidence = AgentMemoryToolConfidence.High
        };
        if (input.MaximumCount != 5) throw new InvalidOperationException();

        // BuildAgentMemoryPackResult
        var result = new BuildAgentMemoryPackResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            Items = [],
            ReturnedCount = 0,
            WasTruncated = false,
            IsAuthoritative = false,
            Diagnostics = []
        };
        if (result.OperationStatus != AgentMemoryToolOperationStatus.Completed) throw new InvalidOperationException();

        // ExpandAgentMemorySourceInput
        var expandInput = new ExpandAgentMemorySourceInput
        {
            GrantId = "test-grant",
            MaximumCharacters = 500
        };
        if (expandInput.GrantId != "test-grant") throw new InvalidOperationException();

        // ExpandAgentMemorySourceResult
        var expandHash = new AgentMemoryToolCanonicalHashDto
        {
            Value = "hash",
            AlgorithmVersion = "1",
            ContractVersion = "1",
            CanonicalShapeVersion = "v1"
        };
        var expandResult = new ExpandAgentMemorySourceResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            SanitizedContent = "test",
            CanonicalContentHash = expandHash,
            WasTruncated = false,
            Diagnostics = []
        };
        if (expandResult.SanitizedContent != "test") throw new InvalidOperationException();

        // AgentMemoryToolBlockDto
        var blockHash = new AgentMemoryToolCanonicalHashDto
        {
            Value = "block-hash",
            AlgorithmVersion = "1",
            ContractVersion = "1",
            CanonicalShapeVersion = "v1"
        };
        var block = new AgentMemoryToolBlockDto
        {
            Content = "block-content",
            CanonicalContentHash = blockHash,
            SourceGrants = []
        };
        if (block.Content != "block-content") throw new InvalidOperationException();

        // AgentMemoryToolItemDto
        var itemHash = new AgentMemoryToolCanonicalHashDto
        {
            Value = "item-hash",
            AlgorithmVersion = "1",
            ContractVersion = "1",
            CanonicalShapeVersion = "v1"
        };
        var item = new AgentMemoryToolItemDto
        {
            MemoryHandle = "handle-1",
            Kind = AgentMemoryToolKind.ProjectFact,
            Content = "item-content",
            CanonicalContentHash = itemHash,
            Confidence = AgentMemoryToolConfidence.High,
            MemoryStatus = AgentMemoryToolMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = [],
            SourceGrants = []
        };
        if (item.MemoryHandle != "handle-1") throw new InvalidOperationException();

        // AgentMemoryToolDiagnosticDto
        var diag = new AgentMemoryToolDiagnosticDto
        {
            Code = "TEST001",
            Severity = AgentMemoryToolDiagnosticSeverity.Warning
        };
        if (diag.Code != "TEST001") throw new InvalidOperationException();

        // AgentMemorySourceGrantDto
        var grant = new AgentMemorySourceGrantDto
        {
            GrantId = "grant-1",
            SourceKind = AgentMemoryToolSourceKind.ConversationTurn,
            ExpiresAt = DateTimeOffset.MaxValue
        };
        if (grant.GrantId != "grant-1") throw new InvalidOperationException();

        // AgentMemoryToolCanonicalHashDto
        var hash = new AgentMemoryToolCanonicalHashDto
        {
            Value = "sha256:abc",
            AlgorithmVersion = "1",
            ContractVersion = "1",
            CanonicalShapeVersion = "v1"
        };
        if (hash.Value != "sha256:abc") throw new InvalidOperationException();
    }

    private static void ValidateEnumConverter()
    {
        // Verify the generic converter base type is accessible
        var converterType = typeof(AgentMemoryToolEnumConverter<>);
        if (converterType == null) throw new InvalidOperationException();
        if (!converterType.IsAbstract) throw new InvalidOperationException();

        // Verify specific converter types are accessible
        var opConverter = new AgentMemoryToolOperationStatusJsonConverter();
        if (opConverter == null) throw new InvalidOperationException();

        var kindConverter = new AgentMemoryToolKindJsonConverter();
        if (kindConverter == null) throw new InvalidOperationException();
    }
}
