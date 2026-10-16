using System.Collections;
using System.Linq.Expressions;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.GeneratedCrud.AotFixture.Domain;

namespace CrestCreates.GeneratedCrud.AotFixture;

/// <summary>
/// #128 S4 memory query provider: the fixture's statically executed <see cref="IQueryable{T}"/>.
/// §8 requires that when the BCL's in-memory IQueryable cannot execute a generated operation
/// combination natively, the fixture converges to a statically supported execution scheme — the BCL
/// <c>EnumerableQuery</c> fails both ways on NativeAOT (reflective <c>Enumerable.OrderByDescending</c>
/// instantiation, and <c>Expression.Equal</c> operator lookup for <see cref="decimal"/>), which the
/// S4 native gate reproduced. This provider therefore interprets exactly the shapes the generated
/// typed adapter emits, executes them with compiled-in delegates, and rejects everything else with a
/// deterministic <see cref="QueryPlanException"/> instead of guessing.
///
/// Supported shapes (the generated adapter's complete surface):
/// Where(expr over the six Widget properties with ==/!=/&lt;/&lt;=/&gt;/&gt;=, string
/// Contains/StartsWith/EndsWith, Enumerable.Contains for In/NotIn, null guards, &amp;&amp;/||/!),
/// OrderBy/OrderByDescending/ThenBy/ThenByDescending(member key selector), Skip(n), Take(n),
/// Count()/Count(predicate), Any(predicate), FirstOrDefault(predicate).
/// </summary>
internal sealed class WidgetQueryProvider : IQueryProvider
{
    public IQueryable CreateQuery(Expression expression)
        => new WidgetQuery(this, expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        RequireElementType<TElement>();
        return (IQueryable<TElement>)CreateQuery(expression);
    }

    public object? Execute(Expression expression)
        => WidgetQueryPlan.Execute(expression);

    public TResult Execute<TResult>(Expression expression)
    {
        var result = WidgetQueryPlan.Execute(expression);
        if (result is null)
        {
            if (default(TResult) is not null)
            {
                throw Unsupported($"a null query result cannot be returned as {typeof(TResult).Name}");
            }

            return default!;
        }

        if (result is TResult typed)
        {
            return typed;
        }

        throw Unsupported($"the query result '{result.GetType().Name}' cannot be returned as {typeof(TResult).Name}");
    }

    internal static QueryPlanException Unsupported(string reason)
        => new(
            "Crest.Crud.UnsupportedMemoryQuery",
            "The fixture memory query provider executes only the shapes of the generated CRUD adapter; "
            + reason + ".");

    private static void RequireElementType<TElement>()
    {
        if (typeof(TElement) != typeof(Widget))
        {
            throw Unsupported($"element type {typeof(TElement).Name} is not Widget");
        }
    }
}

/// <summary>The query object carrying the accumulated expression; it is also ordered-capable so the generated ThenBy path works.</summary>
internal sealed class WidgetQuery : IQueryable<Widget>, IOrderedQueryable<Widget>
{
    private readonly IQueryProvider _provider;

    /// <summary>Root query over a store snapshot; its expression is the constant wrapper Queryable operators require.</summary>
    public WidgetQuery(IQueryProvider provider, List<Widget> source)
    {
        _provider = provider;
        Source = source;
        Expression = Expression.Constant(this);
    }

    public WidgetQuery(IQueryProvider provider, Expression expression)
    {
        _provider = provider;
        Source = [];
        Expression = expression;
    }

    internal List<Widget> Source { get; }

    public Type ElementType => typeof(Widget);

    public Expression Expression { get; }

    public IQueryProvider Provider => _provider;

    public IEnumerator<Widget> GetEnumerator()
        => ((IEnumerable<Widget>)_provider.Execute(Expression)!).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The plan builder and executor: walks the accumulated expression tree into ops, then executes statically.</summary>
internal static class WidgetQueryPlan
{
    private static readonly Dictionary<string, Func<Widget, object?>> Accessors = new(StringComparer.Ordinal)
    {
        ["Id"] = widget => widget.Id,
        ["Name"] = widget => widget.Name,
        ["Price"] = widget => widget.Price,
        ["Description"] = widget => widget.Description,
        ["CreationTime"] = widget => widget.CreationTime,
        ["ConcurrencyStamp"] = widget => widget.ConcurrencyStamp,
    };

