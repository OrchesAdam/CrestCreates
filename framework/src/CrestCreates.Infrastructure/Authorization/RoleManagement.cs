using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;

namespace CrestCreates.Infrastructure.Authorization
{
    /// <summary>
    /// 角色实体
    /// </summary>
    public class Role : FullyAuditedEntity<Guid>
    {
        /// <summary>
        /// 角色名称（唯一）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 标准化名称（用于查询）
        /// </summary>
        public string NormalizedName { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否默认角色
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// 是否系统内置角色（不可删除）
        /// </summary>
        public bool IsStatic { get; set; }

        /// <summary>
        /// 是否公开角色
        /// </summary>
        public bool IsPublic { get; set; }

        protected Role()
        {
        }

        public Role(Guid id, string name, string displayName = null)
        {
            Id = id;
            Name = name;
            NormalizedName = name.ToUpperInvariant();
            DisplayName = displayName ?? name;
        }
    }

    /// <summary>
    /// 用户角色关联
    /// </summary>
    public class UserRole : Entity<Guid>
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        public Guid RoleId { get; set; }

        /// <summary>
        /// 租户ID
        /// </summary>
        public string TenantId { get; set; }

        protected UserRole()
        {
        }

        public UserRole(Guid id, string userId, Guid roleId, string tenantId = null)
        {
            Id = id;
            UserId = userId;
            RoleId = roleId;
            TenantId = tenantId;
        }
    }

    /// <summary>
    /// 角色仓储接口
    /// </summary>
    public interface IRoleRepository
    {
        Task<Role> FindByIdAsync(Guid id);
        Task<Role> FindByNameAsync(string name);
        Task<List<Role>> GetListAsync();
        Task<List<Role>> GetDefaultRolesAsync();
        Task<Role> InsertAsync(Role role);
        Task<Role> UpdateAsync(Role role);
        Task DeleteAsync(Guid id);
    }

    /// <summary>
    /// 用户角色仓储接口
    /// </summary>
    public interface IUserRoleRepository
    {
        Task<List<UserRole>> GetListByUserIdAsync(string userId);
        Task<List<UserRole>> GetListByRoleIdAsync(Guid roleId);
        Task<UserRole> InsertAsync(UserRole userRole);
        Task DeleteAsync(Guid id);
        Task DeleteByUserIdAndRoleIdAsync(string userId, Guid roleId);
    }

    /// <summary>
    /// 角色管理服务接口
    /// </summary>
    public interface IRoleManager
    {
        Task<Role> GetAsync(Guid id);
        Task<Role> FindByNameAsync(string name);
        Task<List<Role>> GetListAsync();
        Task<Role> CreateAsync(string name, string displayName = null, string description = null);
        Task<Role> UpdateAsync(Guid id, string displayName = null, string description = null);
        Task DeleteAsync(Guid id);
        Task<bool> RoleExistsAsync(string name);
    }

    /// <summary>
    /// 角色管理服务实现
    /// </summary>
    public class RoleManager : IRoleManager
    {
        private readonly IRoleRepository _roleRepository;

        public RoleManager(IRoleRepository roleRepository)
        {
            _roleRepository = roleRepository;
        }

        public async Task<Role> GetAsync(Guid id)
        {
            var role = await _roleRepository.FindByIdAsync(id);
            if (role == null)
            {
                throw new InvalidOperationException($"Role with id '{id}' not found");
            }
            return role;
        }

        public Task<Role> FindByNameAsync(string name)
        {
            return _roleRepository.FindByNameAsync(name);
        }

        public Task<List<Role>> GetListAsync()
        {
            return _roleRepository.GetListAsync();
        }

        public async Task<Role> CreateAsync(string name, string displayName = null, string description = null)
        {
            // 检查名称是否已存在
            var existingRole = await _roleRepository.FindByNameAsync(name);
            if (existingRole != null)
            {
                throw new InvalidOperationException($"Role with name '{name}' already exists");
            }

            var role = new Role(Guid.NewGuid(), name, displayName)
            {
                Description = description
            };

            return await _roleRepository.InsertAsync(role);
        }

