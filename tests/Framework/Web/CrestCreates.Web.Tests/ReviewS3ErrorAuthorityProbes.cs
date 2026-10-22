using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Exceptions;
using CrestCreates.DynamicApi;
using CrestCreates.Web.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.Web.Tests;

public sealed class ReviewS3ErrorAuthorityProbes
{
    [Fact]
    public async Task ReviewS3_capability_error_wire_preserves_authoritative_localized_message()
    {
        var resources = new CrestExceptionLocalizationResources(
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [CultureInfo.CurrentCulture.Name] = new Dictionary<string, string>
                { ["Crest.Concurrency.Conflict"] = "localized-authoritative-conflict" }
            });
        using var provider = new ServiceCollection().AddLogging()
            .AddSingleton(resources)
            .AddSingleton<ICrestExceptionConverter, DefaultCrestExceptionConverter>()
            .AddSingleton<ICapabilityExceptionClassifier, CrestCapabilityExceptionClassifier>()
            .AddSingleton<ICapabilityEndpointFailurePresenter, CrestCapabilityEndpointFailurePresenter>()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider, TraceIdentifier = "review-trace" };
        context.Response.Body = new MemoryStream();
        var converter = provider.GetRequiredService<ICrestExceptionConverter>();
        var failure = new CrestConcurrencyException("Ticket", "review-ticket");
        var expected = converter.Convert(context, failure).Response;
        expected.Message.Should().Be("localized-authoritative-conflict");
        var classifier = provider.GetRequiredService<ICapabilityExceptionClassifier>();
        var presenter = provider.GetRequiredService<ICapabilityEndpointFailurePresenter>();
        var result = CapabilityExecutionResult.Failure("PIPELINE_ERROR", "internal", System.TimeSpan.Zero,
            failureFacts: classifier.Classify(failure));
        await presenter.Present(result, context).ExecuteAsync(context);
        context.Response.StatusCode.Should().Be(expected.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("code").GetString().Should().Be(expected.Code);
        json.RootElement.GetProperty("traceId").GetString().Should().Be(expected.TraceId);
        json.RootElement.GetProperty("message").GetString().Should().Be(expected.Message,
            "the capability bridge must preserve the existing converter/localization authority");
    }
}
