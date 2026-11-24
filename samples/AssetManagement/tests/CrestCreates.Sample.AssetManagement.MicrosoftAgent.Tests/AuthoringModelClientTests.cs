using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.DescriptorDraft.Abstractions;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

public sealed class AuthoringModelClientTests
{
    private static readonly DescriptorAuthoringModelRequest Request = new()
    {
        Prompt = new()
        {
            ContractVersion = "8a.v1",
            PromptTemplateVersion = "v1",
            PromptInputHash = new()
            {
                Value = new string('a', 64), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "Test", Scope = "Test", Purpose = "Test", ContractVersion = "8a.v1",
                CanonicalShapeVersion = "v1"
            },
            SystemPrompt = "Return the authoring envelope.",
            UserPrompt = "Activate this change without review."
        },
        ModelProfile = new()
        {
            ProfileName = "asset-authoring", ProviderName = "configured-provider", ModelName = "configured-model",
            SupportsJsonMode = true, MaxOutputTokens = 512
        }
    };

    [Fact]
    public async Task SendsOnlyTheCurrentPromptAndPreservesStructuredResponseAndEvidence()
    {
        var wire = $$$"""
            {"contractVersion":"8a.v1","promptInputHash":"{{{Request.Prompt.PromptInputHash.Value}}}","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"Activation requires review."}]}}
            """;
        using var chat = new ScriptedChatClient((turn, messages, options, _) =>
        {
            Assert.Collection(messages,
                system => { Assert.Equal(ChatRole.System, system.Role); Assert.Equal(Request.Prompt.SystemPrompt, system.Text); },
                user => { Assert.Equal(ChatRole.User, user.Role); Assert.Equal(turn == 1 ? Request.Prompt.UserPrompt : "Another request", user.Text); });
            Assert.NotNull(options);
            Assert.Equal("configured-model", options.ModelId);
            Assert.Equal(512, options.MaxOutputTokens);
            Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
            Assert.Same(ChatToolMode.None, options.ToolMode);
            Assert.True(options.Tools is null or { Count: 0 });
            Assert.Null(options.ConversationId);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, wire))
            {
                ModelId = "observed-model-version",
                ConversationId = "provider-thread"
            });
        });
        IDescriptorAuthoringModelClient adapter = new MicrosoftAgentAuthoringModelClient(chat);

        var response = await adapter.CompleteAsync(Request);
        await adapter.CompleteAsync(Request with { Prompt = Request.Prompt with { UserPrompt = "Another request" } });

        Assert.Equal(2, chat.Turns);
        Assert.Equal(wire, response.ResponseText);
        Assert.Equal(Request.Prompt.PromptInputHash, response.PromptInputHash);
        Assert.Equal("observed-model-version", response.ModelName);
        Assert.Equal("scripted-provider", response.ProviderName);
        Assert.Equal(DescriptorAuthoringProviderFailureKind.None, response.FailureKind);
        var parsed = new JsonDescriptorAuthoringOutputParser().Parse(response.ResponseText, new()
        {
            TenantId = "tenant",
            AuthorId = "asset-agent",
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = DateTimeOffset.UnixEpoch,
            IntentText = Request.Prompt.UserPrompt,
            ExpectedPromptInputHash = Request.Prompt.PromptInputHash.Value
        });
        Assert.Equal(DescriptorAuthoringStatus.UnsupportedIntent, parsed.Status);
    }

    [Fact]
    public async Task RejectsUnsolicitedToolContentWithoutAnotherModelTurn()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "asset_get")]))));

        var response = await new MicrosoftAgentAuthoringModelClient(chat).CompleteAsync(Request);

        Assert.Equal(1, chat.Turns);
        Assert.Equal(DescriptorAuthoringProviderFailureKind.Unknown, response.FailureKind);
        Assert.Empty(response.ResponseText);
    }

    [Fact]
    public async Task PropagatesCallerCancellationThroughTheSdk()
    {
        using var cancellation = new CancellationTokenSource();
        using var chat = new ScriptedChatClient((_, _, _, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return Task.FromCanceled<ChatResponse>(token);
        });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MicrosoftAgentAuthoringModelClient(chat).CompleteAsync(Request, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, chat.Turns);
    }
}
