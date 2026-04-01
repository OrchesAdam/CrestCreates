using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;

namespace CrestCreates.Validation.Validators
{
    /// <summary>
    /// 验证器基类
    /// </summary>
    /// <typeparam name="T">验证对象类型</typeparam>
    public abstract class ValidatorBase<T> : AbstractValidator<T>
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        protected ValidatorBase()
        {
            ConfigureRules();
        }

        /// <summary>
        /// 配置验证规则
        /// </summary>
        protected abstract void ConfigureRules();
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 是否验证成功
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 成功结果
        /// </summary>
        public static ValidationResult Success => new ValidationResult { IsValid = true };

        /// <summary>
        /// 失败结果
        /// </summary>
        /// <param name="errors">错误信息</param>
        /// <returns>验证结果</returns>
        public static ValidationResult Failure(params string[] errors)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = errors.ToList()
            };
        }
    }

    /// <summary>
    /// 验证扩展方法
    /// </summary>
    public static class ValidationExtensions
    {
        /// <summary>
        /// 验证对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="validator">验证器</param>
        /// <param name="instance">要验证的对象</param>
        /// <returns>验证结果</returns>
        public static ValidationResult Validate<T>(this IValidator<T> validator, T instance)
        {
            var result = validator.Validate(instance);
            if (result.IsValid)
            {
                return ValidationResult.Success;
            }

            var errors = result.Errors.Select(e => e.ErrorMessage).ToList();
            return ValidationResult.Failure(errors.ToArray());
        }
    }
}