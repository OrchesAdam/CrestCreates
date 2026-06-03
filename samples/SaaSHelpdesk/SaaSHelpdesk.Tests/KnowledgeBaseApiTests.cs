using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using SaaSHelpdesk.Tests.Fixtures;
using Xunit;

namespace SaaSHelpdesk.Tests;

public class KnowledgeBaseApiTests : BaseTest
{
    public KnowledgeBaseApiTests(HelpdeskWebApplicationFactory factory) : base(factory) { }

    // ── Response models ──────────────────────────────────────────────

    private sealed class ArticleDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public Guid? CategoryId { get; set; }
        public bool IsPublished { get; set; }
        public DateTime? PublishedAt { get; set; }
        public int ViewCount { get; set; }
        public string? Tags { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime? LastModificationTime { get; set; }
    }

    private sealed class VoidResponse
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<ArticleDto> CreateArticleAsync(
        HttpClient client,
        string? title = null,
        string? content = null,
        string? tags = null)
    {
        var payload = new
        {
            Title = title ?? $"Test {Guid.NewGuid():N}"[..50],
            Content = content ?? "This is test article content that is sufficiently long for validation purposes and meets the minimum length requirement.",
            CategoryId = (Guid?)null,
            Tags = tags
        };
        var response = await PostAsync(client, "/api/knowledge-base-article", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Create failed: {await response.Content.ReadAsStringAsync()}");
        var result = await ReadApiResponseAsync<ArticleDto>(response);
        return result.Data!;
    }

    private async Task PublishArticleAsync(HttpClient client, Guid articleId)
    {
        var response = await PostAsync(client, $"/api/knowledge-base-article/{articleId}/publish", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Publish failed: {await response.Content.ReadAsStringAsync()}");
    }

    // ── 1. CreateAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsCreatedArticle()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var article = await CreateArticleAsync(
            client,
            "Getting Started Guide",
            "This is a comprehensive guide to get started with the platform and covers all basic features.");

        article.Id.Should().NotBe(Guid.Empty);
        article.Title.Should().Be("Getting Started Guide");
        article.Content.Should()
            .Be("This is a comprehensive guide to get started with the platform and covers all basic features.");
        article.IsPublished.Should().BeFalse();
        article.ViewCount.Should().Be(0);
    }

    // ── 2. GetByIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingArticle_ReturnsArticle()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateArticleAsync(client, "Find Me");

        var response = await GetAsync(client, $"/api/knowledge-base-article/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<ArticleDto>(response);
        result.Data!.Id.Should().Be(created.Id);
        result.Data.Title.Should().Be("Find Me");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingArticle_ReturnsNotFound()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, $"/api/knowledge-base-article/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("404");
        content.Should().Contain("资源不存在");
    }

    // ── 3. GetListAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_ReturnsPagedResult()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        await CreateArticleAsync(client, "Article One");
        await CreateArticleAsync(client, "Article Two");

        var response = await GetAsync(client, "/api/knowledge-base-article?pageIndex=0&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<PagedResultResponse<ArticleDto>>(response);
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.TotalCount.Should().BeGreaterThanOrEqualTo(2);
        result.Data.PageIndex.Should().Be(0);
        result.Data.PageSize.Should().Be(10);
    }

    // ── 4. UpdateAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ExistingArticle_UpdatesTitleAndPersists()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateArticleAsync(client, "Old Title");

        var updatePayload = new
        {
            Title = "New Title",
            Content = "Updated content that is long enough for validation purposes and meets all requirements.",
            CategoryId = (Guid?)null,
            Tags = (string?)"updated"
        };
        var response = await PutAsync(client, $"/api/knowledge-base-article/{created.Id}", updatePayload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<ArticleDto>(response);
        result.Data!.Title.Should().Be("New Title");
        result.Data.Content.Should()
            .Be("Updated content that is long enough for validation purposes and meets all requirements.");
        result.Data.Tags.Should().Be("updated");

        // Verify persistence via GET
        var getResponse = await GetAsync(client, $"/api/knowledge-base-article/{created.Id}");
        var fetched = await ReadApiResponseAsync<ArticleDto>(getResponse);
        fetched.Data!.Title.Should().Be("New Title");
    }

    // ── 5. DeleteAsync ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingArticle_ReturnsOk()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateArticleAsync(client);

