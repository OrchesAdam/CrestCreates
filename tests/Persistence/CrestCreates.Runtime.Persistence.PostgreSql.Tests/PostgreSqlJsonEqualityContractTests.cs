using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public sealed class PostgreSqlJsonEqualityContractTests
{
    [Fact]
    public void Object_property_reordering_is_semantically_equal()
        => PostgreSqlRuntimeStoreSupport.JsonEquals("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}").Should().BeTrue();

    [Fact]
    public void Object_value_change_is_not_semantically_equal()
        => PostgreSqlRuntimeStoreSupport.JsonEquals("{\"a\":1,\"b\":2}", "{\"a\":1,\"b\":3}").Should().BeFalse();

    [Fact]
    public void Array_reordering_is_not_semantically_equal()
        => PostgreSqlRuntimeStoreSupport.JsonEquals("[1,2]", "[2,1]").Should().BeFalse();
}
