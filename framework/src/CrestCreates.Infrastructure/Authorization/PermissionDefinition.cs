using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Infrastructure.Authorization
{
    /// <summary>
    /// 权限定义
    /// </summary>
    public class PermissionDefinition
    {
        /// <summary>
        /// 权限名称（唯一标识）
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 父权限
        /// </summary>
        public PermissionDefinition Parent { get; set; }

        /// <summary>
        /// 子权限列表
        /// </summary>
        public List<PermissionDefinition> Children { get; }

        /// <summary>
        /// 是否默认启用
        /// </summary>
        public bool IsEnabledByDefault { get; set; }

        /// <summary>
        /// 权限组名称
        /// </summary>
        public string GroupName { get; set; }

        /// <summary>
        /// 自定义属性
        /// </summary>
        public Dictionary<string, object> Properties { get; }

        public PermissionDefinition(
            string name,
            string displayName = null,
            string description = null,
            bool isEnabledByDefault = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DisplayName = displayName ?? name;
            Description = description;
            IsEnabledByDefault = isEnabledByDefault;
            Children = new List<PermissionDefinition>();
            Properties = new Dictionary<string, object>();
        }

        /// <summary>
        /// 添加子权限
        /// </summary>
        public PermissionDefinition AddChild(
            string name,
            string displayName = null,
            string description = null,
            bool isEnabledByDefault = false)
        {
            var child = new PermissionDefinition(name, displayName, description, isEnabledByDefault)
            {
                Parent = this,
                GroupName = this.GroupName
            };

            Children.Add(child);
            return child;
        }

        /// <summary>
        /// 设置属性
        /// </summary>
        public PermissionDefinition WithProperty(string key, object value)
        {
            Properties[key] = value;
            return this;
        }

        /// <summary>
        /// 获取所有子孙权限（递归）
        /// </summary>
        public IEnumerable<PermissionDefinition> GetAllDescendants()
        {
            foreach (var child in Children)
            {
                yield return child;

                foreach (var descendant in child.GetAllDescendants())
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// 权限组定义
    /// </summary>
    public class PermissionGroupDefinition
    {
        /// <summary>
        /// 组名称
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 权限列表
        /// </summary>
        public List<PermissionDefinition> Permissions { get; }

        public PermissionGroupDefinition(string name, string displayName = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DisplayName = displayName ?? name;
            Permissions = new List<PermissionDefinition>();
        }

        /// <summary>
        /// 添加权限
        /// </summary>
        public PermissionDefinition AddPermission(
            string name,
            string displayName = null,
            string description = null,
            bool isEnabledByDefault = false)
        {
            var permission = new PermissionDefinition(name, displayName, description, isEnabledByDefault)
            {
                GroupName = this.Name
            };

            Permissions.Add(permission);
            return permission;
        }

        /// <summary>
        /// 获取所有权限（包括子权限）
        /// </summary>
        public IEnumerable<PermissionDefinition> GetAllPermissions()
        {
            foreach (var permission in Permissions)
            {
                yield return permission;

                foreach (var child in permission.GetAllDescendants())
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// 权限定义提供者接口
    /// </summary>
    public interface IPermissionDefinitionProvider
    {
        void Define(IPermissionDefinitionContext context);
    }

    /// <summary>
    /// 权限定义上下文
    /// </summary>
    public interface IPermissionDefinitionContext
    {
        PermissionGroupDefinition AddGroup(string name, string displayName = null);
        PermissionGroupDefinition GetGroupOrNull(string name);
        void RemoveGroup(string name);
    }

    /// <summary>
    /// 权限定义上下文实现
    /// </summary>
    public class PermissionDefinitionContext : IPermissionDefinitionContext
    {
        private readonly Dictionary<string, PermissionGroupDefinition> _groups;

        public PermissionDefinitionContext()
        {
            _groups = new Dictionary<string, PermissionGroupDefinition>();
        }

        public PermissionGroupDefinition AddGroup(string name, string displayName = null)
        {
            if (_groups.ContainsKey(name))
            {
                throw new InvalidOperationException($"Permission group '{name}' already exists.");
            }

            var group = new PermissionGroupDefinition(name, displayName);
            _groups[name] = group;
            return group;
        }

        public PermissionGroupDefinition GetGroupOrNull(string name)
        {
            return _groups.TryGetValue(name, out var group) ? group : null;
        }

        public void RemoveGroup(string name)
        {
            _groups.Remove(name);
        }

        /// <summary>
        /// 获取所有权限组
        /// </summary>
        public IEnumerable<PermissionGroupDefinition> GetGroups()
        {
            return _groups.Values;
        }

        /// <summary>
        /// 获取所有权限定义
        /// </summary>
        public IEnumerable<PermissionDefinition> GetPermissions()
        {
            foreach (var group in _groups.Values)
            {
                foreach (var permission in group.GetAllPermissions())
                {
                    yield return permission;
                }
            }
        }

        /// <summary>
        /// 根据名称查找权限
        /// </summary>
        public PermissionDefinition GetPermissionOrNull(string name)
        {
            return GetPermissions().FirstOrDefault(p => p.Name == name);
        }
    }

    /// <summary>
    /// 权限定义管理器
    /// </summary>
    public interface IPermissionDefinitionManager
    {
        PermissionDefinition Get(string name);
        PermissionDefinition GetOrNull(string name);
        IEnumerable<PermissionDefinition> GetPermissions();
        IEnumerable<PermissionGroupDefinition> GetGroups();
    }

    /// <summary>
    /// 权限定义管理器实现
    /// </summary>
    public class PermissionDefinitionManager : IPermissionDefinitionManager
    {
        private readonly Lazy<PermissionDefinitionContext> _context;

        public PermissionDefinitionManager(IEnumerable<IPermissionDefinitionProvider> providers)
        {
            _context = new Lazy<PermissionDefinitionContext>(() =>
            {
                var context = new PermissionDefinitionContext();

                foreach (var provider in providers)
                {
                    provider.Define(context);
                }

                return context;
            });
        }

        public PermissionDefinition Get(string name)
        {
            var permission = GetOrNull(name);
            if (permission == null)
            {
                throw new InvalidOperationException($"Permission '{name}' not found.");
            }

            return permission;
        }

        public PermissionDefinition GetOrNull(string name)
        {
            return _context.Value.GetPermissionOrNull(name);
        }

        public IEnumerable<PermissionDefinition> GetPermissions()
        {
            return _context.Value.GetPermissions();
        }

        public IEnumerable<PermissionGroupDefinition> GetGroups()
        {
            return _context.Value.GetGroups();
        }
    }
}
