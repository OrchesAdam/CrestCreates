using System;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.Abstract
{
    /// <summary>
    /// 工作单元工厂接口
    /// </summary>
    /// <remarks>
    /// 负责根据指定的 ORM 提供者创建相应的工作单元实例
    /// </remarks>
    public interface IUnitOfWorkFactory
    {
        /// <summary>
        /// 创建工作单元实例
        /// </summary>
        /// <param name="provider">ORM 提供者类型</param>
        /// <returns>工作单元实例</returns>
        IUnitOfWork Create(OrmProvider provider);
    }
}
