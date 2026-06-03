using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IKnowledgeBaseAppService
{
    Task<KnowledgeBaseArticleDto> CreateAsync(CreateKnowledgeBaseArticleDto input, CancellationToken cancellationToken = default);
    Task<KnowledgeBaseArticleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeBaseArticleDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<KnowledgeBaseArticleDto> UpdateAsync(Guid id, UpdateKnowledgeBaseArticleDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
    Task<KnowledgeBaseArticleDto> PublishAsync(Guid id);
    Task<KnowledgeBaseArticleDto> UnpublishAsync(Guid id);
    Task<KnowledgeBaseArticleDto> IncrementViewCountAsync(Guid id);
    Task<List<KnowledgeBaseArticleDto>> SearchAsync(string keyword);
    Task<List<KnowledgeBaseArticleDto>> GetPopularAsync(int count = 10);
}