    public static object? Execute(Expression expression)
    {
        var ops = new List<object>();
        var terminal = "list";
        Expression? current = expression;
        List<Widget>? rows = null;

        while (true)
        {
            if (current is MethodCallExpression call)
            {
                switch (call.Method.Name)
                {
                    case "Where":
                        ops.Add(new WhereOp(BuildPredicate(Unquote(call.Arguments[1]))));
                        break;
                    case "OrderBy":
                        ops.Add(new SortOp(BuildKeySelector(Unquote(call.Arguments[1])), Descending: false, Continued: false));
                        break;
                    case "OrderByDescending":
                        ops.Add(new SortOp(BuildKeySelector(Unquote(call.Arguments[1])), Descending: true, Continued: false));
                        break;
                    case "ThenBy":
                        ops.Add(new SortOp(BuildKeySelector(Unquote(call.Arguments[1])), Descending: false, Continued: true));
                        break;
                    case "ThenByDescending":
                        ops.Add(new SortOp(BuildKeySelector(Unquote(call.Arguments[1])), Descending: true, Continued: true));
                        break;
                    case "Skip":
                        ops.Add(new PagingOp(Offset: EvaluateInt(call.Arguments[1]), Take: null));
                        break;
                    case "Take":
                        ops.Add(new PagingOp(Offset: null, Take: EvaluateInt(call.Arguments[1])));
                        break;
                    case "Count" when call.Arguments.Count == 2:
                        ops.Add(new WhereOp(BuildPredicate(Unquote(call.Arguments[1]))));
                        terminal = "count";
                        break;
                    case "Count":
                        terminal = "count";
                        break;
                    case "Any" when call.Arguments.Count == 2:
                        ops.Add(new WhereOp(BuildPredicate(Unquote(call.Arguments[1]))));
                        terminal = "any";
                        break;
                    case "Any":
                        terminal = "any";
                        break;
                    case "FirstOrDefault" when call.Arguments.Count == 2:
                        ops.Add(new WhereOp(BuildPredicate(Unquote(call.Arguments[1]))));
                        terminal = "first";
                        break;
                    case "FirstOrDefault":
                        terminal = "first";
                        break;
                    default:
                        throw WidgetQueryProvider.Unsupported($"operation '{call.Method.Name}' is not supported");
                }

                current = call.Arguments[0];
                continue;
            }

            if (current is ConstantExpression constant)
            {
                rows = constant.Value is WidgetQuery root
                    ? root.Source
                    : constant.Value as List<Widget>
                        ?? throw WidgetQueryProvider.Unsupported("the query root is not a Widget snapshot");
                break;
            }

            throw WidgetQueryProvider.Unsupported(
                $"expression node '{current?.NodeType.ToString() ?? "null"}' is not supported");
        }

        // The walk collects the outermost call first; execution needs the composition order.
        ops.Reverse();
        var result = Apply(rows, ops);
        return terminal switch
        {
            "list" => result,
            "count" => result.Count,
            "any" => result.Count > 0,
            "first" => result.Count > 0 ? result[0] : null,
            _ => throw WidgetQueryProvider.Unsupported($"terminal '{terminal}' is not supported"),
        };
    }

