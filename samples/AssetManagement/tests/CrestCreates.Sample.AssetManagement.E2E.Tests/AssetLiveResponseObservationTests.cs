using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetLiveResponseObservationTests
{
    [Fact]
    public async Task Observation_PreservesBody_AndStoresOnlyBoundedMetadata()
    {
        const string responseBody = """
            {
              "choices": [{
                "finish_reason": "mystery-provider-value",
                "message": {
                  "content": "provider answer must not be retained",
                  "reasoning_content": "provider reasoning must not be retained"
                }
              }],
              "usage": {
                "prompt_tokens": 11,
                "completion_tokens": 7,
                "completion_tokens_details": {
                  "reasoning_tokens": 5
                }
              }
            }
            """;
        var observation = new AssetLiveResponseObservation();
        using var handler = new AssetLiveResponseObservationHandler(observation)
        {
            InnerHandler = new FakeResponseHandler(responseBody)
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://provider.test/v1/chat/completions");
        var bodyAfterObservation = await response.Content.ReadAsStringAsync();
        var metadata = observation.Snapshot();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        bodyAfterObservation.Should().Be(responseBody);
        metadata.HttpStatus.Should().Be(200);
        metadata.ChoicesCount.Should().Be(1);
        metadata.FinishReason.Should().Be("unknown");
        metadata.ContentCharacterCount.Should().Be("provider answer must not be retained".Length);
        metadata.ReasoningContentCharacterCount.Should().Be("provider reasoning must not be retained".Length);
        metadata.PromptTokens.Should().Be(11);
        metadata.CompletionTokens.Should().Be(7);
        metadata.ReasoningTokens.Should().Be(5);
        metadata.ToString().Should().NotContain("provider answer");
        metadata.ToString().Should().NotContain("provider reasoning");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[42]}")]
    public async Task Observation_UnexpectedJsonShapes_PreserveBody_AndDoNotThrow(string responseBody)
    {
        var observation = new AssetLiveResponseObservation();
        using var handler = new AssetLiveResponseObservationHandler(observation)
        {
            InnerHandler = new FakeResponseHandler(responseBody)
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://provider.test/v1/chat/completions");
        var bodyAfterObservation = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        bodyAfterObservation.Should().Be(responseBody);
        observation.Snapshot().HttpStatus.Should().Be(200);
    }

    private sealed class FakeResponseHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
