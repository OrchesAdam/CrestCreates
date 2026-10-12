using System;
using System.Collections.Generic;
using System.Data;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// Provider 能力声明（装配期由 Provider 包声明，内核在**执行业务前**校验）。
    /// </summary>
    /// <remarks>
    /// 未声明即不支持：显式请求声明外能力时内核确定性拒绝，不静默降级。
    /// 隔离级别按实际数据库/驱动逐项声明（EF 绑定按 ProviderName 探测；SQLite/PG 矩阵见验收记录）。
    /// </remarks>
    public sealed class UnitOfWorkProviderCapabilities
    {
        /// <summary>默认声明：支持事务与 requiresNew 绑定（由绑定单独声明），无显式隔离级别、无终结/丢弃声明。</summary>
        public static UnitOfWorkProviderCapabilities Default { get; } = new();

        /// <summary>本地事务支持。</summary>
        public bool SupportsTransactions { get; init; } = true;

        /// <summary>静态声明的显式隔离级别集合（空 = 未声明任何级别）。</summary>
        public IReadOnlyCollection<IsolationLevel> SupportedIsolationLevels { get; init; } = Array.Empty<IsolationLevel>();

        /// <summary>
        /// 按当前作用域解析受支持的隔离级别（用于按实际数据库/驱动判定，如 EF ProviderName 探测）。
        /// null 表示使用 <see cref="SupportedIsolationLevels"/> 静态声明。
        /// </summary>
        public Func<IServiceProvider, IReadOnlyCollection<IsolationLevel>>? SupportedIsolationLevelsFactory { get; init; }

        /// <summary>未完成退出时能及时终结事务。</summary>
        public bool PromptTerminationOnAbandon { get; init; }

        /// <summary>未完成退出时能丢弃未 flush 的跟踪写入。</summary>
        public bool DiscardUncommittedOnAbandon { get; init; }

        /// <summary>解析当前作用域实际支持的隔离级别。</summary>
        public IReadOnlyCollection<IsolationLevel> ResolveSupportedIsolationLevels(IServiceProvider serviceProvider)
        {
            if (SupportedIsolationLevelsFactory is not null)
            {
                try
                {
                    return SupportedIsolationLevelsFactory(serviceProvider) ?? Array.Empty<IsolationLevel>();
                }
                catch (Exception)
                {
                    // 探测失败按「未声明」处理（fail closed 由内核给出确定性诊断）。
                    return Array.Empty<IsolationLevel>();
                }
            }

            return SupportedIsolationLevels;
        }
    }
}
