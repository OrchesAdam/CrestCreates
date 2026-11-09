using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Caching.Interceptors;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace CrestCreates.Infrastructure.Tests.Caching;

public class CacheBehaviorTests
{
    [Fact]
    public async Task Pattern_removal_is_anchored_per_tenant_and_supports_wildcards()
    {
        var options = new CacheOptions { Prefix = NewPrefix() };
        var cache = new CrestMemoryCache(options);
        await cache.SetAsync("Scope:User:user-1:tenant-a:Name", "a");
        await cache.SetAsync("Scope:User:user-1:tenant-ab:Name", "ab");
        await cache.SetAsync("Scope:User:user-1:tenant-b:Name", "b");

        await cache.RemoveByPatternAsync("Scope:User:*:tenant-a:*");

        (await cache.ExistsAsync("Scope:User:user-1:tenant-a:Name")).Should().BeFalse();
        (await cache.ExistsAsync("Scope:User:user-1:tenant-ab:Name")).Should().BeTrue(
            "a tenant prefix must not match a longer sibling tenant id");
        (await cache.ExistsAsync("Scope:User:user-1:tenant-b:Name")).Should().BeTrue();
        await cache.ClearAsync();
    }

    [Fact]
    public async Task Pattern_removal_treats_regex_metacharacters_as_literal_and_questions_as_single_characters()
    {
        var options = new CacheOptions { Prefix = NewPrefix() };
        var cache = new CrestMemoryCache(options);
        await cache.SetAsync("literal(a.b)+[x]", "literal");
        await cache.SetAsync("literalXaYbZ[x]", "decoy");
        await cache.SetAsync("q:ab", "ab");
        await cache.SetAsync("q:ac", "ac");
        await cache.SetAsync("q:a", "a");

        await cache.RemoveByPatternAsync("literal(a.b)+[x]");
        await cache.RemoveByPatternAsync("q:a?");

        (await cache.ExistsAsync("literal(a.b)+[x]")).Should().BeFalse();
        (await cache.ExistsAsync("literalXaYbZ[x]")).Should().BeTrue();
        (await cache.ExistsAsync("q:ab")).Should().BeFalse();
        (await cache.ExistsAsync("q:ac")).Should().BeFalse();
        (await cache.ExistsAsync("q:a")).Should().BeTrue();
        await cache.ClearAsync();
    }

    [Theory]
    [InlineData("?")]
    [InlineData("*")]
    [InlineData("[a]")]
    [InlineData("\\")]
    public async Task Configured_prefix_is_literal_namespace_ownership_not_a_glob(string metacharacter)
    {
        var root = $"Crest-test-{Guid.NewGuid():N}";
        var owner = new CrestMemoryCache(new CacheOptions { Prefix = root + metacharacter + ":" });
        var sibling = new CrestMemoryCache(new CacheOptions { Prefix = root + "X:" });
        await owner.SetAsync("key", "owner");
        await sibling.SetAsync("key", "sibling");
        try
        {
            await owner.RemoveByPatternAsync("*");

            (await owner.ExistsAsync("key")).Should().BeFalse(
                "the pattern removal must still remove the instance's own keys");
            (await sibling.ExistsAsync("key")).Should().BeTrue(
                "characters in the configured prefix are literal namespace ownership, never wildcards");
            (await owner.GetCountAsync()).Should().Be(0);
            (await sibling.GetCountAsync()).Should().Be(1);

            await owner.SetAsync("key", "owner-again");
            await owner.ClearAsync();

            (await owner.ExistsAsync("key")).Should().BeFalse();
            (await sibling.ExistsAsync("key")).Should().BeTrue(
                "clear must also treat the configured prefix literally");
        }
        finally
        {
            await owner.ClearAsync();
            await sibling.ClearAsync();
        }
    }

