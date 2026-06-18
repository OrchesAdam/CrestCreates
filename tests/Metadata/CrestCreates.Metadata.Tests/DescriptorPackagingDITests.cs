using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackagingDITests
{
    [Fact]
    public void AddDescriptorPackaging_RegistersBuilder()
    {
        var services = new ServiceCollection();
        services.AddDescriptorPackaging();
        var provider = services.BuildServiceProvider();

        var builder = provider.GetService<IDescriptorPackageBuilder>();
        builder.Should().NotBeNull();
    }

    [Fact]
    public void AddDescriptorPackaging_RegistersDiffer()
    {
        var services = new ServiceCollection();
        services.AddDescriptorPackaging();
        var provider = services.BuildServiceProvider();

        var differ = provider.GetService<IDescriptorPackageDiffer>();
        differ.Should().NotBeNull();
    }

    [Fact]
    public void AddDescriptorPackaging_RegistersSerializer()
    {
        var services = new ServiceCollection();
        services.AddDescriptorPackaging();
        var provider = services.BuildServiceProvider();

        var serializer = provider.GetService<IDescriptorPackageSerializer>();
        serializer.Should().NotBeNull();
    }
}
