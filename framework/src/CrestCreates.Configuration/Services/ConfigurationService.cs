using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace CrestCreates.Configuration.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, List<object>> _subscriptions = new Dictionary<string, List<object>>();

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            var value = _configuration.GetValue<T>(key, defaultValue);
            return value;
        }

        public async Task<T> GetAsync<T>(string key, T defaultValue = default)
        {
            // 异步实现，实际上配置读取是同步的
            return await Task.FromResult(Get(key, defaultValue));
        }

        public void Set<T>(string key, T value)
        {
            // 注意：IConfiguration本身是只读的，这里只是示例实现
            // 实际应用中可能需要使用配置提供程序的特定实现来支持写入
            throw new NotSupportedException("Configuration is read-only");
        }

        public async Task SetAsync<T>(string key, T value)
        {
            await Task.Run(() => Set(key, value));
        }

        public IDisposable Subscribe<T>(string key, Action<T> callback)
        {
            if (!_subscriptions.ContainsKey(key))
            {
                _subscriptions[key] = new List<object>();
            }

            _subscriptions[key].Add(callback);

            return new DisposableAction(() =>
            {
                _subscriptions[key].Remove(callback);
                if (_subscriptions[key].Count == 0)
                {
                    _subscriptions.Remove(key);
                }
            });
        }

        public void Refresh()
        {
            // 刷新配置
            if (_configuration is IConfigurationRoot configurationRoot)
            {
                configurationRoot.Reload();
            }

            // 通知所有订阅者
            NotifySubscribers();
        }

        public async Task RefreshAsync()
        {
            await Task.Run(() => Refresh());
        }

        private void NotifySubscribers()
        {
            foreach (var key in _subscriptions.Keys)
            {
                var callbacks = _subscriptions[key];
                foreach (var callback in callbacks)
                {
                    // 这里简化实现，实际应用中需要根据具体类型进行处理
                }
            }
        }

        private class DisposableAction : IDisposable
        {
            private readonly Action _action;

            public DisposableAction(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }
}