using CrestCreates.DescriptorDraft.Abstractions;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record PackagePreviewEntry(
    string DraftId,
    string TenantId,
    DraftPackagePreview Preview);
