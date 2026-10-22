using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IKnowledgeBaseAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "KnowledgeBase.Create" },
        RequiresValidator = true)]
    Task<KnowledgeBaseArticleDto> CreateAsync(CreateKnowledgeBaseArticleDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "KnowledgeBase.Get" })]
    Task<KnowledgeBaseArticleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "KnowledgeBase.Search" })]
    Task<IReadOnlyList<KnowledgeBaseArticleDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "KnowledgeBase.Update" })]
    Task<KnowledgeBaseArticleDto> UpdateAsync(Guid id, UpdateKnowledgeBaseArticleDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "KnowledgeBase.Delete" },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "publish",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "KnowledgeBase.Search" })]
    Task<KnowledgeBaseArticleDto> PublishAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "unpublish",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "KnowledgeBase.Search" })]
    Task<KnowledgeBaseArticleDto> UnpublishAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "increment-view-count",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "KnowledgeBase.Search" })]
    Task<KnowledgeBaseArticleDto> IncrementViewCountAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "search",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "KnowledgeBase.Search" })]
    Task<List<KnowledgeBaseArticleDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "popular",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "KnowledgeBase.Search" })]
    Task<List<KnowledgeBaseArticleDto>> GetPopularAsync(int? count = 10, CancellationToken cancellationToken = default);
}
