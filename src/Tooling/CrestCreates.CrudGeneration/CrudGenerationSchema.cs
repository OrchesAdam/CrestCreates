#nullable enable

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Schema identity shared by the CRUD model, the precompile manifest and the legacy analyzer's
/// ownership reader. Kept in its own source file so the legacy analyzer can link exactly this
/// contract without recompiling the public CRUD generation types.
/// </summary>
internal static class CrudGenerationSchema
{
    public const string ModelSchemaVersion = "crud-model-v1";
}