        public async Task<Role> UpdateAsync(Guid id, string displayName = null, string description = null)
        {
            var role = await GetAsync(id);

            if (role.IsStatic)
            {
                throw new InvalidOperationException($"Cannot update static role '{role.Name}'");
            }

            if (!string.IsNullOrEmpty(displayName))
            {
                role.DisplayName = displayName;
            }

            if (!string.IsNullOrEmpty(description))
            {
                role.Description = description;
            }

            return await _roleRepository.UpdateAsync(role);
        }

        public async Task DeleteAsync(Guid id)
        {
            var role = await GetAsync(id);

            if (role.IsStatic)
            {
                throw new InvalidOperationException($"Cannot delete static role '{role.Name}'");
            }

            await _roleRepository.DeleteAsync(id);
        }

        public async Task<bool> RoleExistsAsync(string name)
        {
            var role = await _roleRepository.FindByNameAsync(name);
            return role != null;
        }
    }

    /// <summary>
    /// 用户角色管理服务接口
    /// </summary>
    public interface IUserRoleManager
    {
        Task<List<Role>> GetRolesAsync(string userId);
        Task<List<string>> GetRoleNamesAsync(string userId);
        Task<bool> IsInRoleAsync(string userId, string roleName);
        Task AddToRoleAsync(string userId, string roleName);
        Task AddToRolesAsync(string userId, params string[] roleNames);
        Task RemoveFromRoleAsync(string userId, string roleName);
        Task RemoveFromRolesAsync(string userId, params string[] roleNames);
        Task SetRolesAsync(string userId, params string[] roleNames);
    }

    /// <summary>
    /// 用户角色管理服务实现
    /// </summary>
    public class UserRoleManager : IUserRoleManager
    {
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IRoleRepository _roleRepository;

        public UserRoleManager(
            IUserRoleRepository userRoleRepository,
            IRoleRepository roleRepository)
        {
            _userRoleRepository = userRoleRepository;
            _roleRepository = roleRepository;
        }

        public async Task<List<Role>> GetRolesAsync(string userId)
        {
            var userRoles = await _userRoleRepository.GetListByUserIdAsync(userId);
            var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

            var roles = new List<Role>();
            foreach (var roleId in roleIds)
            {
                var role = await _roleRepository.FindByIdAsync(roleId);
                if (role != null)
                {
                    roles.Add(role);
                }
            }

            return roles;
        }

        public async Task<List<string>> GetRoleNamesAsync(string userId)
        {
            var roles = await GetRolesAsync(userId);
            return roles.Select(r => r.Name).ToList();
        }

        public async Task<bool> IsInRoleAsync(string userId, string roleName)
        {
            var roleNames = await GetRoleNamesAsync(userId);
            return roleNames.Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public async Task AddToRoleAsync(string userId, string roleName)
        {
            var role = await _roleRepository.FindByNameAsync(roleName);
            if (role == null)
            {
                throw new InvalidOperationException($"Role '{roleName}' not found");
            }

            // 检查是否已存在
            var userRoles = await _userRoleRepository.GetListByUserIdAsync(userId);
            if (userRoles.Any(ur => ur.RoleId == role.Id))
            {
                return; // 已存在，不重复添加
            }

            var userRole = new UserRole(Guid.NewGuid(), userId, role.Id);
            await _userRoleRepository.InsertAsync(userRole);
        }

        public async Task AddToRolesAsync(string userId, params string[] roleNames)
        {
            foreach (var roleName in roleNames)
            {
                await AddToRoleAsync(userId, roleName);
            }
        }

        public async Task RemoveFromRoleAsync(string userId, string roleName)
        {
            var role = await _roleRepository.FindByNameAsync(roleName);
            if (role == null)
            {
                return; // 角色不存在，无需删除
            }

            await _userRoleRepository.DeleteByUserIdAndRoleIdAsync(userId, role.Id);
        }

        public async Task RemoveFromRolesAsync(string userId, params string[] roleNames)
        {
            foreach (var roleName in roleNames)
            {
                await RemoveFromRoleAsync(userId, roleName);
            }
        }

        public async Task SetRolesAsync(string userId, params string[] roleNames)
        {
            // 删除现有角色
            var existingUserRoles = await _userRoleRepository.GetListByUserIdAsync(userId);
            foreach (var userRole in existingUserRoles)
            {
                await _userRoleRepository.DeleteAsync(userRole.Id);
            }

            // 添加新角色
            await AddToRolesAsync(userId, roleNames);
        }
    }
}
