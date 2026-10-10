using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 装配完成的 Provider 绑定索引。
    /// </summary>
    /// <remarks>
    /// 索引在容器构建时一次性从全部 <see cref="UnitOfWorkProviderBinding"/> 生成，
    /// 之后不再被任何作用域内的工厂修改；重复绑定在构建期给出确定性异常（不允许 last-wins）。
    /// </remarks>
    public sealed class UnitOfWorkProviderBindingRegistry
    {
        private readonly IReadOnlyDictionary<OrmProvider, UnitOfWorkProviderBinding> _bindings;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="bindings">装配期收集的全部绑定</param>
        /// <exception cref="InvalidOperationException">同一 Provider 出现多个绑定时抛出</exception>
        public UnitOfWorkProviderBindingRegistry(IEnumerable<UnitOfWorkProviderBinding> bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);

            var map = new Dictionary<OrmProvider, UnitOfWorkProviderBinding>();
            foreach (var binding in bindings)
            {
                if (map.TryGetValue(binding.Provider, out _))
                {
                    throw new InvalidOperationException(
                        $"Duplicate unit-of-work binding for provider '{binding.Provider}'. " +
                        "Each ORM provider must register exactly one binding; conflicting registrations are not resolved by order.");
                }

                map[binding.Provider] = binding;
            }

            _bindings = map;
        }

        /// <summary>
        /// 已注册的 Provider 绑定
        /// </summary>
        public IEnumerable<UnitOfWorkProviderBinding> Bindings => _bindings.Values;

        /// <summary>
        /// 获取指定 Provider 的绑定
        /// </summary>
        /// <exception cref="InvalidOperationException">该 Provider 没有注册绑定时抛出（含已注册列表，不返回 null、不回退扫描）</exception>
        public UnitOfWorkProviderBinding GetRequired(OrmProvider provider)
        {
            if (_bindings.TryGetValue(provider, out var binding))
            {
                return binding;
            }

            var registered = _bindings.Count == 0
                ? "<none>"
                : string.Join(", ", _bindings.Keys.OrderBy(key => key));
            throw new InvalidOperationException(
                $"No unit-of-work binding is registered for ORM provider '{provider}'. Registered providers: {registered}. " +
                "Register the provider package's unit-of-work services (for example Add...OrmModule / AddEfCoreUnitOfWork).");
        }

        /// <summary>
        /// 解析实际使用的 Provider。
        /// </summary>
        /// <remarks>
        /// 规则（无隐藏 last-wins）：
        /// 1. 调用方显式传入的 provider；
        /// 2. 应用显式声明的默认 provider（<c>AddUnitOfWork(defaultProvider: X)</c>）；
        /// 3. 恰好注册一个绑定时，该 Provider 即默认；
        /// 4. 多绑定且无显式默认时给出确定性异常，指引显式选择。
        /// </remarks>
        public OrmProvider ResolveProvider(OrmProvider? requested, OrmProvider? explicitDefault)
        {
            if (requested is not null)
            {
                return requested.Value;
            }

            if (explicitDefault is not null)
            {
                return explicitDefault.Value;
            }

            if (_bindings.Count == 1)
            {
                return _bindings.Keys.First();
            }

            if (_bindings.Count == 0)
            {
                throw new InvalidOperationException(
                    "No unit-of-work provider binding is registered. " +
                    "Register a provider package's unit-of-work services before beginning a unit of work.");
            }

            throw new InvalidOperationException(
                $"No default ORM provider is declared but multiple providers are registered " +
                $"({string.Join(", ", _bindings.Keys.OrderBy(key => key))}). " +
                "Pass an explicit provider or declare AddUnitOfWork(defaultProvider: <provider>).");
        }
    }
}
