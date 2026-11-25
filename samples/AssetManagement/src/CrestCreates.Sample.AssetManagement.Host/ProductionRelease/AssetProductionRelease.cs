using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public sealed record AssetProductionRelease(
    int FormatVersion,
    string TenantId,
    RuntimeDescriptorPin[] Baseline,
    AssetDescriptorApproval WorkflowApproval,
    WorkflowDescriptor Workflow)
{
    public const int CurrentFormatVersion = 1;
    public const string ReceiptSetting = "AssetManagement:ProductionRelease:Receipt";
    public const string SigningKeySetting = "AssetManagement:ProductionRelease:SigningKey";

    public void Validate(IReadOnlyList<IDescriptor> baseline)
    {
        if (FormatVersion != CurrentFormatVersion || string.IsNullOrWhiteSpace(TenantId) ||
            Baseline is null || !Baseline.SequenceEqual(AssetApprovedRelease.PinBaseline(baseline)))
        {
            throw new InvalidOperationException("The Asset production release format, tenant or baseline is invalid.");
        }

        if (WorkflowApproval?.Binding is not { Hashes: { } hashes } binding ||
            string.IsNullOrWhiteSpace(WorkflowApproval.RequestId) || string.IsNullOrWhiteSpace(binding.DraftId) ||
            string.IsNullOrWhiteSpace(binding.ReviewResultId) || string.IsNullOrWhiteSpace(binding.PackagePreviewId) ||
            string.IsNullOrWhiteSpace(binding.EvidencePreviewId) || binding.TenantId != TenantId ||
            binding.DraftVersion != Workflow.Version || hashes.SourceReviewHash is null || hashes.ReviewManifestHash is null ||
            hashes.PackageManifestHash is null || hashes.PackageEvidenceHash is null || hashes.PackageEvidenceEnvelopeHash is null ||
            WorkflowApproval.Descriptor != AssetApprovedRelease.Pin(Workflow) ||
            hashes.ContractHash != WorkflowApproval.Descriptor.ContractHash ||
            hashes.DefinitionHash != WorkflowApproval.Descriptor.DefinitionHash)
        {
            throw new InvalidOperationException("The Asset production release workflow does not match its activation binding.");
        }

        AssetProductionDescriptors.RequireProductionWorkflow(Workflow);
    }

    public static string Sign(AssetProductionRelease release, string? configuredKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(release, AssetProductionReleaseJsonContext.Default.AssetProductionRelease);
        return $"{Convert.ToBase64String(payload)}.{Convert.ToHexString(ComputeSignature(payload, configuredKey))}";
    }

    public static AssetProductionRelease Load(string receipt, string? configuredKey, IReadOnlyList<IDescriptor> baseline)
    {
        AssetProductionRelease release;
        try
        {
            var separator = receipt.IndexOf('.');
            if (separator <= 0)
                throw new FormatException();

            var payload = Convert.FromBase64String(receipt[..separator]);
            var signature = Convert.FromHexString(receipt[(separator + 1)..]);
            if (!CryptographicOperations.FixedTimeEquals(signature, ComputeSignature(payload, configuredKey)))
                throw new InvalidOperationException("The Asset production release signature is invalid.");

            release = JsonSerializer.Deserialize(payload, AssetProductionReleaseJsonContext.Default.AssetProductionRelease)
                ?? throw new InvalidOperationException("The Asset production release payload is empty.");
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("The Asset production release receipt is invalid.", exception);
        }

        release.Validate(baseline);
        return release;
    }

    private static byte[] ComputeSignature(byte[] payload, string? configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException("AssetManagement:ProductionRelease:SigningKey is required.");
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(configuredKey));
        return hmac.ComputeHash(payload);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = [typeof(AssetApprovedWorkflowTargetJsonConverter)])]
[JsonSerializable(typeof(AssetProductionRelease))]
[JsonSerializable(typeof(HumanTaskTarget))]
internal sealed partial class AssetProductionReleaseJsonContext : JsonSerializerContext;
