using System.Linq.Expressions;
using System.Reflection;
using SaaSHelpdesk.Domain.Entities;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Bounded static queryables for the Ticket memory leaf. The base AppService only issues
/// <c>ToList()</c> and <c>FirstOrDefault(e =&gt; e.Id.Equals(id))</c>; the SLA lookup only issues
/// <c>FirstOrDefault(p =&gt; p.IsActive)</c>. They are interpreted directly over row snapshots with
/// compiled-in accessors — no BCL EnumerableQuery, Expression.Compile or reflective LINQ operators.
/// </summary>
internal sealed class TicketQueryable : IQueryable<Ticket>
{
    private readonly TicketQueryProvider _provider;

    internal TicketQueryable(Func<IReadOnlyList<TicketRow>> rows)
    {
        _provider = new TicketQueryProvider(rows);
        Expression = System.Linq.Expressions.Expression.Constant(this);
    }

    public Type ElementType => typeof(Ticket);
    public Expression Expression { get; }
    public IQueryProvider Provider => _provider;

    public IEnumerator<Ticket> GetEnumerator()
        => _provider.Execute<IEnumerable<Ticket>>(Expression).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class TicketQueryProvider(Func<IReadOnlyList<TicketRow>> rows) : IQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => throw Unsupported(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => throw Unsupported(expression);
        public object? Execute(Expression expression) => Execute<object>(expression);

        public TResult Execute<TResult>(Expression expression)
        {
            if (expression is ConstantExpression)
            {
                var all = rows().Select(TicketRowsMapper.ToTicket).ToList();
                if (typeof(TResult) == typeof(IEnumerable<Ticket>) || typeof(TResult) == typeof(List<Ticket>))
                {
                    return (TResult)(object)all;
                }

                if (typeof(TResult) == typeof(Ticket))
                {
                    return (TResult)(object?)all.FirstOrDefault()!;
                }

                if (typeof(TResult) == typeof(int))
                {
                    return (TResult)(object)all.Count;
                }

                if (typeof(TResult) == typeof(bool))
                {
                    return (TResult)(object)(all.Count > 0);
                }

                throw Unsupported(expression);
            }

            if (expression is not MethodCallExpression call)
            {
                throw Unsupported(expression);
            }

            var snapshot = rows();
            return call.Method.Name switch
            {
                "ToList" => (TResult)(object)snapshot.Select(TicketRowsMapper.ToTicket).ToList(),
                "FirstOrDefault" => FirstOrDefault(snapshot, call) is { } item ? (TResult)(object)item : default!,
                "Count" => (TResult)(object)snapshot.Count,
                "Any" => (TResult)(object)(snapshot.Count > 0),
                _ => throw Unsupported(expression),
            };
        }

        private static Ticket? FirstOrDefault(IReadOnlyList<TicketRow> snapshot, MethodCallExpression call)
        {
            if (call.Arguments.Count == 1)
            {
                return snapshot.Count == 0 ? null : TicketRowsMapper.ToTicket(snapshot[0]);
            }

            var lambda = (LambdaExpression)StripQuote(call.Arguments[1]);
            var id = ParseId(lambda);
            foreach (var row in snapshot)
            {
                if (row.Id == id)
                {
                    return TicketRowsMapper.ToTicket(row);
                }
            }

            return null;
        }

        private static Guid ParseId(LambdaExpression lambda)
        {
            if (lambda.Body is MethodCallExpression equals
                && equals.Method.Name == nameof(object.Equals)
                && equals.Object is MemberExpression member
                && member.Member.Name == "Id")
            {
                return ReadGuid(equals.Arguments[0]);
            }

            if (lambda.Body is BinaryExpression binary
                && binary.NodeType == ExpressionType.Equal
                && binary.Left is MemberExpression left
                && left.Member.Name == "Id")
            {
                return ReadGuid(binary.Right);
            }

            throw new InvalidOperationException(
                $"Unsupported Ticket predicate '{lambda}'. The native fixture only supports Id equality.");
        }

        private static Guid ReadGuid(Expression expression)
        {
            if (expression is ConstantExpression constant && constant.Value is Guid direct)
            {
                return direct;
            }

            if (expression is MemberExpression member
                && member.Expression is ConstantExpression closure
                && member.Member is FieldInfo field
                && field.GetValue(closure.Value) is Guid captured)
            {
                return captured;
            }

            throw new InvalidOperationException($"Unsupported Ticket id expression '{expression}'.");
        }

        private static Expression StripQuote(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Quote } unary ? unary.Operand : expression;

        private static InvalidOperationException Unsupported(Expression expression)
            => new($"Unsupported Ticket query '{expression}'. The native fixture only supports the AppService read shapes.");
    }
}

