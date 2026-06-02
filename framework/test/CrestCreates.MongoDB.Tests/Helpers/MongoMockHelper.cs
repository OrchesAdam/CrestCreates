using System.Collections.Generic;
using MongoDB.Driver;
using Moq;

namespace CrestCreates.MongoDB.Tests.Helpers;

/// <summary>
/// Helper for creating in-memory MongoDB collections for testing.
/// Uses InMemoryMongoCollection (real implementation) + Moq IMongoDatabase.
/// </summary>
public static class MongoMockHelper
{
    /// <summary>
    /// Creates an in-memory IMongoCollection{T} pre-populated with the given entities.
    /// </summary>
    public static InMemoryMongoCollection<T> CreateCollection<T>(List<T>? entities = null)
        where T : class
    {
        return new InMemoryMongoCollection<T>(entities ?? new List<T>());
    }

    /// <summary>
    /// Creates a Mock IMongoDatabase that returns the given InMemoryMongoCollection from GetCollection.
    /// </summary>
    public static Mock<IMongoDatabase> CreateDatabaseMock<T>(InMemoryMongoCollection<T> collection)
        where T : class
    {
        var database = new Mock<IMongoDatabase>();
        database.Setup(d => d.GetCollection<T>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(collection);
        return database;
    }
}