        var response = await DeleteAsync(client, $"/api/knowledge-base-article/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var voidResponse = await ReadJsonAsync<VoidResponse>(response);
        voidResponse.Code.Should().Be(200);
        voidResponse.Message.Should().Be("操作成功");
    }

    // ── 6. PublishAsync ──────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_SetsIsPublishedTrue()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateArticleAsync(client);

        var response = await PostAsync(client, $"/api/knowledge-base-article/{created.Id}/publish", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<ArticleDto>(response);
        result.Data!.IsPublished.Should().BeTrue();
        result.Data.PublishedAt.Should().NotBeNull();

        // Verify persistence
        var getResponse = await GetAsync(client, $"/api/knowledge-base-article/{created.Id}");
        var fetched = await ReadApiResponseAsync<ArticleDto>(getResponse);
        fetched.Data!.IsPublished.Should().BeTrue();
    }

    // ── 7. UnpublishAsync ────────────────────────────────────────────

    [Fact]
    public async Task UnpublishAsync_SetsIsPublishedFalse()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateArticleAsync(client);

        // Publish first via API
        await PublishArticleAsync(client, created.Id);

        // Unpublish
        var response = await PostAsync(client, $"/api/knowledge-base-article/{created.Id}/unpublish", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<ArticleDto>(response);
        result.Data!.IsPublished.Should().BeFalse();
        result.Data.PublishedAt.Should().BeNull();

        // Verify persistence
        var getResponse = await GetAsync(client, $"/api/knowledge-base-article/{created.Id}");
        var fetched = await ReadApiResponseAsync<ArticleDto>(getResponse);
        fetched.Data!.IsPublished.Should().BeFalse();
    }

    // ── 8. IncrementViewCountAsync ───────────────────────────────────

    [Fact]
    public async Task IncrementViewCountAsync_IncrementsViewCount()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateArticleAsync(client);

        var response = await PostAsync(
            client, $"/api/knowledge-base-article/{created.Id}/increment-view-count", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<ArticleDto>(response);
        result.Data!.ViewCount.Should().Be(1);

        // Second increment
        var response2 = await PostAsync(
            client, $"/api/knowledge-base-article/{created.Id}/increment-view-count", new { });
        var result2 = await ReadApiResponseAsync<ArticleDto>(response2);
        result2.Data!.ViewCount.Should().Be(2);
    }

    // ── 9. SearchAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_WithMatchingKeyword_ReturnsArticles()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var article = await CreateArticleAsync(client, "How to Configure the Settings Panel", tags: "configuration");

        // Must be published to appear in search
        await PublishArticleAsync(client, article.Id);

        var response = await GetAsync(client, "/api/knowledge-base-article/search?keyword=configure");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().NotBeEmpty();
        result.Data!.Any(a => a.Id == article.Id).Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_WithNonMatchingKeyword_ReturnsEmptyList()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var article = await CreateArticleAsync(client, "Setup Guide");
        await PublishArticleAsync(client, article.Id);

        var response = await GetAsync(client, "/api/knowledge-base-article/search?keyword=zzzznonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithEmptyKeyword_ReturnsEmptyList()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, "/api/knowledge-base-article/search?keyword=");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithoutKeyword_ReturnsEmptyList()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, "/api/knowledge-base-article/search");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().BeEmpty();
    }

    // ── 10. GetPopularAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetPopularAsync_WithPositiveCount_ReturnsArticles()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        // Create, publish, and add views
        var article = await CreateArticleAsync(client, "Popular Guide");
        await PublishArticleAsync(client, article.Id);
        await PostAsync(client,
            $"/api/knowledge-base-article/{article.Id}/increment-view-count", new { });

        var response = await GetAsync(client, "/api/knowledge-base-article/popular?count=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().NotBeEmpty();
        result.Data!.Any(a => a.Id == article.Id).Should().BeTrue();
    }

    [Fact]
    public async Task GetPopularAsync_WithZeroCount_ReturnsEmptyList()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, "/api/knowledge-base-article/popular?count=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPopularAsync_WithNegativeCount_ReturnsEmptyList()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, "/api/knowledge-base-article/popular?count=-1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadApiResponseAsync<List<ArticleDto>>(response);
        result.Data.Should().BeEmpty();
    }
}
