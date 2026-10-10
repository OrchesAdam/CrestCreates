using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 装配期注册状态（单例）。
    /// </summary>
    /// <remarks>
    /// 记录应用显式声明的默认 Provider 与装配模式；重复声明相同值/相同模式幂等，
    /// 冲突值或混用装配模式在注册阶段立即失败（不允许 first/last-wins）。
    /// 由 <see cref="UnitOfWorkServiceCollectionExtensions"/> 维护。
    /// </remarks>
    public sealed class UnitOfWorkRegistrationState
    {
        private readonly object _gate = new();
        private OrmProvider? _explicitDefault;
        private string? _defaultSource;
        private string? _assemblyMode;
        private string? _assemblyModeSource;

        /// <summary>
        /// 应用显式声明的默认 Provider；未声明时为 null
        /// </summary>
        public OrmProvider? ExplicitDefault
        {
            get
            {
                lock (_gate)
                    return _explicitDefault;
            }
        }

        /// <summary>
        /// 声明默认 Provider；相同值幂等，不同值抛出确定性异常
        /// </summary>
        /// <param name="provider">默认 Provider</param>
        /// <param name="source">声明来源（用于诊断信息）</param>
        public void SetExplicitDefault(OrmProvider provider, string source)
        {
            lock (_gate)
            {
                if (_explicitDefault is null)
                {
                    _explicitDefault = provider;
                    _defaultSource = source;
                    return;
                }

                if (_explicitDefault.Value != provider)
                {
                    throw new InvalidOperationException(
                        $"Conflicting default ORM providers: '{_explicitDefault.Value}' was declared by {_defaultSource}, " +
                        $"but {source} declares '{provider}'. Declare a single default provider or pass an explicit provider per call.");
                }
            }
        }

        /// <summary>
        /// 声明装配模式（provider bindings / custom factory）；重复同模式幂等，混用不同模式抛出确定性异常
        /// </summary>
        /// <param name="mode">装配模式</param>
        /// <param name="source">声明来源（用于诊断信息）</param>
        public void SetAssemblyMode(string mode, string source)
        {
            lock (_gate)
            {
                if (_assemblyMode is null)
                {
                    _assemblyMode = mode;
                    _assemblyModeSource = source;
                    return;
                }

                if (!string.Equals(_assemblyMode, mode, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Conflicting unit-of-work registrations: '{_assemblyMode}' was selected by {_assemblyModeSource}, " +
                        $"but {source} selects '{mode}'. Use a single registration path (provider bindings or one custom factory).");
                }
            }
        }
    }
}
