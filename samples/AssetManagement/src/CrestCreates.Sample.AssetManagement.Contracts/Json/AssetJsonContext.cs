using System.Text.Json.Serialization;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;

namespace CrestCreates.Sample.AssetManagement.Contracts.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegisterAssetInput))]
[JsonSerializable(typeof(UpdateAssetInput))]
[JsonSerializable(typeof(AssetQueryInput))]
[JsonSerializable(typeof(AssignAssetInput))]
[JsonSerializable(typeof(TransferAssetInput))]
[JsonSerializable(typeof(MaintenanceRequestInput))]
[JsonSerializable(typeof(MaintenanceDecisionInput))]
[JsonSerializable(typeof(AssetIdInput))]
[JsonSerializable(typeof(AssetResult))]
[JsonSerializable(typeof(AssetOperationResult))]
[JsonSerializable(typeof(List<AssetResult>))]
[JsonSerializable(typeof(AssetResult[]))]
[JsonSerializable(typeof(AssetMaintenanceDecisionFact))]
[JsonSerializable(typeof(Mes.TrialObservation))]
[JsonSerializable(typeof(Mes.ProductionReleaseReceipt))]
[JsonSerializable(typeof(AssetProductionLockFact))]
[JsonSerializable(typeof(AssetMaintenanceEvidenceFact))]
[JsonSerializable(typeof(AssetProductionReleaseInput))]
[JsonSerializable(typeof(AssetProductionReleaseFact))]
public sealed partial class AssetJsonContext : JsonSerializerContext;