    private static List<Widget> Apply(List<Widget> source, List<object> ops)
    {
        var rows = source;
        var pendingSorts = new List<SortKey>();

        void FlushSorts()
        {
            if (pendingSorts.Count == 0)
            {
                return;
            }

            rows = ApplySorts(rows, pendingSorts);
            pendingSorts.Clear();
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case WhereOp where:
                    FlushSorts();
                    rows = rows.Where(where.Predicate).ToList();
                    break;
                case SortOp sort when !sort.Continued:
                    FlushSorts();
                    pendingSorts.Add(new SortKey(sort.Key, sort.Descending));
                    break;
                case SortOp sort:
                    if (pendingSorts.Count == 0)
                    {
                        throw WidgetQueryProvider.Unsupported("ThenBy without a preceding OrderBy");
                    }

                    pendingSorts.Add(new SortKey(sort.Key, sort.Descending));
                    break;
                case PagingOp paging:
                    FlushSorts();
                    if (paging.Offset is int offset)
                    {
                        rows = rows.Skip(offset).ToList();
                    }

                    if (paging.Take is int take)
                    {
                        rows = rows.Take(take).ToList();
                    }

                    break;
                default:
                    throw WidgetQueryProvider.Unsupported($"op '{op.GetType().Name}' is not supported");
            }
        }

        FlushSorts();
        return rows;
    }

    /// <summary>Stable multi-key sort, mirroring LINQ's OrderBy/ThenBy stability with an original-index tiebreak.</summary>
    private static List<Widget> ApplySorts(List<Widget> rows, List<SortKey> keys)
    {
        var indexed = rows.Select((row, index) => (row, index)).ToList();
        indexed.Sort((left, right) =>
        {
            foreach (var key in keys)
            {
                var comparison = Comparer<object?>.Default.Compare(
                    key.Key(left.row), key.Key(right.row));
                if (comparison != 0)
                {
                    return key.Descending ? -comparison : comparison;
                }
            }

            return left.index.CompareTo(right.index);
        });

        return indexed.Select(entry => entry.row).ToList();
    }

    private static Func<Widget, bool> BuildPredicate(Expression body) => body switch
    {
        LambdaExpression lambda => BuildPredicate(lambda.Body),
        BinaryExpression binary when binary.NodeType == ExpressionType.AndAlso
            => widget => BuildPredicate(binary.Left)(widget) && BuildPredicate(binary.Right)(widget),
        BinaryExpression binary when binary.NodeType == ExpressionType.OrElse
            => widget => BuildPredicate(binary.Left)(widget) || BuildPredicate(binary.Right)(widget),
        UnaryExpression unary when unary.NodeType == ExpressionType.Not
            => widget => !BuildPredicate(unary.Operand)(widget),
        BinaryExpression binary => BuildComparison(binary),
        MethodCallExpression call => BuildMethodCall(call),
        _ => throw WidgetQueryProvider.Unsupported(
            $"predicate node '{body.NodeType}' is not supported"),
    };

    private static Func<Widget, bool> BuildComparison(BinaryExpression binary)
    {
        var left = BuildScalar(binary.Left);
        var right = BuildScalar(binary.Right);
        return binary.NodeType switch
        {
            ExpressionType.Equal => widget => Equals(left(widget), right(widget)),
            ExpressionType.NotEqual => widget => !Equals(left(widget), right(widget)),
            ExpressionType.GreaterThan => widget => Compare(left(widget), right(widget)) > 0,
            ExpressionType.GreaterThanOrEqual => widget => Compare(left(widget), right(widget)) >= 0,
            ExpressionType.LessThan => widget => Compare(left(widget), right(widget)) < 0,
            ExpressionType.LessThanOrEqual => widget => Compare(left(widget), right(widget)) <= 0,
            _ => throw WidgetQueryProvider.Unsupported(
                $"comparison '{binary.NodeType}' is not supported"),
        };
    }

    /// <summary>
    /// Scalar reader for one comparison operand: a Widget member read, a member method call the
    /// adapter emits for decimal (Equals/CompareTo), or a captured value. Anything else is rejected.
    /// </summary>
    private static Func<Widget, object?> BuildScalar(Expression expression)
    {
        var stripped = StripConvert(expression);
        if (stripped is MemberExpression { Expression: ParameterExpression } member
            && Accessors.TryGetValue(member.Member.Name, out var accessor))
        {
            return accessor;
        }

        if (stripped is MethodCallExpression call
            && call.Object is not null
            && call.Method.Name is "Equals" or "CompareTo")
        {
            var inner = BuildScalar(call.Object);
            var argument = Evaluate(call.Arguments[0]);
            return call.Method.Name == "CompareTo"
                ? widget => Compare(inner(widget), argument)
                : widget => Equals(inner(widget), argument);
        }

        var value = Evaluate(stripped);
        return _ => value;
    }

