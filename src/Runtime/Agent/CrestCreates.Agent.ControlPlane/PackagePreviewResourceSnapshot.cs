using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record PackagePreviewResourceSnapshot(
    PackagePreviewEntry Preview,
    Draft Owner,
    DescriptorPackage? Package = null);
