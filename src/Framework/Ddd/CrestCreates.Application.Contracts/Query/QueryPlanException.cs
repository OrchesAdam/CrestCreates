using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Application.Contracts.Query;

/// <summary>
/// #128 S3 (§8): a filter/sort request that cannot be planned by a generated query adapter — an unknown
/// or non-filterable/sortable field, or an operator the field's static CLR kind does not support. Plan
/// rejections happen before the query executes and are never ignored or degraded into an unfiltered
/// query. A value that does not fit an otherwise supported (field, operator) pair stays a
/// <see cref="FilterValueFormatException"/>.
/// Derives from <see cref="CrestBusinessException"/> so the rejection keeps the framework's
/// business-error (400) surface instead of degrading to an unhandled server failure.
/// </summary>
public sealed class QueryPlanException : CrestBusinessException
{
    public QueryPlanException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
