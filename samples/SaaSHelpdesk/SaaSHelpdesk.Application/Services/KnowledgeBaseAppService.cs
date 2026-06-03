using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class KnowledgeBaseAppService : CrestAppServiceBase<KnowledgeBaseArticle, Guid, KnowledgeBaseArticleDto, CreateKnowledgeBaseArticleDto, UpdateKnowledgeBaseArticleDto>, IKnowledgeBaseAppService
{
    public KnowledgeBaseAppService(
        ICrestRepositoryBase<KnowledgeBaseArticle, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
    }

    protected override KnowledgeBaseArticle MapToEntity(CreateKnowledgeBaseArticleDto dto)
    {
        var article = new KnowledgeBaseArticle(Guid.NewGuid(), dto.Title, dto.Content);
        article.SetTags(dto.Tags);
        article.SetCategory(dto.CategoryId);
        return article;
    }

    protected override KnowledgeBaseArticleDto MapToDto(KnowledgeBaseArticle entity)
    {
        return new KnowledgeBaseArticleDto
        {
            Id = entity.Id,
            Title = entity.Title,
            Content = entity.Content,
            CategoryId = entity.CategoryId,
            IsPublished = entity.IsPublished,
            PublishedAt = entity.PublishedAt,
            ViewCount = entity.ViewCount,
            Tags = entity.Tags,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
        };
    }

    protected override void MapToEntity(UpdateKnowledgeBaseArticleDto dto, KnowledgeBaseArticle entity)
    {
        entity.SetTitle(dto.Title);
        entity.SetContent(dto.Content);
        entity.SetCategory(dto.CategoryId);
        entity.SetTags(dto.Tags);
    }

    public async Task<KnowledgeBaseArticleDto> PublishAsync(Guid id)
    {
        var article = await Repository.GetAsync(id);
        article.Publish();
        await Repository.UpdateAsync(article);
        return MapToDto(article);
    }

    public async Task<KnowledgeBaseArticleDto> UnpublishAsync(Guid id)
    {
        var article = await Repository.GetAsync(id);
        article.Unpublish();
        await Repository.UpdateAsync(article);
        return MapToDto(article);
    }

    public async Task<KnowledgeBaseArticleDto> IncrementViewCountAsync(Guid id)
    {
        var article = await Repository.GetAsync(id);
        article.IncrementViewCount();
        await Repository.UpdateAsync(article);
        return MapToDto(article);
    }

    public async Task<List<KnowledgeBaseArticleDto>> SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<KnowledgeBaseArticleDto>();
        }

        // NOTE: Loads all articles into memory and filters in-app because the
        // repository base does not expose a Where(predicate) overload for
        // string.Contains operations. This is acceptable for small datasets
        // but should be replaced with a database-level query if the article
        // count grows significantly.
        var articles = await Repository.GetListAsync();
        return articles.Where(a => a.IsPublished)
            .Where(a => a.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || (a.Tags != null && a.Tags.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(MapToDto)
            .ToList();
    }

    public async Task<List<KnowledgeBaseArticleDto>> GetPopularAsync(int count = 10)
    {
        if (count <= 0)
        {
            return new List<KnowledgeBaseArticleDto>();
        }

        // In-memory operation: loads all articles then sorts and takes top N.
        // Acceptable for small datasets; replace with database-level query if
        // the article count grows significantly.
        var articles = await Repository.GetListAsync();
        return articles.Where(a => a.IsPublished)
            .OrderByDescending(a => a.ViewCount)
            .Take(count)
            .Select(MapToDto)
            .ToList();
    }
}
