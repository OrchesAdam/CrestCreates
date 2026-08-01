using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Samples.DescriptorControlPlane;

internal sealed class CompanyCertificationRuntimeStateContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        builder.Add(
            "crest.sample.company-certification/submit-input/v1",
            SampleSqliteJsonContext.Default.CertificationSubmitInput,
            new HashSet<Type> { typeof(CertificationSubmitInput) });
        builder.Add(
            "crest.sample.company-certification/review-input/v1",
            SampleSqliteJsonContext.Default.CertificationReviewInput,
            new HashSet<Type> { typeof(CertificationReviewInput) });
        builder.Add(
            "crest.sample.company-certification/result/v1",
            SampleSqliteJsonContext.Default.CertificationResult,
            new HashSet<Type> { typeof(CertificationResult) });
    }
}
