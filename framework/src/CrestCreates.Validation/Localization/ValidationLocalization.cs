using System;
using FluentValidation;

namespace CrestCreates.Validation.Localization;

/// <summary>
/// 验证错误码定义，用于本地化验证消息
/// </summary>
public static class ValidationErrorCodes
{
    public const string Required = "Crest.Validation.Required";
    public const string InvalidFormat = "Crest.Validation.InvalidFormat";
    public const string MinLength = "Crest.Validation.MinLength";
    public const string MaxLength = "Crest.Validation.MaxLength";
    public const string Range = "Crest.Validation.Range";
    public const string Email = "Crest.Validation.Email";
    public const string PhoneNumber = "Crest.Validation.PhoneNumber";
    public const string Url = "Crest.Validation.Url";
    public const string MustBeTrue = "Crest.Validation.MustBeTrue";
    public const string MustBeFalse = "Crest.Validation.MustBeFalse";
    public const string NotEmpty = "Crest.Validation.NotEmpty";
    public const string Equal = "Crest.Validation.Equal";
    public const string NotEqual = "Crest.Validation.NotEqual";
    public const string GreaterThan = "Crest.Validation.GreaterThan";
    public const string LessThan = "Crest.Validation.LessThan";
    public const string GreaterThanOrEqual = "Crest.Validation.GreaterThanOrEqual";
    public const string LessThanOrEqual = "Crest.Validation.LessThanOrEqual";
    public const string Length = "Crest.Validation.Length";
    public const string Matches = "Crest.Validation.Matches";
    public const string InclusiveBetween = "Crest.Validation.InclusiveBetween";
    public const string ExclusiveBetween = "Crest.Validation.ExclusiveBetween";
    public const string Unknown = "Crest.Validation.Unknown";
}

/// <summary>
/// 验证本地化扩展方法
/// </summary>
public static class ValidationLocalizationExtensions
{
    /// <summary>
    /// 使用错误码和回退消息进行本地化验证
    /// FluentValidation 会使用 ErrorCode 作为消息键进行本地化查找，找不到则使用 fallbackMessage
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> WithLocalizedMessage<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> ruleBuilder,
        string errorCode,
        string fallbackMessage)
    {
        ruleBuilder.WithErrorCode(errorCode);
        ruleBuilder.WithMessage(fallbackMessage);
        return ruleBuilder;
    }
}