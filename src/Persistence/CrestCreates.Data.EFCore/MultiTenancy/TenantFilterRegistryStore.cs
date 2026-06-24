using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// 租户过滤器配置的运行时注册中心
    /// Source Generator 通过 [ModuleInitializer] 将生成的 ApplyAll 委托注册到此存储
    /// </summary>
    public static class TenantFilterRegistryStore
    {
        private static readonly ConcurrentDictionary<string, ApplyAllDelegate> ApplyAllDelegates = new(StringComparer.Ordinal);

        public delegate void ApplyAllDelegate(ModelBuilder modelBuilder, ICurrentTenant currentTenant);

        /// <summary>
        /// 注册编译时生成的 ApplyAll 实现
        /// </summary>
        public static void Register(ApplyAllDelegate applyAll)
        {
            ArgumentNullException.ThrowIfNull(applyAll);
            ApplyAllDelegates.TryAdd(applyAll.Method.DeclaringType?.AssemblyQualifiedName ?? applyAll.Method.Name, applyAll);
        }

        /// <summary>
        /// 已注册的 ApplyAll 委托数量
        /// </summary>
        public static int Count => ApplyAllDelegates.Count;

        /// <summary>
        /// 是否存在已注册的编译期生成过滤器
        /// </summary>
        public static bool HasRegistrations => Count > 0;

        /// <summary>
        /// 获取所有已注册的 ApplyAll 委托
        /// </summary>
        public static IReadOnlyCollection<ApplyAllDelegate> GetApplyAllDelegates()
        {
            return ApplyAllDelegates.Values.ToArray();
        }

        internal static void Clear()
        {
            ApplyAllDelegates.Clear();
        }

        public static InvalidOperationException CreateMissingGeneratedFiltersException()
        {
            return new InvalidOperationException(
                "Tenant discriminator 未找到编译期生成的过滤器注册，当前主链只支持生成链。请确认包含多租户实体的 DbContext 项目引用了 CrestCreates.Data.EFCore 且 Source Generator 已运行。");
        }
    }
}
