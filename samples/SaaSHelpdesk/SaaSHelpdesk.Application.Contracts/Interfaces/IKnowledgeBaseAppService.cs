using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IKnowledgeBaseAppService
{
    Task<KnowledgeBaseArticleDto> PublishAsync(Guid id);
    Task<KnowledgeBaseArticleDto> UnpublishAsync(Guid id);
    Task<KnowledgeBaseArticleDto> IncrementViewCountAsync(Guid id);
    Task<List<KnowledgeBaseArticleDto>> SearchAsync(string keyword);
    Task<List<KnowledgeBaseArticleDto>> GetPopularAsync(int count = 10);
}
