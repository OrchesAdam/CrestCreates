using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;

internal sealed record AssetApprovedRelease(
    int FormatVersion,
    string TenantId,
    RuntimeDescriptorPin[] Baseline,
    AssetDescriptorApproval InitialTaskApproval,
    HumanTaskDescriptor InitialTask,
    AssetDescriptorApproval WorkflowApproval,
    WorkflowDescriptor Workflow)
{
    internal const int CurrentFormatVersion = 1;
    internal const string ReceiptSetting = "AssetManagement:ApprovedRelease:Receipt";
    internal const string SigningKeySetting = "AssetManagement:ApprovedRelease:SigningKey";
    private static readonly DescriptorStableHashBuilder HashBuilder = new(new DefaultCanonicalHashComputer());

    internal static IDescriptor[] GetBaseline() =>
    [
        ..AssetDescriptorCatalog.Schemas,
        ..AssetDescriptorCatalog.Capabilities,
        AssetDescriptorCatalog.MaintenanceForm,
        AssetDescriptorCatalog.MaintenanceHumanTask,
        AssetDescriptorCatalog.MaintenanceWorkflow
    ];

    internal static RuntimeDescriptorPin Pin(IVersionedDescriptor descriptor)
    {
        var hashes = HashBuilder.Build(descriptor);
        return new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptor.Version),
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash
        };
    }

    internal static RuntimeDescriptorPin[] PinBaseline(IReadOnlyList<IDescriptor> baseline) => baseline
        .Cast<IVersionedDescriptor>().Select(Pin)
        .OrderBy(pin => pin.Ref.Namespace, StringComparer.Ordinal)
        .ThenBy(pin => pin.Ref.Id, StringComparer.Ordinal)
        .ThenBy(pin => pin.Ref.Version)
        .ToArray();

    internal void Validate(IReadOnlyList<IDescriptor> baseline)
    {
        if (FormatVersion != CurrentFormatVersion || string.IsNullOrWhiteSpace(TenantId))
            throw new InvalidOperationException("The Asset approved release format or tenant is invalid.");
        if (Baseline is null || !Baseline.SequenceEqual(PinBaseline(baseline)))
            throw new InvalidOperationException("The Asset approved release does not match the deployed baseline.");

        ValidateApproval(InitialTaskApproval, InitialTask);
        ValidateApproval(WorkflowApproval, Workflow);
        if (InitialTaskApproval.RequestId == WorkflowApproval.RequestId
            || InitialTaskApproval.Binding.DraftId == WorkflowApproval.Binding.DraftId)
            throw new InvalidOperationException("The Asset approved release requires distinct task and workflow approvals.");

        var baseWorkflow = AssetDescriptorCatalog.MaintenanceWorkflow;
        if (Pin(InitialTask) != Pin(AssetDescriptorCatalog.MaintenanceInitialHumanTask)
            || Workflow.Id != baseWorkflow.Id
            || Workflow.Version <= baseWorkflow.Version
            || Workflow.State != DescriptorState.Active
            || Workflow.Steps is not [var initial, var final]
            || !IsReviewStep(initial, InitialTask, condition: null)
            || !IsReviewStep(final, AssetDescriptorCatalog.MaintenanceHumanTask, WorkflowConditionTokens.PreviousHumanTaskApproved))
        {
            throw new InvalidOperationException("The Asset approved release does not fit the maintenance workflow and HumanTask contracts supported by this host.");
        }
    }

    private void ValidateApproval(AssetDescriptorApproval? approval, IVersionedDescriptor? descriptor)
    {
        if (descriptor is null || approval?.Binding is not { Hashes: { } hashes } binding
            || string.IsNullOrWhiteSpace(approval.RequestId)
            || string.IsNullOrWhiteSpace(binding.DraftId)
            || string.IsNullOrWhiteSpace(binding.ReviewResultId)
            || string.IsNullOrWhiteSpace(binding.PackagePreviewId)
            || string.IsNullOrWhiteSpace(binding.EvidencePreviewId)
            || binding.TenantId != TenantId
            || binding.DraftVersion != descriptor.Version
            || hashes.SourceReviewHash is null || hashes.ReviewManifestHash is null
            || hashes.PackageManifestHash is null || hashes.PackageEvidenceHash is null || hashes.PackageEvidenceEnvelopeHash is null
            || approval.Descriptor != Pin(descriptor)
            || hashes.ContractHash != approval.Descriptor.ContractHash
            || hashes.DefinitionHash != approval.Descriptor.DefinitionHash)
        {
            throw new InvalidOperationException("The Asset approved release descriptor does not match its activation binding.");
        }
    }

    private static bool IsReviewStep(WorkflowStep step, HumanTaskDescriptor task, string? condition) =>
        step is { Target: HumanTaskTarget target } && step.Condition == condition
        && target.HumanTask == new VersionedDescriptorRef<HumanTaskDescriptor>(task.Id, task.Version);

    internal static string Sign(AssetApprovedRelease release, string? configuredKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(release, AssetApprovedReleaseJsonContext.Default.AssetApprovedRelease);
        return $"{Convert.ToBase64String(payload)}.{Convert.ToHexString(ComputeSignature(payload, configuredKey))}";
    }

    internal static AssetApprovedRelease Load(string receipt, string? configuredKey, IReadOnlyList<IDescriptor> baseline)
    {
        AssetApprovedRelease release;
        try
        {
            var separator = receipt.IndexOf('.');
            if (separator <= 0)
                throw new FormatException();

            var payload = Convert.FromBase64String(receipt[..separator]);
            var signature = Convert.FromHexString(receipt[(separator + 1)..]);
            if (!CryptographicOperations.FixedTimeEquals(signature, ComputeSignature(payload, configuredKey)))
                throw new InvalidOperationException("The Asset approved release signature is invalid.");

            release = JsonSerializer.Deserialize(payload, AssetApprovedReleaseJsonContext.Default.AssetApprovedRelease)
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new InvalidOperationException("The Asset approved release receipt is malformed.", exception);
        }

        release.Validate(baseline);
        return release;
    }

    private static byte[] ComputeSignature(byte[] payload, string? configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException($"{SigningKeySetting} is required for Asset approved releases.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{SigningKeySetting} must contain a base64-encoded key.");
        }

        try
        {
            if (key.Length < 32)
                throw new InvalidOperationException($"{SigningKeySetting} must contain at least 32 bytes.");
            return HMACSHA256.HashData(key, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

public sealed record AssetDescriptorApproval(
    string RequestId,
    ActivationBindingSnapshot Binding,
    RuntimeDescriptorPin Descriptor);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = [typeof(AssetApprovedWorkflowTargetJsonConverter)])]
[JsonSerializable(typeof(AssetApprovedRelease))]
[JsonSerializable(typeof(HumanTaskTarget))]
internal sealed partial class AssetApprovedReleaseJsonContext : JsonSerializerContext;

internal sealed class AssetApprovedWorkflowTargetJsonConverter : JsonConverter<InteractionTarget>
{
    public override InteractionTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize(ref reader, AssetApprovedReleaseJsonContext.Default.HumanTaskTarget)
        ?? throw new JsonException("An Asset maintenance workflow requires a HumanTask target.");

    public override void Write(Utf8JsonWriter writer, InteractionTarget value, JsonSerializerOptions options)
    {
        if (value is not HumanTaskTarget target)
            throw new JsonException("An Asset maintenance workflow requires a HumanTask target.");
        JsonSerializer.Serialize(writer, target, AssetApprovedReleaseJsonContext.Default.HumanTaskTarget);
    }
}
