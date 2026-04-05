using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Infrastructure.DataFilter
{
    public class DataPermissionFilter : IDataPermissionFilter
    {
        private readonly ICurrentUser _currentUser;
        private readonly DataFilterState _dataFilterState;

        public DataPermissionFilter(ICurrentUser currentUser, DataFilterState dataFilterState)
        {
            _currentUser = currentUser;
            _dataFilterState = dataFilterState;
        }

        public Task<IQueryable<TEntity>> ApplyFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (!_currentUser.IsAuthenticated)
            {
                return Task.FromResult(query);
            }

            var dataScope = (DataScope)_currentUser.DataScopeValue;
            query = ApplyTenantFilterAsync<TEntity>(query).Result;

            switch (dataScope)
            {
                case DataScope.Self:
                    query = ApplySelfFilter(query);
                    break;
                case DataScope.Organization:
                    query = ApplyOrganizationFilter(query);
                    break;
                case DataScope.OrganizationAndSub:
                    query = ApplyOrganizationAndSubFilter(query);
                    break;
                case DataScope.Tenant:
                case DataScope.All:
                default:
                    break;
            }

            return Task.FromResult(query);
        }

        public Task<IQueryable<TEntity>> ApplyTenantFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (!_currentUser.IsAuthenticated || string.IsNullOrEmpty(_currentUser.TenantId))
            {
                return Task.FromResult(query);
            }

            if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)))
            {
                var parameter = Expression.Parameter(typeof(TEntity), "e");
                var tenantIdProperty = Expression.Property(parameter, nameof(IMustHaveTenant.TenantId));
                var tenantIdValue = Expression.Constant(_currentUser.TenantId);
                var equality = Expression.Equal(tenantIdProperty, tenantIdValue);
                var lambda = Expression.Lambda<Func<TEntity, bool>>(equality, parameter);
                query = query.Where(lambda);
            }

            return Task.FromResult(query);
        }

        public Task<IQueryable<TEntity>> ApplyOrganizationFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.OrganizationId.HasValue)
            {
                return Task.FromResult(query);
            }

            if (typeof(IMayHaveOrganization).IsAssignableFrom(typeof(TEntity)))
            {
                var parameter = Expression.Parameter(typeof(TEntity), "e");
                var orgIdProperty = Expression.Property(parameter, nameof(IMayHaveOrganization.OrganizationId));
                var orgIdValue = Expression.Constant(_currentUser.OrganizationId, typeof(Guid?));
                var equality = Expression.Equal(orgIdProperty, orgIdValue);
                var lambda = Expression.Lambda<Func<TEntity, bool>>(equality, parameter);
                query = query.Where(lambda);
            }

            return Task.FromResult(query);
        }

        private IQueryable<TEntity> ApplySelfFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (typeof(IHasCreator).IsAssignableFrom(typeof(TEntity)) && Guid.TryParse(_currentUser.Id, out var userId))
            {
                var parameter = Expression.Parameter(typeof(TEntity), "e");
                var creatorIdProperty = Expression.Property(parameter, nameof(IHasCreator.CreatorId));
                var creatorIdValue = Expression.Constant(userId, typeof(Guid?));
                var equality = Expression.Equal(creatorIdProperty, creatorIdValue);
                var lambda = Expression.Lambda<Func<TEntity, bool>>(equality, parameter);
                query = query.Where(lambda);
            }

            return query;
        }

        private IQueryable<TEntity> ApplyOrganizationFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (!_currentUser.OrganizationId.HasValue)
            {
                return query;
            }

            if (typeof(IMayHaveOrganization).IsAssignableFrom(typeof(TEntity)))
            {
                var parameter = Expression.Parameter(typeof(TEntity), "e");
                var orgIdProperty = Expression.Property(parameter, nameof(IMayHaveOrganization.OrganizationId));
                var orgIdValue = Expression.Constant(_currentUser.OrganizationId, typeof(Guid?));
                var equality = Expression.Equal(orgIdProperty, orgIdValue);
                var lambda = Expression.Lambda<Func<TEntity, bool>>(equality, parameter);
                query = query.Where(lambda);
            }

            return query;
        }

        private IQueryable<TEntity> ApplyOrganizationAndSubFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (_currentUser.OrganizationIds == null || _currentUser.OrganizationIds.Count == 0)
            {
                return query;
            }

            if (typeof(IMayHaveOrganization).IsAssignableFrom(typeof(TEntity)))
            {
                var parameter = Expression.Parameter(typeof(TEntity), "e");
                var orgIdProperty = Expression.Property(parameter, nameof(IMayHaveOrganization.OrganizationId));
                var orgIds = _currentUser.OrganizationIds;
                Expression? combinedExpression = null;

                foreach (var orgId in orgIds)
                {
                    var orgIdValue = Expression.Constant(orgId, typeof(Guid?));
                    var equality = Expression.Equal(orgIdProperty, orgIdValue);
                    combinedExpression = combinedExpression == null 
                        ? equality 
                        : Expression.OrElse(combinedExpression, equality);
                }

                if (combinedExpression != null)
                {
                    var lambda = Expression.Lambda<Func<TEntity, bool>>(combinedExpression, parameter);
                    query = query.Where(lambda);
                }
            }

            return query;
        }
    }
}
