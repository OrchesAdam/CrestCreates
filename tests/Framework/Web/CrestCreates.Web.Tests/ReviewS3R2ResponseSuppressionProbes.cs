using System;
using System.Collections.Generic;
using System.Threading;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Web.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests;

public sealed class ReviewS3R2ResponseSuppressionProbes
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReviewR2_aborted_transport_suppresses_every_failure_body(bool requestAborted, bool callerClassification)
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddSingleton(new CrestExceptionLocalizationResources(
                new Dictionary<string, IReadOnlyDictionary<string, string>>()))
            .AddSingleton<ICrestExceptionConverter, DefaultCrestExceptionConverter>()
            .AddSingleton<ICapabilityExceptionClassifier, CrestCapabilityExceptionClassifier>()
            .AddSingleton<ICapabilityEndpointFailurePresenter, CrestCapabilityEndpointFailurePresenter>()
            .BuildServiceProvider();
        using var caller = new CancellationTokenSource();
        if (requestAborted) caller.Cancel();
        var context = new DefaultHttpContext { RequestServices = provider, RequestAborted = caller.Token };
        context.Response.HasStarted.Should().BeFalse();
        var facts = new CapabilityFailureFacts(CapabilityFailureKind.NonCaller,
            "Crest.InternalError", "safe internal error");
        var execution = callerClassification
            ? CapabilityExecutionResult.Cancelled(TimeSpan.Zero)
            : CapabilityExecutionResult.Failure("PIPELINE_ERROR", "third-token OCE", TimeSpan.Zero, failureFacts: facts);
        var wire = provider.GetRequiredService<ICapabilityEndpointFailurePresenter>().Present(execution, context);
        if (requestAborted)
            wire.Should().NotBeAssignableTo<IValueHttpResult>(
                "transport abort suppresses a JSON body even when the cause is classified NonCaller");
        else
            wire.Should().BeAssignableTo<IValueHttpResult>("a live NonCaller failure still returns sanitized JSON");
        if (!callerClassification) execution.FailureFacts!.Kind.Should().Be(CapabilityFailureKind.NonCaller);
    }
}
