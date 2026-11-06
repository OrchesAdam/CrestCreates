using System;
using System.Collections.Generic;
using System.Threading;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 执行窗口的截止时间与联动 token 策略：owner 截止时间由自身超时决定；join 只能收紧外层剩余期限。
    /// execution token = 调用方 CT ⊕（join 时）owner token ⊕ 截止时间。
    /// </summary>
    internal static class UnitOfWorkExecutionTokenPolicy
    {
        internal static DateTimeOffset? ComputeDeadline(
            TimeSpan? requestedTimeout,
            bool isOwner,
            DateTimeOffset? ownerDeadline)
        {
            DateTimeOffset? own = requestedTimeout is { } timeout ? DateTimeOffset.UtcNow + timeout : null;

            if (isOwner)
            {
                return own;
            }

            // join：继承外层剩余期限，只可收紧。
            return ownerDeadline is { } inherited
                ? (own is { } value && value > inherited ? inherited : own ?? inherited)
                : own;
        }

        internal static (CancellationTokenSource? Cts, CancellationToken Token) CreateExecutionToken(
            CancellationToken callerToken,
            CancellationToken ownerToken,
            bool isOwner,
            DateTimeOffset? deadline)
        {
            var sources = new List<CancellationToken>(2);
            if (callerToken.CanBeCanceled)
            {
                sources.Add(callerToken);
            }

            if (!isOwner && ownerToken.CanBeCanceled)
            {
                sources.Add(ownerToken);
            }

            if (sources.Count == 0)
            {
                if (deadline is null)
                {
                    return (null, callerToken);
                }

                var standalone = new CancellationTokenSource();
                standalone.CancelAfter(deadline.Value - DateTimeOffset.UtcNow);
                return (standalone, standalone.Token);
            }

            if (sources.Count == 1 && deadline is null)
            {
                // 唯一来源直接复用：无论是 caller token 还是 owner token（join 继承）都必须生效。
                return (null, sources[0]);
            }

            var cts = sources.Count switch
            {
                1 => CancellationTokenSource.CreateLinkedTokenSource(sources[0]),
                _ => CancellationTokenSource.CreateLinkedTokenSource(sources[0], sources[1])
            };

            if (deadline is { } deadlineValue)
            {
                var remaining = deadlineValue - DateTimeOffset.UtcNow;
                cts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }

            return (cts, cts.Token);
        }
    }
}
