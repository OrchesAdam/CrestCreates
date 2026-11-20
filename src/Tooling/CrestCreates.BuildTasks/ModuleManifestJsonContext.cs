using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrestCreates.BuildTasks;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<CollectModuleManifests.ModuleManifest>), TypeInfoPropertyName = "ModuleManifests")]
internal partial class ModuleManifestJsonContext : JsonSerializerContext;
