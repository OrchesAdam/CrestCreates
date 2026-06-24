using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record PackagePreviewResourceSnapshot(
    PackagePreviewEntry Preview,
    Draft Owner);
