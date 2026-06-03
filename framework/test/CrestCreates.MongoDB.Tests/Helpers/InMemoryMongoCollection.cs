using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace CrestCreates.MongoDB.Tests.Helpers;

/// <summary>
/// In-memory IMongoCollection implementation for MongoDB.Driver 3.x testing.
/// Since Find/FindAsync are extension methods in v3, Moq cannot mock them.
/// This provides a real in-memory implementation of the collection interface.
/// </summary>
public class InMemoryMongoCollection<TDocument> : IMongoCollection<TDocument>
    where TDocument : class
{
    private readonly List<TDocument> _documents = new();

    public InMemoryMongoCollection(IEnumerable<TDocument>? initialDocuments = null)
    {
        if (initialDocuments != null)
            _documents.AddRange(initialDocuments);
    }

    public CollectionNamespace CollectionNamespace => new("test", "test");
    public IBsonSerializer<TDocument> DocumentSerializer => BsonSerializer.LookupSerializer<TDocument>();
    public MongoCollectionSettings Settings => new();
    public IAsyncCursor<TDocument> Aggregate<TProjection>(PipelineDefinition<TDocument, TProjection> pipeline, AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public IAsyncCursor<TDocument> Aggregate<TProjection>(IClientSessionHandle session, PipelineDefinition<TDocument, TProjection> pipeline, AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<IAsyncCursor<TDocument>> AggregateAsync<TProjection>(PipelineDefinition<TDocument, TProjection> pipeline, AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<IAsyncCursor<TDocument>> AggregateAsync<TProjection>(IClientSessionHandle session, PipelineDefinition<TDocument, TProjection> pipeline, AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public void Drop(IndexModelDefinition<TDocument> model, DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public void DropIndex(IndexKeysDefinition<TDocument> keys, DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public void DropIndex(string indexName, DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public void DropIndexAll(DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task DropIndexAsync(IndexModelDefinition<TDocument> model, DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task DropIndexAsync(IndexKeysDefinition<TDocument> keys, DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task DropIndexAsync(string indexName, DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task DropIndexAllAsync(DropIndexOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public long EstimatedDocumentCount(EstimatedDocumentCountOptions? options = null, CancellationToken cancellationToken = default) =>
        _documents.Count;
    public Task<long> EstimatedDocumentCountAsync(EstimatedDocumentCountOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult((long)_documents.Count);
    public IAsyncCursor<TField> Distinct<TField>(FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<IAsyncCursor<TField>> DistinctAsync<TField>(FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public IAsyncCursor<TField> Distinct<TField>(IClientSessionHandle session, FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<IAsyncCursor<TField>> DistinctAsync<TField>(IClientSessionHandle session, FieldDefinition<TDocument, TField> field, FilterDefinition<TDocument> filter, DistinctOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public void DropMany(FilterDefinition<TDocument> filter, DropOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task DropManyAsync(FilterDefinition<TDocument> filter, DropOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public void DropMany(IClientSessionHandle session, FilterDefinition<TDocument> filter, DropOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task DropManyAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, DropOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public void DropOne(DropOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task DropOneAsync(DropOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public void DropOne(IClientSessionHandle session, DropOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task DropOneAsync(IClientSessionHandle session, DropOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public void InsertMany(IEnumerable<TDocument> documents, InsertManyOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.AddRange(documents);
    }
    public Task InsertManyAsync(IEnumerable<TDocument> documents, InsertManyOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.AddRange(documents);
        return Task.CompletedTask;
    }
    public void InsertMany(IClientSessionHandle session, IEnumerable<TDocument> documents, InsertManyOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.AddRange(documents);
    }
    public Task InsertManyAsync(IClientSessionHandle session, IEnumerable<TDocument> documents, InsertManyOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.AddRange(documents);
        return Task.CompletedTask;
    }
    public void InsertOne(TDocument document, InsertOneOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.Add(document);
    }
    public Task InsertOneAsync(TDocument document, InsertOneOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.Add(document);
        return Task.CompletedTask;
    }
    public void InsertOne(IClientSessionHandle session, TDocument document, InsertOneOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.Add(document);
    }
    public Task InsertOneAsync(IClientSessionHandle session, TDocument document, InsertOneOptions? options = null, CancellationToken cancellationToken = default)
    {
        _documents.Add(document);
        return Task.CompletedTask;
    }
    public IAsyncCursor<TDocument> MapReduce(BsonJavaScript mapFunction, BsonJavaScript reduceFunction, MapReduceOptions<TDocument>? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<IAsyncCursor<TDocument>> MapReduceAsync(BsonJavaScript mapFunction, BsonJavaScript reduceFunction, MapReduceOptions<TDocument>? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public IAsyncCursor<TDocument> MapReduce<TProjection>(BsonJavaScript mapFunction, BsonJavaScript reduceFunction, MapReduceOptions<TProjection>? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<IAsyncCursor<TDocument>> MapReduceAsync<TProjection>(BsonJavaScript mapFunction, BsonJavaScript reduceFunction, MapReduceOptions<TProjection>? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public ReplaceOneResult ReplaceOne(FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions? options = null, CancellationToken cancellationToken = default)
    {
        var replaced = false;
        for (int i = 0; i < _documents.Count; i++)
        {
            // Simple replacement: just replace the first match
            _documents[i] = replacement;
            replaced = true;
            break;
        }
        return replaced
            ? ReplaceOneResult.Acknowledged(1, 1, replacement)
            : ReplaceOneResult.NotAcknowledged();
    }
    public Task<ReplaceOneResult> ReplaceOneAsync(FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ReplaceOne(filter, replacement, options, cancellationToken));
    }
    public ReplaceOneResult ReplaceOne(FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ReplaceOne(filter, replacement, new ReplaceOptions(), cancellationToken);
    }
    public Task<ReplaceOneResult> ReplaceOneAsync(FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ReplaceOne(filter, replacement, new ReplaceOptions(), cancellationToken));
    }
    public ReplaceOneResult ReplaceOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ReplaceOne(filter, replacement, options, cancellationToken);
    }
    public Task<ReplaceOneResult> ReplaceOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ReplaceOne(session, filter, replacement, options, cancellationToken));
    }
    public ReplaceOneResult ReplaceOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ReplaceOne(filter, replacement, new ReplaceOptions(), cancellationToken);
    }
    public Task<ReplaceOneResult> ReplaceOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, TDocument replacement, UpdateOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ReplaceOne(session, filter, replacement, new ReplaceOptions(), cancellationToken));
    }
    public UpdateResult UpdateMany(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<UpdateResult> UpdateManyAsync(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public UpdateResult UpdateMany(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<UpdateResult> UpdateManyAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public UpdateResult UpdateOne(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<UpdateResult> UpdateOneAsync(FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public UpdateResult UpdateOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<UpdateResult> UpdateOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, UpdateOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public DeleteResult DeleteMany(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        var count = _documents.Count;
        _documents.Clear();
        return DeleteResult.Acknowledged(count);
    }
    public Task<DeleteResult> DeleteManyAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteMany(filter, cancellationToken));
    }
    public DeleteResult DeleteMany(IClientSessionHandle session, FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        return DeleteMany(filter, cancellationToken);
    }
    public Task<DeleteResult> DeleteManyAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteMany(session, filter, cancellationToken));
    }
    public DeleteResult DeleteOne(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        var removed = _documents.Count > 0;
        if (removed) _documents.RemoveAt(0);
        return removed
            ? DeleteResult.Acknowledged(1)
            : DeleteResult.NotAcknowledged();
    }
    public Task<DeleteResult> DeleteOneAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteOne(filter, cancellationToken));
    }
    public DeleteResult DeleteOne(IClientSessionHandle session, FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        return DeleteOne(filter, cancellationToken);
    }
    public Task<DeleteResult> DeleteOneAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteOne(session, filter, cancellationToken));
    }
    public IAsyncCursor<TDocument> Find(FilterDefinition<TDocument> filter, FindOptions<TDocument, TDocument>? options = null, CancellationToken cancellationToken = default)
    {
        return new InMemoryAsyncCursor<TDocument>(_documents.ToList());
    }
    public Task<IAsyncCursor<TDocument>> FindAsync(FilterDefinition<TDocument> filter, FindOptions<TDocument, TDocument>? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Find(filter, options, cancellationToken));
    }
    public IAsyncCursor<TDocument> Find(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions<TDocument, TDocument>? options = null, CancellationToken cancellationToken = default)
    {
        return Find(filter, options, cancellationToken);
    }
    public Task<IAsyncCursor<TDocument>> FindAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions<TDocument, TDocument>? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Find(session, filter, options, cancellationToken));
    }
    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter, FindOptions? options = null)
    {
        return new InMemoryFindFluent<TDocument, TDocument>(_documents.ToList());
    }
    public IFindFluent<TDocument, TDocument> Find(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions? options = null)
    {
        return Find(filter, options);
    }
    public long CountDocuments(FilterDefinition<TDocument> filter, CountOptions? options = null, CancellationToken cancellationToken = default)
    {
        return _documents.Count;
    }
    public Task<long> CountDocumentsAsync(FilterDefinition<TDocument> filter, CountOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CountDocuments(filter, options, cancellationToken));
    }
    public long CountDocuments(IClientSessionHandle session, FilterDefinition<TDocument> filter, CountOptions? options = null, CancellationToken cancellationToken = default)
    {
        return CountDocuments(filter, options, cancellationToken);
    }
    public Task<long> CountDocumentsAsync(IClientSessionHandle session, FilterDefinition<TDocument> filter, CountOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CountDocuments(session, filter, options, cancellationToken));
    }
    public BulkWriteResult<TDocument> BulkWrite(IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public BulkWriteResult<TDocument> BulkWrite(IClientSessionHandle session, IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(IClientSessionHandle session, IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public IMongoCollection<TDocument> WithReadPreference(ReadPreference readPreference) => this;
    public IMongoCollection<TDocument> WithWriteConcern(WriteConcern writeConcern) => this;

    public IFindFluent<TDocument, TProjection> Find<TProjection>(FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection>? options = null) =>
        new InMemoryFindFluent<TDocument, TProjection>(_documents.ToList());
    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection>? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncCursor<TProjection>>(new InMemoryAsyncCursor<TProjection>(_documents.Cast<TProjection>().ToList()));
    public IFindFluent<TDocument, TProjection> Find<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection>? options = null) =>
        Find(filter, options);
    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(IClientSessionHandle session, FilterDefinition<TDocument> filter, FindOptions<TDocument, TProjection>? options = null, CancellationToken cancellationToken = default) =>
        FindAsync(filter, options, cancellationToken);
}
