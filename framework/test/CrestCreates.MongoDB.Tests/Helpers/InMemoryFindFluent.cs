using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace CrestCreates.MongoDB.Tests.Helpers;

/// <summary>
/// In-memory IFindFluent for MongoDB.Driver 3.x testing.
/// Supports Skip, Limit, ToCursor/ToCursorAsync (used by ToListAsync extension).
/// SortBy tests are skipped because SortBy is an extension method in v3 that cannot be mocked.
/// </summary>
public class InMemoryFindFluent<TDocument, TProjection> : IFindFluent<TDocument, TProjection>
    where TDocument : class
{
    private readonly List<TDocument> _source;
    private IQueryable<TDocument> _query;

    public InMemoryFindFluent(List<TDocument> source)
    {
        _source = source ?? new List<TDocument>();
        _query = _source.AsQueryable();
    }

    public FilterDefinition<TDocument>? Filter { get; set; }
    public FindOptions<TDocument, TProjection> Options { get; } = new();

    // IAsyncCursorSource — ToListAsync extension calls these
    public IAsyncCursor<TProjection> ToCursor(CancellationToken cancellationToken = default)
    {
        var results = _query.Cast<TProjection>().ToList();
        return new InMemoryAsyncCursor<TProjection>(results);
    }

    public Task<IAsyncCursor<TProjection>> ToCursorAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ToCursor(cancellationToken));

    // IFindFluent interface
    public IFindFluent<TDocument, TNewProjection> As<TNewProjection>(IBsonSerializer<TNewProjection> serializer) =>
        new InMemoryFindFluent<TDocument, TNewProjection>(_source);

    public long Count(CancellationToken cancellationToken = default) => _query.Count();
    public Task<long> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)_query.Count());
    public long CountDocuments(CancellationToken cancellationToken = default) => _query.Count();
    public Task<long> CountDocumentsAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)_query.Count());

    public IFindFluent<TDocument, TProjection> Limit(int? value)
    {
        if (value.HasValue) _query = _query.Take(value.Value);
        return this;
    }

    public IFindFluent<TDocument, TNewProjection> Project<TNewProjection>(ProjectionDefinition<TDocument, TNewProjection> projection) =>
        new InMemoryFindFluent<TDocument, TNewProjection>(_source);

    public IFindFluent<TDocument, TProjection> Skip(int? value)
    {
        if (value.HasValue) _query = _query.Skip(value.Value);
        return this;
    }

    public IFindFluent<TDocument, TProjection> Sort(SortDefinition<TDocument> sort) => this;

    public string ToString(ExpressionTranslationOptions? translationOptions) => string.Empty;
}

/// <summary>
/// In-memory IAsyncCursor for testing.
/// </summary>
public class InMemoryAsyncCursor<T> : IAsyncCursor<T>
{
    private readonly List<T> _items;
    private bool _moved;

    public InMemoryAsyncCursor(List<T> items) => _items = items ?? new List<T>();

    public IEnumerable<T> Current => _items;

    public bool MoveNext(CancellationToken cancellationToken = default)
    {
        if (_moved) return false;
        _moved = true;
        return _items.Count > 0;
    }

    public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(MoveNext(cancellationToken));

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
