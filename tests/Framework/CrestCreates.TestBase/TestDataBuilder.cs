using System;
using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using AutoFixture.AutoMoq;

namespace CrestCreates.TestBase
{
    public class TestDataBuilder<T>
    {
        private readonly IFixture _fixture;
        private readonly List<Action<T>> _configurations = new List<Action<T>>();

        public TestDataBuilder()
        {
            _fixture = new Fixture().Customize(new AutoMoqCustomization());
        }

        public TestDataBuilder<T> With(Action<T> configuration)
        {
            _configurations.Add(configuration);
            return this;
        }

        public TestDataBuilder<T> With<TProperty>(System.Linq.Expressions.Expression<Func<T, TProperty>> propertySelector, TProperty value)
        {
            _configurations.Add(instance =>
            {
                // 使用表达式树来获取属性名
                var propertyName = GetPropertyName(propertySelector);
                var propertyInfo = typeof(T).GetProperty(propertyName);
                if (propertyInfo != null)
                {
                    propertyInfo.SetValue(instance, value);
                }
            });
            return this;
        }

        private string GetPropertyName<TProperty>(System.Linq.Expressions.Expression<Func<T, TProperty>> propertySelector)
        {
            var body = propertySelector.Body;
            if (body is System.Linq.Expressions.MemberExpression memberExpression)
            {
                return memberExpression.Member.Name;
            }
            throw new ArgumentException("Property selector must be a simple property access expression");
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

        public List<T> BuildList(int count = 3)
        {
            return BuildMany(count).ToList();
        }

        public TestDataBuilder<T> WithRandomValues()
        {
            // 使用AutoFixture的默认随机值生成
            return this;
        }

        public TestDataBuilder<T> WithSeed(int seed)
        {
            _fixture.RepeatCount = seed;
            return this;
        }
    }

    public static class TestDataBuilder
    {
        public static TestDataBuilder<T> For<T>()
        {
            return new TestDataBuilder<T>();
        }

        public static TestDataBuilder<T> For<T>(Action<TestDataBuilder<T>> configure)
        {
            var builder = new TestDataBuilder<T>();
            configure(builder);
            return builder;
        }

        public static T Build<T>(Action<TestDataBuilder<T>>? configure = null)
        {
            var builder = new TestDataBuilder<T>();
            configure?.Invoke(builder);
            return builder.Build();
        }

        public static IEnumerable<T> BuildMany<T>(int count = 3, Action<TestDataBuilder<T>>? configure = null)
        {
            var builder = new TestDataBuilder<T>();
            configure?.Invoke(builder);
            return builder.BuildMany(count);
        }

        public static List<T> BuildList<T>(int count = 3, Action<TestDataBuilder<T>>? configure = null)
        {
            var builder = new TestDataBuilder<T>();
            configure?.Invoke(builder);
            return builder.BuildList(count);
        }
    }
}