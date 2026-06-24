using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record ReportResourceSnapshot(
    DescriptorReviewReportDto Report,
    Draft Owner);