    private static Func<Widget, bool> BuildMethodCall(MethodCallExpression call)
    {
        if (call.Method.DeclaringType == typeof(string))
        {
            var read = BuildMemberReader(call.Object
                ?? throw WidgetQueryProvider.Unsupported("a static string method call"));
            var argument = Evaluate(call.Arguments[0]);
            var text = argument as string
                ?? throw WidgetQueryProvider.Unsupported("a non-string string-method argument");

            return call.Method.Name switch
            {
                "Contains" => widget => (read(widget) as string)?.Contains(text, StringComparison.Ordinal) == true,
                "StartsWith" => widget => (read(widget) as string)?.StartsWith(text, StringComparison.Ordinal) == true,
                "EndsWith" => widget => (read(widget) as string)?.EndsWith(text, StringComparison.Ordinal) == true,
                _ => throw WidgetQueryProvider.Unsupported($"string method '{call.Method.Name}' is not supported"),
            };
        }

        if (call.Method.Name == "Equals" && call.Object is not null)
        {
            var read = BuildMemberReader(call.Object);
            var argument = Evaluate(call.Arguments[0]);
            return widget => Equals(read(widget), argument);
        }

        if (call.Method.DeclaringType == typeof(Enumerable) && call.Method.Name == "Contains")
        {
            var set = Evaluate(call.Arguments[0]) as IEnumerable
                ?? throw WidgetQueryProvider.Unsupported("a non-enumerable Contains operand");
            var elements = set.Cast<object?>().ToList();
            var read = BuildMemberReader(call.Arguments[1]);
            return widget => elements.Any(element => Equals(element, read(widget)));
        }

        throw WidgetQueryProvider.Unsupported($"method '{call.Method.Name}' is not supported in a predicate");
    }

    private static Func<Widget, object?> BuildKeySelector(Expression expression)
    {
        var lambda = expression as LambdaExpression
            ?? throw WidgetQueryProvider.Unsupported("a non-lambda sort key");
        var body = StripConvert(lambda.Body);
        return BuildMemberReader(body);
    }

    private static Func<Widget, object?> BuildMemberReader(Expression expression)
    {
        var stripped = StripConvert(expression);
        if (stripped is MemberExpression member
            && member.Expression is ParameterExpression
            && Accessors.TryGetValue(member.Member.Name, out var accessor))
        {
            return accessor;
        }

        throw WidgetQueryProvider.Unsupported(
            $"member access '{stripped}' is not one of the Widget accessors");
    }

    private static bool IsParameterMember(Expression expression)
        => StripConvert(expression) is MemberExpression { Expression: ParameterExpression };

    private static Expression StripConvert(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? StripConvert(unary.Operand)
            : expression;

    private static object? Evaluate(Expression expression)
    {
        var current = StripConvert(expression);
        switch (current)
        {
            case ConstantExpression constant:
                return constant.Value;
            case MemberExpression { Expression: ConstantExpression closure } member
                when member.Member is System.Reflection.FieldInfo field:
                return field.GetValue(closure.Value);
            default:
                throw WidgetQueryProvider.Unsupported(
                    $"value expression '{current.NodeType}' is not supported");
        }
    }

    private static int EvaluateInt(Expression expression)
    {
        var value = Evaluate(expression);
        return value is int typed
            ? typed
            : throw WidgetQueryProvider.Unsupported("a non-int paging operand");
    }

    private static int Compare(object? left, object? right)
    {
        var comparison = Comparer<object?>.Default.Compare(left, right);
        return comparison;
    }

    private sealed record WhereOp(Func<Widget, bool> Predicate);

    private sealed record SortOp(Func<Widget, object?> Key, bool Descending, bool Continued);

    private sealed record PagingOp(int? Offset, int? Take);

    private sealed record SortKey(Func<Widget, object?> Key, bool Descending);

    private static Expression Unquote(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Quote } unary
            ? unary.Operand
            : expression;
}
