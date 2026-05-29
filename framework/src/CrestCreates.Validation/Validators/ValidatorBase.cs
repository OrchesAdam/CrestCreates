using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using CrestCreates.Validation.Localization;

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
    /// 验证结果（支持错误码本地化）
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 是否验证成功
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 错误码列表（用于本地化）
        /// </summary>
        public List<string> ErrorCodes { get; set; } = new List<string>();

        /// <summary>
        /// 属性名-错误码映射（用于精准本地化）
        /// </summary>
        public List<ValidationErrorDetail> ErrorDetails { get; set; } = new List<ValidationErrorDetail>();

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

        /// <summary>
        /// 失败结果（带错误码）
        /// </summary>
        public static ValidationResult FailureWithCodes(List<ValidationErrorDetail> details)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = details.Select(d => d.ErrorMessage).ToList(),
                ErrorCodes = details.Select(d => d.ErrorCode ?? ValidationErrorCodes.Unknown).ToList(),
                ErrorDetails = details
            };
        }
    }

    /// <summary>
    /// 验证错误详情
    /// </summary>
    public class ValidationErrorDetail
    {
        public string PropertyName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 用户尝试输入的值。注意：对于密码、信用卡号等敏感字段，此值可能包含敏感数据，
        /// 序列化为 API 响应前应考虑脱敏或忽略。
        /// </summary>
        public object? AttemptedValue { get; set; }
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

            var details = result.Errors.Select(e => new ValidationErrorDetail
            {
                PropertyName = e.PropertyName,
                ErrorMessage = e.ErrorMessage,
                ErrorCode = e.ErrorCode,
                AttemptedValue = e.AttemptedValue
            }).ToList();

            return ValidationResult.FailureWithCodes(details);
        }
    }
}