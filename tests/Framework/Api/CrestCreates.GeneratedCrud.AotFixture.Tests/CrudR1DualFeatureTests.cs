using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.Json;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Middleware;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.DynamicApi;
using CrestCreates.GeneratedCrud.AotFixture.Domain;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

/// <summary>
/// PR150-R1-CRUD128-01 acceptance: two REAL generated Feature assemblies (the main Feature and
/// SecondFeature, each with its own source-generated JSON context and the framework-shared
/// void/error roots) must activate together in both orders on one host — every per-feature request
/// and response travels its own generated chain, and the shared roots are answered by the host's
/// activated source-generated contributors instead of failing one of the features.
/// </summary>
public class CrudR1DualFeatureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_generated_features_must_serve_their_own_wire_in_both_activation_orders(bool widgetFirst)
    {
        await using var app = BuildDualFeatureHost(widgetFirst);
        await app.StartAsync();
        try
        {
            var baseUrl = ResolveBaseUrl(app);
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Widget feature: create + search through its own generated chain.
            var widgetName = "dual-widget-" + Guid.NewGuid().ToString("N")[..8];
            var widgetCreate = await SendAsync(client, HttpMethod.Post, "/api/widgets",
                $$"""{"name":"{{widgetName}}","price":1.5,"description":null}""");
            widgetCreate.Status.Should().Be(200, $"order widgetFirst={widgetFirst}: {widgetCreate.Body}");
            ReadData(widgetCreate).GetProperty("name").GetString().Should().Be(widgetName);

            var widgetSearch = await SendAsync(client, HttpMethod.Post, "/api/widgets/search",
                $$"""{"pageIndex":0,"pageSize":5,"filters":[{"field":"Name","operator":2,"value":"{{widgetName}}"}]}""");
            widgetSearch.Status.Should().Be(200, widgetSearch.Body);
            ReadData(widgetSearch).GetProperty("totalCount").GetInt32().Should().Be(1);

            // Gadget feature: its own create + search with its own DTO/envelope shape.
            var gadgetName = "dual-gadget-" + Guid.NewGuid().ToString("N")[..8];
            var gadgetCreate = await SendAsync(client, HttpMethod.Post, "/api/gadgets",
                $$"""{"name":"{{gadgetName}}","price":2.5}""");
            gadgetCreate.Status.Should().Be(200, $"order widgetFirst={widgetFirst}: {gadgetCreate.Body}");
            var gadgetData = ReadData(gadgetCreate);
            gadgetData.GetProperty("name").GetString().Should().Be(gadgetName);
            gadgetData.TryGetProperty("concurrencyStamp", out _).Should().BeFalse(
                "the Gadget read contract has no concurrency stamp, so the two features' DTO shapes stay distinct");

            var gadgetSearch = await SendAsync(client, HttpMethod.Post, "/api/gadgets/search",
                $$"""{"pageIndex":0,"pageSize":5,"filters":[{"field":"Name","operator":2,"value":"{{gadgetName}}"}]}""");
            gadgetSearch.Status.Should().Be(200, gadgetSearch.Body);
            ReadData(gadgetSearch).GetProperty("totalCount").GetInt32().Should().Be(1);

            // Framework-shared roots under co-activation: each feature's failure path writes
            // ProblemDetails through the shared root the other feature's context also declares.
            var widgetFailure = await SendAsync(client, HttpMethod.Post, "/api/widgets/search",
                """{"pageIndex":0,"pageSize":5,"filters":[{"field":"NoSuchField","operator":2,"value":"x"}]}""");
            widgetFailure.Status.Should().Be(500);
            JsonDocument.Parse(widgetFailure.Body).RootElement.GetProperty("detail").GetString()
                .Should().Contain("NoSuchField");

            var gadgetFailure = await SendAsync(client, HttpMethod.Post, "/api/gadgets/search",
                """{"pageIndex":0,"pageSize":5,"filters":[{"field":"NoSuchField","operator":2,"value":"x"}]}""");
            gadgetFailure.Status.Should().Be(500);
            JsonDocument.Parse(gadgetFailure.Body).RootElement.GetProperty("detail").GetString()
                .Should().Contain("NoSuchField");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    /// Both activations are invoked statically (the generated extension methods share one name) in the
    /// requested order; everything else mirrors the real host composition of the fixture.
    /// </summary>
    private static WebApplication BuildDualFeatureHost(bool widgetFirst)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddCapabilityRuntime();
        builder.Services.AddAccountability();
        builder.Services.RemoveAll<ValidationMiddleware>();
        builder.Services.RemoveAll<IBootstrapValidator>();
        builder.Services.RemoveAll<IDescriptorBindingStatusContributor>();
        builder.Services.AddSingleton(new CapabilityPipelineBuilder());
        builder.Services.AddCrestCompatibilityProjection();

        var widgetActivation =
            global::CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_Feature.GeneratedCrudActivation.AddGeneratedCrud;
        var gadgetActivation =
            global::CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_SecondFeature.GeneratedCrudActivation.AddGeneratedCrud;
        if (widgetFirst)
        {
            widgetActivation(builder.Services);
            gadgetActivation(builder.Services);
        }
        else
        {
            gadgetActivation(builder.Services);
            widgetActivation(builder.Services);
        }

        builder.Services.AddSingleton<MemoryWidgetRepository>();
        builder.Services.AddSingleton<ICrestRepositoryBase<Widget, Guid>>(
            serviceProvider => serviceProvider.GetRequiredService<MemoryWidgetRepository>());
        builder.Services.AddSingleton<MemoryGadgetRepository>();
        builder.Services.AddSingleton<ICrestRepositoryBase<Gadget, Guid>>(
            serviceProvider => serviceProvider.GetRequiredService<MemoryGadgetRepository>());
        builder.Services.AddSingleton<IPermissionChecker, FixturePermissionChecker>();
        builder.Services.AddSingleton<ICurrentUser, FixtureCurrentUser>();
        builder.Services.AddSingleton<IDataPermissionFilter, IdentityDataPermissionFilter>();

        var app = builder.Build();
        app.MapCrestCapabilityEndpoints();
        return app;
    }

    private static string ResolveBaseUrl(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault();
        address.Should().NotBeNullOrWhiteSpace();
        return address!.Replace("0.0.0.0", "127.0.0.1").Replace("[::]", "127.0.0.1").TrimEnd('/');
    }

    private static async Task<HttpOutcome> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? json)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        return new HttpOutcome((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static JsonElement ReadData(HttpOutcome outcome)
    {
        using var document = JsonDocument.Parse(outcome.Body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private sealed record HttpOutcome(int Status, string Body);

    /// <summary>Minimal JIT-only memory repository for the second feature's aggregate (no weaving,
    /// no native gate: the dual-order acceptance is about JSON contract co-activation and wire).</summary>
    public sealed class MemoryGadgetRepository : ICrestRepositoryBase<Gadget, Guid>
    {
        private readonly object _sync = new();
        private readonly List<Gadget> _store = new();

        public IQueryable<Gadget> GetQueryable()
        {
            lock (_sync)
            {
                return _store.Select(Clone).ToList().AsQueryable();
            }
        }

        public Task<List<Gadget>> GetListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().ToList());

        public Task<List<Gadget>> GetListAsync(Expression<Func<Gadget, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().Where(predicate).ToList());

        public Task<List<Gadget>> GetListAsync(
            Expression<Func<Gadget, bool>> predicate,
            Expression<Func<Gadget, object>> orderBy,
            bool ascending = true,
            CancellationToken cancellationToken = default)
        {
            var query = GetQueryable().Where(predicate);
            return Task.FromResult((ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy)).ToList());
        }

        public Task<Gadget?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().FirstOrDefault(x => x.Id == id));

        public Task<Gadget?> GetAsync(Expression<Func<Gadget, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().FirstOrDefault(predicate));

        public Task<Gadget> InsertAsync(Gadget entity, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            lock (_sync)
            {
                if (entity.Id == Guid.Empty)
                {
                    entity.Id = Guid.NewGuid();
                }

                if (entity.CreationTime == default)
                {
                    entity.CreationTime = DateTime.UtcNow;
                }

                _store.Add(Clone(entity));
            }

            return Task.FromResult(entity);
        }

        public async Task<IEnumerable<Gadget>> InsertRangeAsync(IEnumerable<Gadget> entities, CancellationToken cancellationToken = default)
        {
            var inserted = new List<Gadget>();
            foreach (var entity in entities)
            {
                inserted.Add(await InsertAsync(entity, cancellationToken));
            }

            return inserted;
        }

        public Task<Gadget> UpdateAsync(Gadget entity, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            lock (_sync)
            {
                var index = _store.FindIndex(x => x.Id == entity.Id);
                if (index < 0)
                {
                    throw new CrestCreates.Domain.Exceptions.CrestConcurrencyException(nameof(Gadget), entity.Id);
                }

                _store[index] = Clone(entity);
            }

            return Task.FromResult(entity);
        }

        public async Task<IEnumerable<Gadget>> UpdateRangeAsync(IEnumerable<Gadget> entities, CancellationToken cancellationToken = default)
        {
            var updated = new List<Gadget>();
            foreach (var entity in entities)
            {
                updated.Add(await UpdateAsync(entity, cancellationToken));
            }

            return updated;
        }

        public Task DeleteAsync(Gadget entity, CancellationToken cancellationToken = default)
            => DeleteAsync(entity.Id, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _store.RemoveAll(x => x.Id == id);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, string expectedStamp, CancellationToken cancellationToken = default)
            => DeleteAsync(id, cancellationToken);

        public async Task DeleteRangeAsync(IEnumerable<Gadget> entities, CancellationToken cancellationToken = default)
        {
            foreach (var entity in entities)
            {
                await DeleteAsync(entity.Id, cancellationToken);
            }
        }

        public Task DeleteRangeAsync(Expression<Func<Gadget, bool>> predicate, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _store.RemoveAll(new Predicate<Gadget>(predicate.Compile()));
            }

            return Task.CompletedTask;
        }

        public Task<PagedResult<Gadget>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
            => Task.FromResult(Page(GetQueryable(), pageIndex, pageSize));

        public Task<PagedResult<Gadget>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<Gadget, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(Page(GetQueryable().Where(predicate), pageIndex, pageSize));

        public Task<PagedResult<Gadget>> GetPagedAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<Gadget, bool>> predicate,
            Expression<Func<Gadget, object>> orderBy,
            bool ascending = true,
            CancellationToken cancellationToken = default)
        {
            var query = GetQueryable().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            return Task.FromResult(Page(query, pageIndex, pageSize));
        }

        public Task<PagedResult<Gadget>> GetPagedAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<Gadget, object>> orderBy,
            bool ascending = true,
            CancellationToken cancellationToken = default)
        {
            var query = GetQueryable();
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            return Task.FromResult(Page(query, pageIndex, pageSize));
        }

        public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((long)GetQueryable().Count());

        public Task<long> GetCountAsync(Expression<Func<Gadget, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult((long)GetQueryable().Count(predicate));

        public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().Any());

        public Task<bool> AnyAsync(Expression<Func<Gadget, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().Any(predicate));

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(GetQueryable().Any(x => x.Id == id));

        private static PagedResult<Gadget> Page(IQueryable<Gadget> query, int pageIndex, int pageSize)
        {
            var totalCount = query.Count();
            var items = query.Skip(pageIndex * pageSize).Take(pageSize).ToList();
            return new PagedResult<Gadget>(items, totalCount, pageIndex, pageSize);
        }

        private static Gadget Clone(Gadget source) => new()
        {
            Id = source.Id,
            Name = source.Name,
            Price = source.Price,
            CreationTime = source.CreationTime,
        };
    }
}