internal sealed class SlaQueryable : IQueryable<SLAPolicy>
{
    private readonly SlaQueryProvider _provider;

    internal SlaQueryable(Func<IReadOnlyList<SlaRow>> rows)
    {
        _provider = new SlaQueryProvider(rows);
        Expression = System.Linq.Expressions.Expression.Constant(this);
    }

    public Type ElementType => typeof(SLAPolicy);
    public Expression Expression { get; }
    public IQueryProvider Provider => _provider;

    public IEnumerator<SLAPolicy> GetEnumerator()
        => _provider.Execute<IEnumerable<SLAPolicy>>(Expression).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class SlaQueryProvider(Func<IReadOnlyList<SlaRow>> rows) : IQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => throw Unsupported(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => throw Unsupported(expression);
        public object? Execute(Expression expression) => Execute<object>(expression);

        public TResult Execute<TResult>(Expression expression)
        {
            if (expression is ConstantExpression)
            {
                var all = rows().Select(TicketRowsMapper.ToSlaPolicy).ToList();
                if (typeof(TResult) == typeof(IEnumerable<SLAPolicy>) || typeof(TResult) == typeof(List<SLAPolicy>))
                {
                    return (TResult)(object)all;
                }

                if (typeof(TResult) == typeof(SLAPolicy))
                {
                    return (TResult)(object?)all.FirstOrDefault()!;
                }

                if (typeof(TResult) == typeof(int))
                {
                    return (TResult)(object)all.Count;
                }

                if (typeof(TResult) == typeof(bool))
                {
                    return (TResult)(object)(all.Count > 0);
                }

                throw Unsupported(expression);
            }

            if (expression is not MethodCallExpression call)
            {
                throw Unsupported(expression);
            }

            var snapshot = rows();
            return call.Method.Name switch
            {
                "ToList" => (TResult)(object)snapshot.Select(TicketRowsMapper.ToSlaPolicy).ToList(),
                "FirstOrDefault" => FirstOrDefault(snapshot, call) is { } item ? (TResult)(object)item : default!,
                "Count" => (TResult)(object)snapshot.Count,
                "Any" => (TResult)(object)(snapshot.Count > 0),
                _ => throw Unsupported(expression),
            };
        }

        private static SLAPolicy? FirstOrDefault(IReadOnlyList<SlaRow> snapshot, MethodCallExpression call)
        {
            if (call.Arguments.Count == 1)
            {
                return snapshot.Count == 0 ? null : TicketRowsMapper.ToSlaPolicy(snapshot[0]);
            }

            var lambda = (LambdaExpression)StripQuote(call.Arguments[1]);
            if (lambda.Body is MemberExpression member && member.Member.Name == nameof(SLAPolicy.IsActive))
            {
                foreach (var row in snapshot)
                {
                    if (row.IsActive)
                    {
                        return TicketRowsMapper.ToSlaPolicy(row);
                    }
                }

                return null;
            }

            throw new InvalidOperationException(
                $"Unsupported SLA predicate '{lambda}'. The native fixture only supports the active-policy lookup.");
        }

        private static Expression StripQuote(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Quote } unary ? unary.Operand : expression;

        private static InvalidOperationException Unsupported(Expression expression)
            => new($"Unsupported SLA query '{expression}'. The native fixture only supports the AppService lookup shape.");
    }
}
