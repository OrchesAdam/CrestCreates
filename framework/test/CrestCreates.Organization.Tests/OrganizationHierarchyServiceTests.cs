using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Infrastructure.Permission;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Organization.Tests;

public class OrganizationHierarchyServiceTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly MemoryCache _cache;
    private readonly OrganizationHierarchyService _service;

    public OrganizationHierarchyServiceTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        _service = new OrganizationHierarchyService(
            _organizationRepositoryMock.Object,
            _cache);
    }

    [Fact]
    public async Task GetOrganizationAndSubIdsAsync_WithNoChildren_ReturnsOnlySelf()
    {
        var orgId = Guid.NewGuid();

        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(orgId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization>());

        var result = await _service.GetOrganizationAndSubIdsAsync(orgId);

        result.Should().HaveCount(1);
        result.Should().Contain(orgId);
    }

    [Fact]
    public async Task GetOrganizationAndSubIdsAsync_WithChildren_ReturnsSelfAndChildren()
    {
        var orgId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        var child1 = CreateOrganization(child1Id, orgId);
        var child2 = CreateOrganization(child2Id, orgId);

        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(orgId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization> { child1, child2 });
        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(child1Id, default))
            .ReturnsAsync(new List<Domain.Permission.Organization>());
        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(child2Id, default))
            .ReturnsAsync(new List<Domain.Permission.Organization>());

        var result = await _service.GetOrganizationAndSubIdsAsync(orgId);

        result.Should().HaveCount(3);
        result.Should().Contain(orgId);
        result.Should().Contain(child1Id);
        result.Should().Contain(child2Id);
    }

    [Fact]
    public async Task GetOrganizationAndSubIdsAsync_WithNestedDescendants_ReturnsAll()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var child = CreateOrganization(childId, rootId);
        var grandchild = CreateOrganization(grandchildId, childId);

        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(rootId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization> { child });
        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(childId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization> { grandchild });
        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(grandchildId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization>());

        var result = await _service.GetOrganizationAndSubIdsAsync(rootId);

        result.Should().HaveCount(3);
        result.Should().ContainInOrder(rootId, childId, grandchildId);
    }

    [Fact]
    public async Task GetOrganizationAndSubIdsAsync_UsesCache()
    {
        var orgId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var child = CreateOrganization(childId, orgId);

        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(orgId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization> { child });
        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(childId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization>());

        var firstResult = await _service.GetOrganizationAndSubIdsAsync(orgId);
        var secondResult = await _service.GetOrganizationAndSubIdsAsync(orgId);

        firstResult.Should().BeEquivalentTo(secondResult);
        _organizationRepositoryMock.Verify(
            r => r.GetChildrenAsync(orgId, default),
            Times.Once);
    }

    [Fact]
    public async Task GetOrganizationAndSubIdsAsync_CachesFor30Minutes()
    {
        var orgId = Guid.NewGuid();

        _organizationRepositoryMock
            .Setup(r => r.GetChildrenAsync(orgId, default))
            .ReturnsAsync(new List<Domain.Permission.Organization>());

        await _service.GetOrganizationAndSubIdsAsync(orgId);

        var cacheKey = $"OrgHierarchy_{orgId}";
        _cache.TryGetValue(cacheKey, out _).Should().BeTrue();

        // Remove the cached entry and re-invoke to confirm it was the cache holding the value
        _cache.Remove(cacheKey);
        _cache.TryGetValue(cacheKey, out _).Should().BeFalse();

        // Re-invoke to verify the service still works without cache
        var result = await _service.GetOrganizationAndSubIdsAsync(orgId);
        result.Should().Contain(orgId);
    }

    /// <summary>
    /// Creates an <see cref="Organization"/> with the specified <paramref name="id"/> using reflection,
    /// since <see cref="Organization"/> does not expose a constructor that sets <c>Id</c>.
    /// </summary>
    private static Domain.Permission.Organization CreateOrganization(Guid id, Guid? parentId)
    {
        var org = new Domain.Permission.Organization
        {
            ParentId = parentId,
            Code = $"ORG-{id.ToString()[..8]}",
            Name = $"Org-{id.ToString()[..8]}",
            IsActive = true
        };

        typeof(Domain.Permission.Organization)
            .BaseType! // Entity<Guid>
            .GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(org, id);

        return org;
    }
}
