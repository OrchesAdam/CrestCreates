using System.Collections.Generic;
using System.Linq;
using Xunit;
using CrestCreates.TestBase;

namespace CrestCreates.TestBase.Tests
{
    public class TestDataBuilderTests
    {
        public class TestEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
        }

        [Fact]
        public void Build_ShouldCreateInstanceWithRandomValues()
        {
            // Arrange
            var builder = TestDataBuilder.For<TestEntity>();

            // Act
            var entity = builder.Build();

            // Assert
            Assert.NotNull(entity);
            Assert.NotEqual(0, entity.Id);
            Assert.NotNull(entity.Name);
            Assert.NotEqual(0, entity.Age);
        }

        [Fact]
        public void With_ShouldSetPropertyValue()
        {
            // Arrange
            var expectedName = "Test Name";
            var expectedAge = 25;

            // Act
            var entity = TestDataBuilder.For<TestEntity>()
                .With(e => e.Name, expectedName)
                .With(e => e.Age, expectedAge)
                .Build();

            // Assert
            Assert.NotNull(entity);
            Assert.Equal(expectedName, entity.Name);
            Assert.Equal(expectedAge, entity.Age);
        }

        [Fact]
        public void BuildMany_ShouldCreateMultipleInstances()
        {
            // Arrange
            var count = 5;

            // Act
            var entities = TestDataBuilder.For<TestEntity>().BuildMany(count);

            // Assert
            Assert.NotNull(entities);
            Assert.Equal(count, entities.Count());
            foreach (var entity in entities)
            {
                Assert.NotNull(entity);
            }
        }

        [Fact]
        public void BuildList_ShouldCreateListOfInstances()
        {
            // Arrange
            var count = 3;

            // Act
            var entities = TestDataBuilder.For<TestEntity>().BuildList(count);

            // Assert
            Assert.NotNull(entities);
            Assert.IsType<List<TestEntity>>(entities);
            Assert.Equal(count, entities.Count);
        }

        [Fact]
        public void StaticBuild_ShouldCreateInstance()
        {
            // Act
            var entity = TestDataBuilder.Build<TestEntity>();

            // Assert
            Assert.NotNull(entity);
        }

        [Fact]
        public void StaticBuildWithConfigure_ShouldCreateInstanceWithConfiguredValues()
        {
            // Arrange
            var expectedName = "Configured Name";

            // Act
            var entity = TestDataBuilder.Build<TestEntity>(builder =>
            {
                builder.With(e => e.Name, expectedName);
            });

            // Assert
            Assert.NotNull(entity);
            Assert.Equal(expectedName, entity.Name);
        }
    }
}
