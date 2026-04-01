using System;
using System.Threading.Tasks;

namespace CrestCreates.Configuration.Services
{
    public interface IConfigurationService
    {
        /// <summary>
        /// 获取配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        T Get<T>(string key, T defaultValue = default);

        /// <summary>
        /// 获取配置值（异步）
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        Task<T> GetAsync<T>(string key, T defaultValue = default);

        /// <summary>
        /// 设置配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        void Set<T>(string key, T value);

        /// <summary>
        /// 设置配置值（异步）
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        /// <returns>任务</returns>
        Task SetAsync<T>(string key, T value);

        /// <summary>
        /// 订阅配置变更
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="callback">回调函数</param>
        /// <returns>取消订阅的方法</returns>
        IDisposable Subscribe<T>(string key, Action<T> callback);

        /// <summary>
        /// 刷新配置
        /// </summary>
        void Refresh();

        /// <summary>
        /// 刷新配置（异步）
        /// </summary>
        /// <returns>任务</returns>
        Task RefreshAsync();
    }
}