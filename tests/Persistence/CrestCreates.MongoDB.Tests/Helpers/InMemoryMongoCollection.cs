using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace CrestCreates.MongoDB.Tests.Helpers;

/// <summary>
/// In-memory IMongoCollection implementation for repository tests.
/// </summary>
public class InMemoryMongoCollection<TDocument> : IMongoCollection<TDocument>
    where TDocument : class
{
    private readonly List<TDocument> _documents = new();

    public InMemoryMongoCollection(IEnumerable<TDocument>? initialDocuments = null)
    {
        if (initialDocuments != null)
        {
            _documents.AddRange(initialDocuments);
        }
    }

    public CollectionNamespace CollectionNamespace => new("test", "test");
    public IMongoDatabase Database => throw new NotSupportedException();
    public IBsonSerializer<TDocument> DocumentSerializer => BsonSerializer.LookupSerializer<TDocument>();
    public IMongoIndexManager<TDocument> Indexes => throw new NotSupportedException();
    public IMongoSearchIndexManager SearchIndexes => throw new NotSupportedException();
    public MongoCollectionSettings Settings => new();

    public IAsyncCursor<TResult> Aggregate<TResult>(PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncCursor<TResult> Aggregate<TResult>(IClientSessionHandle session, PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(IClientSessionHandle session, PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void AggregateToCollection<TResult>(PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void AggregateToCollection<TResult>(IClientSessionHandle session, PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task AggregateToCollectionAsync<TResult>(PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task AggregateToCollectionAsync<TResult>(IClientSessionHandle session, PipelineDefinition<TDocument, TResult> pipeline, AggregateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public BulkWriteResult<TDocument> BulkWrite(IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public BulkWriteResult<TDocument> BulkWrite(IClientSessionHandle session, IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(IClientSessionHandle session, IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public long Count(FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        _documents.Count;

    public long Count(IClientSessionHandle session, FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        Count(filter, options, cancellationToken);

    public Task<long> CountAsync(FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(Count(filter, options, cancellationToken));

    public Task<long> CountAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(Count(session, filter, options, cancellationToken));

    public long CountDocuments(FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        _documents.Count;

    public long CountDocuments(IClientSessionHandle session, FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        CountDocuments(filter, options, cancellationToken);

    public Task<long> CountDocumentsAsync(FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(CountDocuments(filter, options, cancellationToken));

    public Task<long> CountDocumentsAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, CountOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(CountDocuments(session, filter, options, cancellationToken));

    public DeleteResult DeleteMany(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default) =>
        DeleteMany(filter, new DeleteOptions(), cancellationToken);

    public DeleteResult DeleteMany(FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default)
    {
        var count = _documents.Count;
        _documents.Clear();
        return new DeleteResult.Acknowledged(count);
    }

    public DeleteResult DeleteMany(IClientSessionHandle session, FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default) =>
        DeleteMany(filter, options, cancellationToken);

    public Task<DeleteResult> DeleteManyAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeleteMany(filter, cancellationToken));

    public Task<DeleteResult> DeleteManyAsync(FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeleteMany(filter, options, cancellationToken));

    public Task<DeleteResult> DeleteManyAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeleteMany(session, filter, options, cancellationToken));

    public DeleteResult DeleteOne(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default) =>
        DeleteOne(filter, new DeleteOptions(), cancellationToken);

    public DeleteResult DeleteOne(FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default)
    {
        var removed = _documents.Count > 0;
        if (removed)
        {
            _documents.RemoveAt(0);
        }

        return new DeleteResult.Acknowledged(removed ? 1 : 0);
    }

    public DeleteResult DeleteOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default) =>
        DeleteOne(filter, options, cancellationToken);

    public Task<DeleteResult> DeleteOneAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeleteOne(filter, cancellationToken));

    public Task<DeleteResult> DeleteOneAsync(FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeleteOne(filter, options, cancellationToken));

    public Task<DeleteResult> DeleteOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, DeleteOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeleteOne(session, filter, options, cancellationToken));

    public IAsyncCursor<TField> Distinct<TField>(FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncCursor<TField> Distinct<TField>(IClientSessionHandle session, FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TField>> DistinctAsync<TField>(FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TField>> DistinctAsync<TField>(IClientSessionHandle session, FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncCursor<TItem> DistinctMany<TItem>(FieldDefinition<TDocument, IEnumerable<TItem>> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncCursor<TItem> DistinctMany<TItem>(IClientSessionHandle session, FieldDefinition<TDocument, IEnumerable<TItem>> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(FieldDefinition<TDocument, IEnumerable<TItem>> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(IClientSessionHandle session, FieldDefinition<TDocument, IEnumerable<TItem>> field, FilterDefinition<TDocument> filter, DistinctOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public long EstimatedDocumentCount(EstimatedDocumentCountOptions options, CancellationToken cancellationToken = default) =>
        _documents.Count;

    public Task<long> EstimatedDocumentCountAsync(EstimatedDocumentCountOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult((long)_documents.Count);

    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncCursor<TProjection>>(new InMemoryAsyncCursor<TProjection>(_documents.Cast<TProjection>().ToList()));

    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        FindAsync(filter, options, cancellationToken);

    public IAsyncCursor<TProjection> FindSync<TProjection>(FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        new InMemoryAsyncCursor<TProjection>(_documents.Cast<TProjection>().ToList());

    public IAsyncCursor<TProjection> FindSync<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        FindSync(filter, options, cancellationToken);

    public TProjection FindOneAndDelete<TProjection>(FilterDefinition<TDocument> filter, FindOneAndDeleteOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public TProjection FindOneAndDelete<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOneAndDeleteOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TProjection> FindOneAndDeleteAsync<TProjection>(FilterDefinition<TDocument> filter, FindOneAndDeleteOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TProjection> FindOneAndDeleteAsync<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOneAndDeleteOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public TProjection FindOneAndReplace<TProjection>(FilterDefinition<TDocument> filter, TDocument replacement, FindOneAndReplaceOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public TProjection FindOneAndReplace<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, FindOneAndReplaceOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TProjection> FindOneAndReplaceAsync<TProjection>(FilterDefinition<TDocument> filter, TDocument replacement, FindOneAndReplaceOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TProjection> FindOneAndReplaceAsync<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, FindOneAndReplaceOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public TProjection FindOneAndUpdate<TProjection>(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, FindOneAndUpdateOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public TProjection FindOneAndUpdate<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, FindOneAndUpdateOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TProjection> FindOneAndUpdateAsync<TProjection>(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, FindOneAndUpdateOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TProjection> FindOneAndUpdateAsync<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, FindOneAndUpdateOptions<TDocument, TProjection> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void InsertMany(IEnumerable<TDocument> documents, InsertManyOptions options, CancellationToken cancellationToken = default) =>
        _documents.AddRange(documents);

    public void InsertMany(IClientSessionHandle session, IEnumerable<TDocument> documents, InsertManyOptions options, CancellationToken cancellationToken = default) =>
        InsertMany(documents, options, cancellationToken);

    public Task InsertManyAsync(IEnumerable<TDocument> documents, InsertManyOptions options, CancellationToken cancellationToken = default)
    {
        InsertMany(documents, options, cancellationToken);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IClientSessionHandle session, IEnumerable<TDocument> documents, InsertManyOptions options, CancellationToken cancellationToken = default) =>
        InsertManyAsync(documents, options, cancellationToken);

    public void InsertOne(TDocument document, InsertOneOptions options, CancellationToken cancellationToken = default) =>
        _documents.Add(document);

    public void InsertOne(IClientSessionHandle session, TDocument document, InsertOneOptions options, CancellationToken cancellationToken = default) =>
        InsertOne(document, options, cancellationToken);

    public Task InsertOneAsync(TDocument document, CancellationToken _cancellationToken) =>
        InsertOneAsync(document, new InsertOneOptions(), _cancellationToken);

    public Task InsertOneAsync(TDocument document, InsertOneOptions options, CancellationToken cancellationToken = default)
    {
        InsertOne(document, options, cancellationToken);
        return Task.CompletedTask;
    }

    public Task InsertOneAsync(IClientSessionHandle session, TDocument document, InsertOneOptions options, CancellationToken cancellationToken = default) =>
        InsertOneAsync(document, options, cancellationToken);

    #pragma warning disable CS0618
    public IAsyncCursor<TResult> MapReduce<TResult>(BsonJavaScript map, BsonJavaScript reduce, MapReduceOptions<TDocument, TResult> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncCursor<TResult> MapReduce<TResult>(IClientSessionHandle session, BsonJavaScript map, BsonJavaScript reduce, MapReduceOptions<TDocument, TResult> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(BsonJavaScript map, BsonJavaScript reduce, MapReduceOptions<TDocument, TResult> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(IClientSessionHandle session, BsonJavaScript map, BsonJavaScript reduce, MapReduceOptions<TDocument, TResult> options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
#pragma warning restore CS0618

    public IFilteredMongoCollection<TDerivedDocument> OfType<TDerivedDocument>()
        where TDerivedDocument : TDocument =>
        throw new NotSupportedException();

    public ReplaceOneResult ReplaceOne(FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions options, CancellationToken cancellationToken = default)
    {
        var replaced = false;
        for (var i = 0; i < _documents.Count; i++)
        {
            _documents[i] = replacement;
            replaced = true;
            break;
        }

        return new ReplaceOneResult.Acknowledged(replaced ? 1 : 0, replaced ? 1 : 0, BsonNull.Value);
    }

    public ReplaceOneResult ReplaceOne(FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions options, CancellationToken cancellationToken = default) =>
        ReplaceOne(filter, replacement, new ReplaceOptions(), cancellationToken);

    public ReplaceOneResult ReplaceOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions options, CancellationToken cancellationToken = default) =>
        ReplaceOne(filter, replacement, options, cancellationToken);

    public ReplaceOneResult ReplaceOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions options, CancellationToken cancellationToken = default) =>
        ReplaceOne(filter, replacement, new ReplaceOptions(), cancellationToken);

    public Task<ReplaceOneResult> ReplaceOneAsync(FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReplaceOne(filter, replacement, options, cancellationToken));

    public Task<ReplaceOneResult> ReplaceOneAsync(FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReplaceOne(filter, replacement, new ReplaceOptions(), cancellationToken));

    public Task<ReplaceOneResult> ReplaceOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReplaceOne(session, filter, replacement, options, cancellationToken));

    public Task<ReplaceOneResult> ReplaceOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReplaceOne(session, filter, replacement, new ReplaceOptions(), cancellationToken));

    public UpdateResult UpdateMany(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public UpdateResult UpdateMany(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<UpdateResult> UpdateManyAsync(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<UpdateResult> UpdateManyAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public UpdateResult UpdateOne(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public UpdateResult UpdateOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<UpdateResult> UpdateOneAsync(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<UpdateResult> UpdateOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IChangeStreamCursor<TResult> Watch<TResult>(PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline, ChangeStreamOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IChangeStreamCursor<TResult> Watch<TResult>(IClientSessionHandle session, PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline, ChangeStreamOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline, ChangeStreamOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(IClientSessionHandle session, PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline, ChangeStreamOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IMongoCollection<TDocument> WithReadConcern(ReadConcern readConcern) => this;
    public IMongoCollection<TDocument> WithReadPreference(ReadPreference readPreference) => this;
    public IMongoCollection<TDocument> WithWriteConcern(WriteConcern writeConcern) => this;
}
