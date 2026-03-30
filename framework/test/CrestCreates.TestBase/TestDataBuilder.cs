using System;
using System.Collections.Generic;
using System.Linq;
using AutoFixture;

namespace CrestCreates.TestBase
{
    public class TestDataBuilder<T>
    {
        private readonly IFixture _fixture;
        private readonly List<Action<T>> _configurations = new List<Action<T>>();

        public TestDataBuilder()
        {
            _fixture = new Fixture();
        }

        public TestDataBuilder<T> With(Action<T> configuration)
        {
            _configurations.Add(configuration);
            return this;
        }

        public T Build()
        {
            var instance = _fixture.Create<T>();
            foreach (var configuration in _configurations)
            {
                configuration(instance);
            }
            return instance;
        }

        public IEnumerable<T> BuildMany(int count = 3)
        {
            return Enumerable.Range(0, count).Select(_ => Build());
        }
    }

    public static class TestDataBuilder
    {
        public static TestDataBuilder<T> For<T>()
        {
            return new TestDataBuilder<T>();
        }
    }
}