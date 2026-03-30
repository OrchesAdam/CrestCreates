using System;
using System.Reflection;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching.Interceptors;

namespace CrestCreates.Infrastructure.Tests
{
    public class CacheKeyExpressionParserTests
    {
        private readonly CacheKeyExpressionParser _parser;

        public CacheKeyExpressionParserTests()
        {
            _parser = new CacheKeyExpressionParser();
        }

        [Fact]
        public void Parse_Should_Return_Empty_String_When_Expression_Is_Empty()
        {
            var result = _parser.Parse("", Array.Empty<object>(), Array.Empty<ParameterInfo>());
            result.Should().BeEmpty();
        }

        [Fact]
        public void Parse_Should_Return_Expression_When_No_Parameters()
        {
            var expression = "static-key";
            var result = _parser.Parse(expression, Array.Empty<object>(), Array.Empty<ParameterInfo>());
            result.Should().Be(expression);
        }

        [Fact]
        public void Parse_Should_Replace_Parameter_With_Value()
        {
            var expression = "key-#id";
            var args = new object[] { 123 };
            var parameters = new[] { CreateParameter("id", typeof(int)) };

            var result = _parser.Parse(expression, args, parameters);

            result.Should().Be("key-123");
        }

        [Fact]
        public void Parse_Should_Replace_Multiple_Parameters()
        {
            var expression = "product-#id-category-#categoryId";
            var args = new object[] { 123, 456 };
            var parameters = new[]
            {
                CreateParameter("id", typeof(int)),
                CreateParameter("categoryId", typeof(int))
            };

            var result = _parser.Parse(expression, args, parameters);

            result.Should().Be("product-123-category-456");
        }

        [Fact]
        public void Parse_Should_Replace_Parameter_Property()
        {
            var expression = "user-#user.Name";
            var user = new User { Name = "John" };
            var args = new object[] { user };
            var parameters = new[] { CreateParameter("user", typeof(User)) };

            var result = _parser.Parse(expression, args, parameters);

            result.Should().Be("user-John");
        }

        [Fact]
        public void Parse_Should_Handle_Null_Parameter_Value()
        {
            var expression = "key-#id";
            var args = new object[] { null! };
            var parameters = new[] { CreateParameter("id", typeof(int?)) };

            var result = _parser.Parse(expression, args, parameters);

            result.Should().Be("key-null");
        }

        [Fact]
        public void Parse_Should_Ignore_Unknown_Parameters()
        {
            var expression = "key-#unknown";
            var args = new object[] { 123 };
            var parameters = new[] { CreateParameter("id", typeof(int)) };

            var result = _parser.Parse(expression, args, parameters);

            result.Should().Be("key-#unknown");
        }

        [Fact]
        public void EvaluateCondition_Should_Return_True_When_Condition_Is_Empty()
        {
            var result = _parser.EvaluateCondition(null, Array.Empty<object>(), Array.Empty<ParameterInfo>());
            result.Should().BeTrue();
        }

        [Fact]
        public void EvaluateCondition_Should_Return_True_When_Condition_Is_Not_False()
        {
            var result = _parser.EvaluateCondition("true", Array.Empty<object>(), Array.Empty<ParameterInfo>());
            result.Should().BeTrue();
        }

        [Fact]
        public void EvaluateCondition_Should_Return_False_When_Condition_Is_False()
        {
            var result = _parser.EvaluateCondition("false", Array.Empty<object>(), Array.Empty<ParameterInfo>());
            result.Should().BeFalse();
        }

        [Fact]
        public void EvaluateCondition_Should_Return_False_When_Condition_Is_Zero()
        {
            var result = _parser.EvaluateCondition("0", Array.Empty<object>(), Array.Empty<ParameterInfo>());
            result.Should().BeFalse();
        }

        [Fact]
        public void EvaluateCondition_Should_Return_False_When_Condition_Is_Null()
        {
            var expression = "#id";
            var args = new object[] { null! };
            var parameters = new[] { CreateParameter("id", typeof(int?)) };

            var result = _parser.EvaluateCondition(expression, args, parameters);

            result.Should().BeFalse();
        }

        [Fact]
        public void EvaluateCondition_Should_Replace_Parameters_In_Condition()
        {
            var expression = "#id";
            var args = new object[] { 123 };
            var parameters = new[] { CreateParameter("id", typeof(int)) };

            var result = _parser.EvaluateCondition(expression, args, parameters);

            result.Should().BeTrue();
        }

        private static ParameterInfo CreateParameter(string name, Type type)
        {
            var method = typeof(TestMethods).GetMethod(nameof(TestMethods.TestMethod))!;
            var param = method.GetParameters()[0];
            return new FakeParameterInfo(name, type);
        }

        private class User
        {
            public string Name { get; set; } = string.Empty;
        }

        private class TestMethods
        {
            public void TestMethod(int param) { }
        }

        private class FakeParameterInfo : ParameterInfo
        {
            private readonly string _name;
            private readonly Type _type;

            public FakeParameterInfo(string name, Type type)
            {
                _name = name;
                _type = type;
            }

            public override string Name => _name;
            public override Type ParameterType => _type;
        }
    }
}