    [Fact]
    public async Task Pattern_escape_makes_wildcard_characters_literal()
    {
        var cache = new CrestMemoryCache(new CacheOptions { Prefix = NewPrefix() });
        await cache.SetAsync("a*b", "literal-star");
        await cache.SetAsync("aXb", "wildcard-star");
        await cache.SetAsync(@"path\name", "literal-backslash");

        await cache.RemoveByPatternAsync(@"a\*b");
        await cache.RemoveByPatternAsync(@"path\\name");

        (await cache.ExistsAsync("a*b")).Should().BeFalse();
        (await cache.ExistsAsync("aXb")).Should().BeTrue("an escaped star must not act as a wildcard");
        (await cache.ExistsAsync(@"path\name")).Should().BeFalse("an escaped backslash must stay literal");
        await cache.ClearAsync();
    }

    [Fact]
    public async Task Clear_only_removes_keys_under_the_configured_prefix()
    {
        var first = new CrestMemoryCache(new CacheOptions { Prefix = NewPrefix() });
        var second = new CrestMemoryCache(new CacheOptions { Prefix = NewPrefix() });
        await first.SetAsync("key", "first");
        await second.SetAsync("key", "second");

        await first.ClearAsync();

        (await first.ExistsAsync("key")).Should().BeFalse();
        (await second.ExistsAsync("key")).Should().BeTrue();
        await second.ClearAsync();
    }

    [Fact]
    public async Task Tenant_cache_writes_carry_the_interceptor_expiration()
    {
        var cache = new RecordingCrestCache();
        using var host = BuildHost(cache, "tenant-a");
        var probe = host.Services.GetRequiredService<CacheProbeService>();

        await probe.WithExplicitTtlAsync("k");

        cache.SetCount.Should().Be(1);
        cache.LastKey.Should().Contain("tenant-a");
        cache.LastExpiration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Tenant_cache_writes_carry_the_configured_default_expiration()
    {
        var cache = new RecordingCrestCache();
        using var host = BuildHost(
            cache,
            "tenant-a",
            options => options.DefaultExpiration = TimeSpan.FromMinutes(45));
        var probe = host.Services.GetRequiredService<CacheProbeService>();

        await probe.WithDefaultTtlAsync("k");

        cache.LastExpiration.Should().Be(TimeSpan.FromMinutes(45));
    }

    [Fact]
    public void AddCrestCaching_exposes_one_cache_options_source()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestCaching();
        services.Configure<CacheOptions>(options =>
        {
            options.DefaultExpiration = TimeSpan.FromMinutes(7);
            options.Prefix = "Configured:";
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<CacheOptions>().DefaultExpiration.Should().Be(TimeSpan.FromMinutes(7));
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CacheOptions>>()
            .Value.DefaultExpiration.Should().Be(TimeSpan.FromMinutes(7));
        provider.GetRequiredService<ICrestCache>().Should().BeOfType<CrestMemoryCache>();
    }

    private static IHost BuildHost(
        ICrestCache cache,
        string? tenantId,
        Action<CacheOptions>? configure = null)
    {
        return Host.CreateDefaultBuilder()
            .UsePinnedScopeServiceProvider()
            .ConfigureServices(services =>
            {
                services.AddSingleton(cache);
                services.AddCrestCaching();
                services.Configure<CacheOptions>(options =>
                {
                    options.Enabled = true;
                    configure?.Invoke(options);
                });
                var currentTenant = new Mock<ICurrentTenant>();
                currentTenant.SetupGet(tenant => tenant.Id).Returns(tenantId);
                services.AddSingleton(currentTenant.Object);
                services.AddSingleton<CacheProbeService>();
            })
            .Build();
    }

    private static string NewPrefix() => $"Crest-test-{Guid.NewGuid():N}:";

    public sealed class CacheProbeService
    {
        [CacheMo("probe-explicit", expirationMinutes: 2)]
        public Task<string> WithExplicitTtlAsync(string key)
        {
            return Task.FromResult("value");
        }

        [CacheMo("probe-default")]
        public Task<string> WithDefaultTtlAsync(string key)
        {
            return Task.FromResult("value");
        }
    }

    private sealed class RecordingCrestCache : ICrestCache
    {
        public string? LastKey { get; private set; }

        public TimeSpan? LastExpiration { get; private set; }

        public int SetCount { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(default(T?));
        }

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            LastKey = key;
            LastExpiration = expiration;
            SetCount++;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }
    }
}
