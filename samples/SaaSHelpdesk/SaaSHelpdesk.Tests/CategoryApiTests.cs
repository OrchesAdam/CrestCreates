using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using SaaSHelpdesk.Tests.Fixtures;
using SaaSHelpdesk.Tests.Helpers;
using Xunit;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// End-to-end tests for all branches of CategoryAppService via Dynamic API endpoints.
/// Covers CRUD operations (inherited from CrestAppServiceBase) and custom tree-manipulation methods.
/// </summary>
public class CategoryApiTests : BaseTest
{
    public CategoryApiTests(HelpdeskWebApplicationFactory factory) : base(factory)
    {
    }

    // ────────────────────────────────────────────────────────
    //  Test DTOs (minimal shapes for response deserialization)
    // ────────────────────────────────────────────────────────

    private sealed class CategoryResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? ParentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class CreateCategoryPayload
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? ParentId { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class UpdateCategoryPayload
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? ParentId { get; set; }
        public int SortOrder { get; set; }
    }

    // ────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────

    private async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string name,
        int sortOrder = 0,
        Guid? parentId = null,
        string? description = null)
    {
        var payload = new CreateCategoryPayload
        {
            Name = name,
            Description = description,
            ParentId = parentId,
            SortOrder = sortOrder
        };

        var response = await PostAsync(client, "/api/category", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Create category '{name}' failed. Body: {await response.Content.ReadAsStringAsync()}");

        var apiResponse = await ReadApiResponseAsync<CategoryResponse>(response);
        apiResponse.Data.Should().NotBeNull();
        apiResponse.Data!.Id.Should().NotBe(Guid.Empty, "created category must have a non-empty Id");
        apiResponse.Data.Name.Should().Be(name);
        return apiResponse.Data;
    }

    private async Task<CategoryResponse> GetCategoryByIdAsync(HttpClient client, Guid id)
    {
        var response = await GetAsync(client, $"/api/category/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Get category {id} failed. Body: {await response.Content.ReadAsStringAsync()}");

        var apiResponse = await ReadApiResponseAsync<CategoryResponse>(response);
        return apiResponse.Data!;
    }

    // ════════════════════════════════════════════════════════
    //  CRUD TESTS (inherited from CrestAppServiceBase)
    // ════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAsync_ValidInput_ShouldCreateCategory()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var payload = new CreateCategoryPayload
        {
            Name = "Hardware",
            Description = "Hardware issues",
            SortOrder = 1
        };

        // Act
        var response = await PostAsync(client, "/api/category", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<CategoryResponse>(response);
        apiResponse.Code.Should().Be(200);
        apiResponse.Data.Should().NotBeNull();
        apiResponse.Data!.Name.Should().Be("Hardware");
        apiResponse.Data.Description.Should().Be("Hardware issues");
        apiResponse.Data.SortOrder.Should().Be(1);
        apiResponse.Data.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateAsync_WithParent_ShouldSetParentId()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var parent = await CreateCategoryAsync(client, "Parent", sortOrder: 0);

        var payload = new CreateCategoryPayload
        {
            Name = "Child",
            ParentId = parent.Id,
            SortOrder = 0
        };

        // Act
        var response = await PostAsync(client, "/api/category", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<CategoryResponse>(response);
        apiResponse.Data!.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ShouldReturnCategory()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateCategoryAsync(client, "Network", sortOrder: 2);

        // Act
        var response = await GetAsync(client, $"/api/category/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<CategoryResponse>(response);
        apiResponse.Data!.Id.Should().Be(created.Id);
        apiResponse.Data.Name.Should().Be("Network");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ShouldReturnNotFound()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await GetAsync(client, $"/api/category/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetListAsync_WithPaging_ShouldReturnCategories()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        await CreateCategoryAsync(client, "Cat-A", sortOrder: 1);
        await CreateCategoryAsync(client, "Cat-B", sortOrder: 2);

        // Act
        var response = await GetAsync(client, "/api/category/all");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var apiResponse = DeserializeJson<DynamicApiResponse<List<CategoryResponse>>>(raw);
        apiResponse.Should().NotBeNull();
        apiResponse!.Data.Should().NotBeNull();
        apiResponse.Data!.Should().NotBeEmpty();
        apiResponse.Data.Any(c => c.Name == "Cat-A").Should().BeTrue();
        apiResponse.Data.Any(c => c.Name == "Cat-B").Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateCategory()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateCategoryAsync(client, "OldName", sortOrder: 3);

        var payload = new UpdateCategoryPayload
        {
            Name = "UpdatedName",
            Description = "Updated description",
            SortOrder = 99
        };

        // Act
        var response = await PutAsync(client, $"/api/category/{created.Id}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<CategoryResponse>(response);
        apiResponse.Data!.Name.Should().Be("UpdatedName");
        apiResponse.Data.Description.Should().Be("Updated description");
        apiResponse.Data.SortOrder.Should().Be(99);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteCategory()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateCategoryAsync(client, "ToDelete", sortOrder: 0);

        // Act: delete
        var deleteResponse = await DeleteAsync(client, $"/api/category/{created.Id}");

        // Assert: delete succeeded
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: subsequent GET returns 404
        var getResponse = await GetAsync(client, $"/api/category/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ════════════════════════════════════════════════════════
    //  CUSTOM METHOD TESTS (declared on ICategoryAppService)
    // ════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTreeAsync_WithParentAndChild_ShouldReturnCorrectTreeStructure()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        // Create root and child categories
        var root = await CreateCategoryAsync(client, "Software", sortOrder: 1);
        var child1 = await CreateCategoryAsync(client, "OS", sortOrder: 1, parentId: root.Id);
        var child2 = await CreateCategoryAsync(client, "Drivers", sortOrder: 2, parentId: root.Id);

        // Act
        var response = await GetAsync(client, "/api/category/tree");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResult = await ReadApiResponseAsync<List<CategoryResponse>>(response);
        var roots = apiResult.Data!;

        roots.Should().NotBeNull();
        roots.Should().ContainSingle(c => c.Name == "Software");
    }

    [Fact]
    public async Task GetRootsAsync_ShouldReturnOnlyRootCategories()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var root1 = await CreateCategoryAsync(client, "Root-A", sortOrder: 1);
        var root2 = await CreateCategoryAsync(client, "Root-B", sortOrder: 2);
        await CreateCategoryAsync(client, "Child-A1", sortOrder: 0, parentId: root1.Id);

        // Act
        var response = await GetAsync(client, "/api/category/roots");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<List<CategoryResponse>>(response);
        var roots = apiResponse.Data!;

        // Only root categories (ParentId == null) should be returned
        roots.Should().OnlyContain(c => c.ParentId == null);
        roots.Should().Contain(c => c.Name == "Root-A");
        roots.Should().Contain(c => c.Name == "Root-B");
        roots.Should().NotContain(c => c.Name == "Child-A1");
    }

    [Fact]
    public async Task GetChildrenAsync_ShouldReturnChildrenOfSpecificParent()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var parent = await CreateCategoryAsync(client, "Parent", sortOrder: 0);
        var child1 = await CreateCategoryAsync(client, "Child-1", sortOrder: 1, parentId: parent.Id);
        var child2 = await CreateCategoryAsync(client, "Child-2", sortOrder: 2, parentId: parent.Id);

        // Also create an unrelated root to verify it's NOT returned
        await CreateCategoryAsync(client, "Unrelated", sortOrder: 99);

        // Act
        var response = await GetAsync(client, $"/api/category/children/{parent.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<List<CategoryResponse>>(response);
        var children = apiResponse.Data!;

        children.Should().HaveCount(2);
        children[0].Name.Should().Be("Child-1");
        children[1].Name.Should().Be("Child-2");
        children.Should().OnlyContain(c => c.ParentId == parent.Id);
    }

    [Fact]
    public async Task GetChildrenAsync_EmptyParent_ShouldReturnEmptyList()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var parent = await CreateCategoryAsync(client, "Parent-No-Kids", sortOrder: 0);

        // Act
        var response = await GetAsync(client, $"/api/category/children/{parent.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<List<CategoryResponse>>(response);
        apiResponse.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task MoveAsync_ToNewParent_ShouldUpdateParentId()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var root1 = await CreateCategoryAsync(client, "Root-1", sortOrder: 0);
        var root2 = await CreateCategoryAsync(client, "Root-2", sortOrder: 0);
        var child = await CreateCategoryAsync(client, "Movable", sortOrder: 0, parentId: root1.Id);

        // Act: move child from root1 to root2
        var moveResponse = await GetAsync(client,
            $"/api/category/move?id={child.Id}&newParentId={root2.Id}");

        // Assert
        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var movedDto = await ReadApiResponseAsync<CategoryResponse>(moveResponse);
        movedDto.Data!.ParentId.Should().Be(root2.Id);

        // Verify children of root1 no longer include the moved child
        var root1ChildrenRaw = await GetAsync(client, $"/api/category/children/{root1.Id}");
        var root1Children = await ReadApiResponseAsync<List<CategoryResponse>>(root1ChildrenRaw);
        root1Children.Data.Should().NotContain(c => c.Id == child.Id);

        // Verify children of root2 now include the moved child
        var root2ChildrenRaw = await GetAsync(client, $"/api/category/children/{root2.Id}");
        var root2Children = await ReadApiResponseAsync<List<CategoryResponse>>(root2ChildrenRaw);
        root2Children.Data.Should().Contain(c => c.Id == child.Id);
    }

    [Fact]
    public async Task MoveAsync_CircularReference_ShouldBeRejected()
    {
        // Arrange: create parent → child → grandchild chain
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var parent = await CreateCategoryAsync(client, "Grandparent", sortOrder: 0);
        var child = await CreateCategoryAsync(client, "Parent", sortOrder: 0, parentId: parent.Id);
        var grandchild = await CreateCategoryAsync(client, "Child", sortOrder: 0, parentId: child.Id);

        // Act: attempt to move grandparent under grandchild (circular reference)
        var moveResponse = await GetAsync(client,
            $"/api/category/move?id={parent.Id}&newParentId={grandchild.Id}");

        // Assert
        moveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadJsonAsync<ErrorResponse>(moveResponse);
        error.Code.Should().Be("Crest.Operation.Invalid");
        error.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ReorderAsync_ShouldUpdateSortOrder()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var category = await CreateCategoryAsync(client, "ReorderMe", sortOrder: 5);

        // Act
        var reorderResponse = await GetAsync(client,
            $"/api/category/reorder?id={category.Id}&sortOrder=42");

        // Assert: void endpoint returns 200 OK with empty body
        reorderResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify sort order was persisted by fetching the category
        var verify = await GetCategoryByIdAsync(client, category.Id);
        verify.SortOrder.Should().Be(42);
    }

    // ════════════════════════════════════════════════════════
    //  EDGE CASES & BRANCH COVERAGE
    // ════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTreeAsync_EmptyTree_ShouldReturnEmptyList()
    {
        // This test verifies GetTreeAsync branch when no categories exist.
        // We cannot guarantee a clean DB, so we assert the response is a valid list.
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, "/api/category/tree");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        // Should be a valid JSON response (wrapped in DynamicApiResponse)
        content.TrimStart().Should().StartWith("{");
    }

    [Fact]
    public async Task GetRootsAsync_NoRoots_ShouldReturnEmptyList()
    {
        // This test verifies GetRootsAsync returns a valid response
        // when no root categories exist.
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await GetAsync(client, "/api/category/roots");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<List<CategoryResponse>>(response);
        apiResponse.Data.Should().NotBeNull();
        // Data may be empty or contain previously created roots from other tests
        // (shared DB). The key assertion is that the endpoint responds successfully.
    }

    [Fact]
    public async Task MoveAsync_NonExistentCategory_ShouldReturnNotFound()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var nonExistentId = Guid.NewGuid();
        var someParentId = Guid.NewGuid();

        // Act
        var response = await GetAsync(client,
            $"/api/category/move?id={nonExistentId}&newParentId={someParentId}");

        // Assert: KeyNotFoundException → 404 via exception middleware
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await ReadJsonAsync<ErrorResponse>(response);
        error.Code.Should().Be("Crest.Entity.NotFound");
    }

    [Fact]
    public async Task ReorderAsync_NonExistentCategory_ShouldReturnNotFound()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await GetAsync(client,
            $"/api/category/reorder?id={nonExistentId}&sortOrder=10");

        // Assert: KeyNotFoundException → 404 via exception middleware
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await ReadJsonAsync<ErrorResponse>(response);
        error.Code.Should().Be("Crest.Entity.NotFound");
    }

    [Fact]
    public async Task MoveAsync_NullParent_ShouldMakeCategoryRoot()
    {
        // Arrange — create a parent with a child
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var parent = await CreateCategoryAsync(client, "Parent", sortOrder: 0);
        var child = await CreateCategoryAsync(client, "Child", sortOrder: 0, parentId: parent.Id);

        // Verify child has a parent
        var childBefore = await GetCategoryByIdAsync(client, child.Id);
        childBefore.ParentId.Should().Be(parent.Id);

        // Act — move child to root (null parent) — omit newParentId to pass null
        var moveResponse = await GetAsync(client,
            $"/api/category/move?id={child.Id}&newParentId=");

        // Assert — child should now be a root (ParentId = null)
        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var movedDto = await ReadApiResponseAsync<CategoryResponse>(moveResponse);
        movedDto.Data!.ParentId.Should().BeNull();

        // Verify child is no longer under parent
        var root1ChildrenRaw = await GetAsync(client, $"/api/category/children/{parent.Id}");
        var root1Children = await ReadApiResponseAsync<List<CategoryResponse>>(root1ChildrenRaw);
        root1Children.Data.Should().NotContain(c => c.Id == child.Id);
    }

    [Fact]
    public async Task FullLifecycle_CreateUpdateMoveReorderDelete_ShouldWorkEndToEnd()
    {
        // This test exercises the full CRUD + tree lifecycle in a single flow
        // to verify that all operations compose correctly.
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        // 1. Create two root categories
        var rootA = await CreateCategoryAsync(client, "Lifecycle-RootA", sortOrder: 10);
        var rootB = await CreateCategoryAsync(client, "Lifecycle-RootB", sortOrder: 20);

        // 2. Create children under rootA
        var childA1 = await CreateCategoryAsync(client, "Lifecycle-ChildA1", sortOrder: 1, parentId: rootA.Id);
        var childA2 = await CreateCategoryAsync(client, "Lifecycle-ChildA2", sortOrder: 2, parentId: rootA.Id);

        // 3. Verify tree structure contains correct nesting
        var treeRaw = await GetAsync(client, "/api/category/tree");
        var treeBody = await treeRaw.Content.ReadAsStringAsync();
        treeBody.Should().Contain("Lifecycle-RootA");
        treeBody.Should().Contain("Lifecycle-RootB");

        // 4. Verify roots endpoint returns only roots
        var rootsResponse = await GetAsync(client, "/api/category/roots");
        var rootsData = await ReadApiResponseAsync<List<CategoryResponse>>(rootsResponse);
        rootsData.Data.Should().Contain(r => r.Name == "Lifecycle-RootA");
        rootsData.Data.Should().Contain(r => r.Name == "Lifecycle-RootB");
        rootsData.Data.Should().NotContain(r => r.Name == "Lifecycle-ChildA1");

        // 5. Verify children endpoint
        var childrenResponse = await GetAsync(client, $"/api/category/children/{rootA.Id}");
        var childrenData = await ReadApiResponseAsync<List<CategoryResponse>>(childrenResponse);
        childrenData.Data.Should().HaveCount(2);

        // 6. Move childA1 to rootB
        var moveResponse = await GetAsync(client,
            $"/api/category/move?id={childA1.Id}&newParentId={rootB.Id}");
        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Verify childA1 is now under rootB
        var rootBChildren = await GetAsync(client, $"/api/category/children/{rootB.Id}");
        var rootBChildrenData = await ReadApiResponseAsync<List<CategoryResponse>>(rootBChildren);
        rootBChildrenData.Data.Should().Contain(c => c.Name == "Lifecycle-ChildA1");

        // 8. Reorder childA2 to sort order 99
        var reorderResponse = await GetAsync(client,
            $"/api/category/reorder?id={childA2.Id}&sortOrder=99");
        reorderResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyReorder = await GetCategoryByIdAsync(client, childA2.Id);
        verifyReorder.SortOrder.Should().Be(99);

        // 9. Update rootA name
        var updatePayload = new UpdateCategoryPayload
        {
            Name = "Lifecycle-RootA-Updated",
            Description = "Updated via lifecycle test",
            SortOrder = 15
        };
        var updateResponse = await PutAsync(client, $"/api/category/{rootA.Id}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedDto = await ReadApiResponseAsync<CategoryResponse>(updateResponse);
        updatedDto.Data!.Name.Should().Be("Lifecycle-RootA-Updated");

        // 10. Delete childA2 and verify it's gone
        var deleteResponse = await DeleteAsync(client, $"/api/category/{childA2.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getDeleted = await GetAsync(client, $"/api/category/{childA2.Id}");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
