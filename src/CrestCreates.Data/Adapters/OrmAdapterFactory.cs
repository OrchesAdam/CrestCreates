using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Adapters
{
    /// <summary>
    /// ORM适配器工厂接口
    /// </summary>
    public interface IOrmAdapterFactory
    {
        /// <summary>
        /// 获取指定类型的ORM适配器
        /// </summary>
        /// <param name="ormType">ORM类型</param>
        /// <returns>ORM适配器</returns>
        IOrmAdapter GetAdapter(OrmType ormType);

        /// <summary>
        /// 获取支持指定数据库类型的ORM适配器
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>ORM适配器</returns>
        IOrmAdapter GetAdapterForDatabase(DatabaseType databaseType);

        /// <summary>
        /// 注册ORM适配器
        /// </summary>
        /// <param name="adapter">ORM适配器</param>
        void RegisterAdapter(IOrmAdapter adapter);

        /// <summary>
        /// 获取所有已注册的适配器
        /// </summary>
        /// <returns>适配器集合</returns>
        IEnumerable<IOrmAdapter> GetAllAdapters();

        /// <summary>
        /// 检查是否存在指定类型的适配器
        /// </summary>
        /// <param name="ormType">ORM类型</param>
        /// <returns>是否存在</returns>
        bool HasAdapter(OrmType ormType);
    }

    /// <summary>
    /// ORM适配器工厂实现
    /// </summary>
    public class OrmAdapterFactory : IOrmAdapterFactory
    {
        private readonly Dictionary<OrmType, IOrmAdapter> _adapters = new();
        private readonly IServiceProvider _serviceProvider;

        public OrmAdapterFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            InitializeDefaultAdapters();
        }

        /// <summary>
        /// 初始化默认适配器
        /// </summary>
        private void InitializeDefaultAdapters()
        {
            // 从DI容器中获取所有已注册的适配器
            var adapters = _serviceProvider.GetServices<IOrmAdapter>();
            foreach (var adapter in adapters)
            {
                RegisterAdapter(adapter);
            }
        }

        public IOrmAdapter GetAdapter(OrmType ormType)
        {
            if (_adapters.TryGetValue(ormType, out var adapter))
            {
                return adapter;
            }

            throw new NotSupportedException($"ORM type '{ormType}' is not supported. Available adapters: {string.Join(", ", _adapters.Keys)}");
        }

        public IOrmAdapter GetAdapterForDatabase(DatabaseType databaseType)
        {
            var adapter = _adapters.Values.FirstOrDefault(a => a.SupportsDatabase(databaseType));
            if (adapter != null)
            {
                return adapter;
            }

            throw new NotSupportedException($"Database type '{databaseType}' is not supported by any registered ORM adapter.");
        }

        public void RegisterAdapter(IOrmAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));

            _adapters[adapter.OrmType] = adapter;
        }

        public IEnumerable<IOrmAdapter> GetAllAdapters()
        {
            return _adapters.Values;
        }

        public bool HasAdapter(OrmType ormType)
        {
            return _adapters.ContainsKey(ormType);
        }
    }
}
