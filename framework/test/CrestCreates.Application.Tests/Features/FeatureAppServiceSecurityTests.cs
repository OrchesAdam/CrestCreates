using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Features;
using CrestPermissionException = CrestCreates.Domain.Exceptions.CrestPermissionException;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Features;

public class FeatureAppServiceSecurityTests
{
    [Fact]
    public async Task TenantUser_ShouldNotManageOtherTenantFeature()
    {
        var fixture = FeatureAppServiceFixture.Create(currentTenantId: "tenant-a", grantPermissions: true);

        var action = async () => await fixture.AppService.SetTenantAsync(
            "Identity.UserCreationEnabled",
            "tenant-b",
            "true");

        await action.Should().ThrowAsync<CrestPermissionException>();
    }

    [Fact]
    public async Task HostWithoutPermission_ShouldNotSetGlobalFeature()
    {
        var fixture = FeatureAppServiceFixture.Create(currentTenantId: null, grantPermissions: false);

        var action = async () => await fixture.AppService.SetGlobalAsync(
            "Identity.UserCreationEnabled",
            "true");

        await action.Should().ThrowAsync<CrestPermissionException>();
    }

    private sealed class FeatureAppServiceFixture
    {
        public FeatureAppService AppService { get; private init; } = null!;

        public static FeatureAppServiceFixture Create(string? currentTenantId, bool grantPermissions)
        {
            var repository = new InMemoryFeatureRepository();
            var definitionManager = new FeatureDefinitionManager(new IFeatureDefinitionProvider[] { new CoreFeatureDefinitionProvider() });
            var cacheService = new CrestCacheService(
                new CrestMemoryCache(new CacheOptions { Prefix = $"FeatureSecurity:{Guid.NewGuid():N}:" }),
                new CrestCacheKeyGenerator());
            var cacheKeyContributor = new FeatureCacheKeyContributor();
            var store = new FeatureStore(repository, cacheService, cacheKeyContributor);
            var resolver = new FeatureValueResolver(definitionManager, store);
            var currentTenant = new Mock<ICurrentTenant>();
            currentTenant.SetupGet(x => x.Id).Returns(currentTenantId ?? string.Empty);
            if (currentTenantId is not null)
            {
                var tenantInfo = new Mock<ITenantInfo>();
                tenantInfo.SetupGet(t => t.Id).Returns(currentTenantId);
                tenantInfo.SetupGet(t => t.Name).Returns(currentTenantId);
                currentTenant.SetupGet(x => x.Tenant).Returns(tenantInfo.Object);
            }

            var permissionChecker = new Mock<IPermissionChecker>();
            permissionChecker
                .Setup(x => x.IsGrantedAsync(It.IsAny<string>()))
                .ReturnsAsync(grantPermissions);

            var appService = new FeatureAppService(
                new FeatureManager(
                    definitionManager,
                    repository,
                    store,
                    new FeatureValueTypeConverter(),
                    new FeatureCacheInvalidator(cacheService, cacheKeyContributor),
                    new FeatureAuditRecorder()),
                new FeatureProvider(definitionManager, resolver, new FeatureValueTypeConverter(), currentTenant.Object),
                resolver,
                currentTenant.Object,
                new FeatureValueAppServiceMapper(),
                permissionChecker.Object);

            return new FeatureAppServiceFixture { AppService = appService };
        }
    }

    private sealed class InMemoryFeatureRepository : IFeatureRepository
    {
        private readonly List<FeatureValue> _features = new();

        public Task<FeatureValue?> FindAsync(
            string name,
            FeatureScope scope,
            string providerKey,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_features.FirstOrDefault(feature =>
                feature.Name == name &&
                feature.Scope == scope &&
                feature.ProviderKey == providerKey &&
                feature.TenantId == tenantId));
        }

        public Task<List<FeatureValue>> GetListByScopeAsync(
            FeatureScope scope,
            string? providerKey = null,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_features
                .Where(feature => feature.Scope == scope)
                .Where(feature => providerKey is null || feature.ProviderKey == providerKey)
                .Where(feature => tenantId is null || feature.TenantId == tenantId)
                .OrderBy(feature => feature.Name)
                .ToList());
        }

        public Task<FeatureValue> InsertAsync(FeatureValue entity, CancellationToken cancellationToken = default)
        {
            _features.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<FeatureValue> UpdateAsync(FeatureValue entity, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entity);
        }

        public Task DeleteAsync(FeatureValue entity, CancellationToken cancellationToken = default)
        {
            _features.Remove(entity);
            return Task.CompletedTask;
        }

        // ICrestRepositoryBase members - not used by these tests
        public IQueryable<FeatureValue> GetQueryable() => throw new NotSupportedException();
        public Task<FeatureValue?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FeatureValue?> GetAsync(Expression<Func<FeatureValue, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, string expectedStamp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<FeatureValue>> GetListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<FeatureValue>> GetListAsync(Expression<Func<FeatureValue, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<FeatureValue>> GetListAsync(Expression<Func<FeatureValue, bool>> predicate, Expression<Func<FeatureValue, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<FeatureValue>> InsertRangeAsync(IEnumerable<FeatureValue> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<FeatureValue>> UpdateRangeAsync(IEnumerable<FeatureValue> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRangeAsync(IEnumerable<FeatureValue> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRangeAsync(Expression<Func<FeatureValue, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<FeatureValue>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<FeatureValue>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<FeatureValue, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<FeatureValue>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<FeatureValue, bool>> predicate, Expression<Func<FeatureValue, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<FeatureValue>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<FeatureValue, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetCountAsync(Expression<Func<FeatureValue, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(Expression<Func<FeatureValue, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
