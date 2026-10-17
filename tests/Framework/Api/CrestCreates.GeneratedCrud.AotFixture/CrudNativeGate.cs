using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.Json;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Capability;
using CrestCreates.Capability.Middleware;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_Feature;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.GeneratedCrud.AotFixture;

/// <summary>
/// #128 S4 native gate. Launched as <c>CrestCreates.GeneratedCrud.AotFixture --crud-gate</c>, it
/// serves the real host on a loopback port and drives the scenario matrix over real HTTP from
/// inside the published NativeAOT process, printing one <c>&lt;scenario&gt;:PASS</c> sentinel per
/// scenario plus the total sentinel. The matrix covers the five actions (create / get / search with
/// non-empty filters+sorts+paging / update with stamp / delete with If-Match), the 404 envelope for
/// an unknown id, the default CreationTime-desc ordering, a full-precision decimal filter value,
/// input rejection with zero business, the unit-of-work commit/rollback/discard facts, the
/// committed-write-survives-response-failure fact, the stale-stamp rejection, and a metadata
/// rejection scenario in which a second in-process host — still activating the real generated
/// contribution — clears its resolver chain, so the map-time contract verification must fail
/// deterministically (a reflection resolver can never cover a missing root).
///
/// Mutation knob for the harness: <c>CREST_CRUD_GATE_MUTATION=wrong-expectation</c> flips the
/// create-scenario expectation, so the gate must fail and the process must exit nonzero — proving
/// the assertions have teeth (a generic OK can never mask a broken expectation).
/// </summary>
public static class CrudNativeGate
{
    public const string TotalSentinel = "CRESTCREATES_GENERATED_CRUD_NATIVE_OK";

    public const string MutationVariable = "CREST_CRUD_GATE_MUTATION";

    private const string WrongExpectationMutation = "wrong-expectation";

    private const string WrongTypedEnvelopeMutation = "wrong-typed-envelope";

    public static async Task<int> RunAsync(WebApplication app)
    {
        var mutation = Environment.GetEnvironmentVariable(MutationVariable) ?? string.Empty;
        var failures = new List<string>();

        await app.StartAsync().ConfigureAwait(false);
        try
        {
            var baseUrl = ResolveBaseUrl(app);
            Console.WriteLine($"crud-gate base url: {baseUrl}");

            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            var context = new GateContext(
                client,
                app.Services.GetRequiredService<MemoryWidgetRepository>(),
                app.Services.GetRequiredService<MemoryUnitOfWorkObserver>(),
                app.Services.GetRequiredService<ResponseWriteFailureGate>(),
                mutation);

            foreach (var (name, run) in Scenarios())
            {
                try
                {
                    await run(context).ConfigureAwait(false);
                    Console.WriteLine($"{name}:PASS");
                }
                catch (Exception exception)
                {
                    failures.Add($"{name}: {exception.GetType().Name}: {exception.Message}");
                    Console.WriteLine($"{name}:FAIL {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }

        if (failures.Count == 0)
        {
            Console.WriteLine(TotalSentinel);
            return 0;
        }

        Console.Error.WriteLine("crud-gate failures: " + string.Join(" | ", failures));
        return 1;
    }

    private static IEnumerable<(string Name, Func<GateContext, Task> Run)> Scenarios()
    {
        yield return ("create", CreateAsync);
        yield return ("get-by-id", GetByIdAsync);
        yield return ("get-unknown-answers-404", GetUnknownAnswersNotFoundAsync);
        yield return ("search-filter-sort-page", SearchFilterSortPageAsync);
        yield return ("search-default-sort", SearchDefaultSortAsync);
        yield return ("complex-scalar-filter", ComplexScalarFilterAsync);
        yield return ("update-with-stamp", UpdateWithStampAsync);
        yield return ("delete-with-if-match", DeleteWithIfMatchAsync);
        yield return ("post-delete-get-404", PostDeleteGetNotFoundAsync);
        yield return ("input-rejection", InputRejectionAsync);
        yield return ("unit-of-work-commit", UnitOfWorkCommitAsync);
        yield return ("unit-of-work-rollback", UnitOfWorkRollbackAsync);
        yield return ("stale-stamp-rejected", StaleStampRejectedAsync);
        yield return ("commit-survives-response-write-failure", CommitSurvivesResponseWriteFailureAsync);
        yield return ("metadata-rejection", MetadataRejectionAsync);
        yield return ("expression-tree-construction-probe", ExpressionTreeConstructionProbeAsync);
    }

    /// <summary>
    /// Platform-fact probe: which comparison shapes survive expression-tree construction under
    /// NativeAOT. The generated adapter's emission matrix is derived from this evidence (the runtime
    /// resolves user-defined operators for <c>==</c> inside expression trees, and that resolution
    /// fails without rooted operator metadata). The probe reports every outcome and fails when a
    /// shape the current adapter relies on cannot be constructed.
    /// </summary>
    private static Task ExpressionTreeConstructionProbeAsync(GateContext context)
    {
        var outcomes = new List<string>();
        var informational = new List<string>();
        void Probe(string name, Func<Expression> build)
        {
            try
            {
                _ = build();
                outcomes.Add(name + "=ok");
            }
            catch (Exception exception)
            {
                outcomes.Add(name + "=" + exception.GetType().Name);
            }
        }

        // Informational only: the decimal operator forms are the documented platform gap this
        // emitter convergence exists for (JIT constructs them, NativeAOT rejects them). If a future
        // runtime starts constructing them, the line below shows it.
        void ProbeGap(string name, Func<Expression> build)
        {
            try
            {
                _ = build();
                informational.Add(name + "=constructed");
            }
            catch (Exception exception)
            {
                informational.Add(name + "=" + exception.GetType().Name + "(known gap on NativeAOT)");
            }
        }

        var number = 1.5m;
        var integer = 42;
        var flag = true;
        var text = "probe";
        var otherText = "other";
        var id = Guid.Empty;
        var otherId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var otherTimestamp = DateTime.UtcNow.AddSeconds(1);
        var ratio = 1.5;
        var kind = ProbeKind.First;
        var otherKind = ProbeKind.Second;
        var nullableText = "x";
        var set = new[] { Guid.Empty };

        Probe("int-eq", () => (Expression<Func<bool>>)(() => integer == integer));
        Probe("bool-eq", () => (Expression<Func<bool>>)(() => flag == flag));
        Probe("double-eq", () => (Expression<Func<bool>>)(() => ratio == ratio));
        Probe("enum-eq", () => (Expression<Func<bool>>)(() => kind == otherKind));
        Probe("string-eq", () => (Expression<Func<bool>>)(() => text == otherText));
        Probe("string-null-check", () => (Expression<Func<bool>>)(() => nullableText == null));
        Probe("string-notnull-and-contains", () => (Expression<Func<bool>>)(() => nullableText != null && nullableText.Contains(text)));
        Probe("guid-eq", () => (Expression<Func<bool>>)(() => id == otherId));
        ProbeGap("decimal-eq", () => (Expression<Func<bool>>)(() => number == number));
        ProbeGap("decimal-gt", () => (Expression<Func<bool>>)(() => number > number));
        Probe("datetime-eq", () => (Expression<Func<bool>>)(() => timestamp == otherTimestamp));
        Probe("datetime-gt", () => (Expression<Func<bool>>)(() => timestamp > otherTimestamp));
        Probe("decimal-equals-method", () => (Expression<Func<bool>>)(() => number.Equals(number)));
        Probe("decimal-compareto", () => (Expression<Func<bool>>)(() => number.CompareTo(number) > 0));
        Probe("datetime-compareto", () => (Expression<Func<bool>>)(() => timestamp.CompareTo(otherTimestamp) > 0));
        Probe("guid-equals-method", () => (Expression<Func<bool>>)(() => id.Equals(otherId)));
        Probe("string-equals-method", () => (Expression<Func<bool>>)(() => text.Equals(otherText)));
        Probe("enum-equals-method", () => (Expression<Func<bool>>)(() => kind.Equals(otherKind)));
        Probe("array-contains", () => (Expression<Func<bool>>)(() => set.Contains(id)));

        var nullableNumber = (decimal?)1.5m;
        var nullableInteger = (int?)42;
        Probe("nullable-decimal-guarded-equals", () => (Expression<Func<bool>>)(() => nullableNumber.HasValue && nullableNumber.Value.Equals(number)));
        Probe("nullable-decimal-hasvalue-negated", () => (Expression<Func<bool>>)(() => !nullableNumber.HasValue));
        Probe("nullable-decimal-values-equals", () => (Expression<Func<bool>>)(() => nullableNumber.Value.Equals(number)));
        Probe("nullable-int-eq", () => (Expression<Func<bool>>)(() => nullableInteger == integer));
        Probe("nullable-int-gt", () => (Expression<Func<bool>>)(() => nullableInteger > integer));
        Probe("nullable-int-null-check", () => (Expression<Func<bool>>)(() => nullableInteger == null));
        Probe("nullable-int-hasvalue", () => (Expression<Func<bool>>)(() => nullableInteger.HasValue));

        Console.WriteLine(
            "expression-tree construction probe: " + string.Join(", ", outcomes)
            + " || informational: " + string.Join(", ", informational));
        Check(
            outcomes.All(outcome => outcome.EndsWith("=ok", StringComparison.Ordinal)),
            "expression-tree construction probe: " + string.Join(", ", outcomes));
        return Task.CompletedTask;
    }

    private enum ProbeKind
    {
        First,
        Second,
    }

    private static async Task CreateAsync(GateContext context)
    {
        context.CreatedName = "gate-create-" + Guid.NewGuid().ToString("N");
        var outcome = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets",
            $$"""{"name":"{{context.CreatedName}}","price":12.5,"description":"gate row"}""");

        Check(outcome.Status == 200, $"create must answer 200, got {outcome.Status}: {outcome.Body}");
        var (code, _, data) = ParseEnvelope(outcome);
        Check(code == 200, "the create envelope must carry code 200");
        context.CreatedId = data.GetProperty("id").GetGuid();
        context.CreatedStamp = data.GetProperty("concurrencyStamp").GetString()!;
        Check(context.CreatedId != Guid.Empty, "the persisted identity must round-trip");

        if (context.Mutation == WrongTypedEnvelopeMutation)
        {
            // Mutation control: a legacy raw-payload expectation must fail against the typed envelope.
            Check(
                data.ValueKind == JsonValueKind.String,
                "the mutated expectation requires the legacy raw-payload shape, which the typed envelope must never produce");
        }

        var expectedName = context.Mutation == WrongExpectationMutation
            ? context.CreatedName + "-WRONG"
            : context.CreatedName;
        Check(
            data.GetProperty("name").GetString() == expectedName,
            $"the created name must round-trip (expected '{expectedName}')");
        Check(context.Repository.Find(context.CreatedId) is not null, "the created row must be stored");
    }

    private static async Task GetByIdAsync(GateContext context)
    {
        var outcome = await context.SendAsync(HttpMethod.Get, "/api/widgets/" + context.CreatedId, null);
        Check(outcome.Status == 200, $"get must answer 200, got {outcome.Status}: {outcome.Body}");
        var (_, _, data) = ParseEnvelope(outcome);
        Check(
            data.GetProperty("id").GetGuid() == context.CreatedId,
            "the route identity must arrive at the service and come back in the envelope");
    }

    private static async Task GetUnknownAnswersNotFoundAsync(GateContext context)
    {
        var unknown = Guid.NewGuid();
        var outcome = await context.SendAsync(HttpMethod.Get, "/api/widgets/" + unknown, null);
        Check(outcome.Status == 404, $"an unknown id must answer 404, got {outcome.Status}: {outcome.Body}");
        using var document = JsonDocument.Parse(outcome.Body);
        Check(document.RootElement.GetProperty("code").GetInt32() == 404, "the 404 envelope must carry code 404");
        Check(
            document.RootElement.GetProperty("message").GetString() == "资源不存在",
            "the 404 envelope must carry the declared message");
        Check(
            !document.RootElement.TryGetProperty("data", out _),
            "the 404 envelope must not carry a data field");
    }

    private static async Task SearchFilterSortPageAsync(GateContext context)
    {
        context.SearchPrefix = "gate-search-" + Guid.NewGuid().ToString("N")[..8];
        foreach (var price in new[] { 1.5m, 2.5m, 3.5m })
        {
            var name = context.SearchPrefix + "-" + price.ToString(CultureInfo.InvariantCulture);
            var outcome = await context.SendAsync(
                HttpMethod.Post,
                "/api/widgets",
                $$"""{"name":"{{name}}","price":{{price.ToString(CultureInfo.InvariantCulture)}},"description":null}""");
            Check(outcome.Status == 200, $"search arrangement create must answer 200: {outcome.Body}");
        }

        var search = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets/search",
            $$"""{"pageIndex":0,"pageSize":2,"filters":[{"field":"Name","operator":2,"value":"{{context.SearchPrefix}}"}],"sorts":[{"field":"Price","direction":1}]}""");
        Check(search.Status == 200, $"search must answer 200, got {search.Status}: {search.Body}");
        var (_, _, data) = ParseEnvelope(search);
        Check(
            data.GetProperty("totalCount").GetInt32() == 3,
            "the unique prefix filter must match exactly the three arranged rows");
        Check(data.GetProperty("pageIndex").GetInt32() == 0, "the page index must round-trip");
        Check(data.GetProperty("pageSize").GetInt32() == 2, "the page size must round-trip");
        var items = data.GetProperty("items");
        Check(items.GetArrayLength() == 2, "the bounded page must carry exactly pageSize rows");
        Check(items[0].GetProperty("price").GetDecimal() == 3.5m, "the explicit Price-desc sort must rank first");
        Check(items[1].GetProperty("price").GetDecimal() == 2.5m, "the explicit Price-desc sort must rank second");
    }

    private static async Task SearchDefaultSortAsync(GateContext context)
    {
        context.DefaultSortPrefix = "gate-default-" + Guid.NewGuid().ToString("N")[..8];
        context.DefaultSortIds.Clear();
        foreach (var index in Enumerable.Range(0, 3))
        {
            var name = context.DefaultSortPrefix + "-" + index.ToString(CultureInfo.InvariantCulture);
            var outcome = await context.SendAsync(
                HttpMethod.Post,
                "/api/widgets",
                $$"""{"name":"{{name}}","price":7.5,"description":null}""");
            Check(outcome.Status == 200, $"default-sort arrangement create must answer 200: {outcome.Body}");
            var (_, _, data) = ParseEnvelope(outcome);
            context.DefaultSortIds.Add(data.GetProperty("id").GetGuid());
        }

        var search = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets/search",
            $$"""{"pageIndex":0,"pageSize":20,"filters":[{"field":"Name","operator":2,"value":"{{context.DefaultSortPrefix}}"}]}""");
        Check(search.Status == 200, $"default-order search must answer 200: {search.Body}");
        var (_, _, page) = ParseEnvelope(search);
        var returned = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        Check(returned.Count == 3, "the default-order search must return the three arranged rows");

        // §8: the stable default order is CreationTime desc with an Id desc tiebreak.
        var expected = context.DefaultSortIds
            .Select(id => context.Repository.Find(id)!)
            .OrderByDescending(row => row.CreationTime)
            .ThenByDescending(row => row.Id)
            .Select(row => row.Id)
            .ToList();
        Check(
            returned.SequenceEqual(expected),
            "the default ordering must be CreationTime desc with the Id tiebreak");
    }

    private static async Task ComplexScalarFilterAsync(GateContext context)
    {
        var name = "gate-decimal-" + Guid.NewGuid().ToString("N")[..8];
        const decimal price = 123.45678901234567890123456m;
        var priceText = price.ToString(CultureInfo.InvariantCulture);

        var create = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets",
            $$"""{"name":"{{name}}","price":{{priceText}},"description":null}""");
        Check(create.Status == 200, $"decimal arrangement create must answer 200: {create.Body}");

        var search = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets/search",
            $$"""{"pageIndex":0,"pageSize":10,"filters":[{"field":"Price","operator":0,"value":{{priceText}}}]}""");
        Check(search.Status == 200, $"decimal filter search must answer 200: {search.Body}");
        var (_, _, page) = ParseEnvelope(search);
        Check(
            page.GetProperty("totalCount").GetInt32() == 1,
            "the full-precision decimal filter must match exactly the arranged row");
        var item = page.GetProperty("items")[0];
        Check(
            item.GetProperty("price").GetDecimal() == price,
            "the decimal filter value must not lose precision through the wire");
    }

    private static async Task UpdateWithStampAsync(GateContext context)
    {
        context.UpdatedName = "gate-updated-" + Guid.NewGuid().ToString("N")[..8];
        var outcome = await context.SendAsync(
            HttpMethod.Put,
            "/api/widgets/" + context.CreatedId,
            $$"""{"name":"{{context.UpdatedName}}","price":99.75,"description":null,"concurrencyStamp":"{{context.CreatedStamp}}"}""");
        Check(outcome.Status == 200, $"update must answer 200, got {outcome.Status}: {outcome.Body}");
        var (_, _, data) = ParseEnvelope(outcome);
        Check(data.GetProperty("name").GetString() == context.UpdatedName, "the update must be echoed");
        context.StampAfterUpdate = data.GetProperty("concurrencyStamp").GetString()!;
        Check(
            context.StampAfterUpdate != context.CreatedStamp,
            "a successful update must rotate the concurrency stamp");

        var stored = context.Repository.Find(context.CreatedId);
        Check(stored is not null && stored.Name == context.UpdatedName, "the update must be persisted");
    }

    private static async Task DeleteWithIfMatchAsync(GateContext context)
    {
        var outcome = await context.SendAsync(
            HttpMethod.Delete,
            "/api/widgets/" + context.CreatedId,
            json: null,
            ifMatch: context.StampAfterUpdate);
        Check(outcome.Status == 200, $"delete must answer 200, got {outcome.Status}: {outcome.Body}");
        using var document = JsonDocument.Parse(outcome.Body);
        Check(document.RootElement.GetProperty("code").GetInt32() == 200, "delete must answer the void envelope");
        Check(
            !document.RootElement.TryGetProperty("data", out _),
            "the delete void envelope must not carry a data field");
        Check(context.Repository.Find(context.CreatedId) is null, "the deleted row must be gone from the store");
    }

    private static async Task PostDeleteGetNotFoundAsync(GateContext context)
    {
        var outcome = await context.SendAsync(HttpMethod.Get, "/api/widgets/" + context.CreatedId, null);
        Check(
            outcome.Status == 404,
            $"getting the deleted id must answer 404, got {outcome.Status}: {outcome.Body}");
    }

    private static async Task InputRejectionAsync(GateContext context)
    {
        var countBefore = context.Repository.CountRows();

        var malformed = await context.SendAsync(HttpMethod.Post, "/api/widgets", "{ this is not valid json");
        Check(malformed.Status == 400, $"malformed JSON must answer 400, got {malformed.Status}");

        var empty = await context.SendAsync(HttpMethod.Post, "/api/widgets", string.Empty);
        Check(empty.Status == 400, $"an empty required body must answer 400, got {empty.Status}");

        var countAfter = context.Repository.CountRows();
        Check(countAfter == countBefore, "rejected input must execute zero business against the store");
    }

    private static async Task UnitOfWorkCommitAsync(GateContext context)
    {
        var before = context.Observer.Capture();
        var name = "gate-uow-commit-" + Guid.NewGuid().ToString("N")[..8];
        var outcome = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets",
            $$"""{"name":"{{name}}","price":4.5,"description":null}""");
        Check(outcome.Status == 200, $"the commit probe create must answer 200: {outcome.Body}");

        var after = context.Observer.Capture();
        Check(after.BeginCount - before.BeginCount == 1, "a write must begin exactly one unit of work");
        Check(after.CommitCount - before.CommitCount == 1, "a successful write must commit its unit of work");
        Check(after.RollbackCount - before.RollbackCount == 0, "a successful write must not roll back");
        Check(context.Repository.FindByName(name) is not null, "the committed row must be stored");
    }

    private static async Task UnitOfWorkRollbackAsync(GateContext context)
    {
        var before = context.Observer.Capture();
        var name = MemoryWidgetRepository.RollbackProbePrefix + Guid.NewGuid().ToString("N");
        var outcome = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets",
            $$"""{"name":"{{name}}","price":5.5,"description":null}""");
        Check(
            outcome.Status == 500,
            $"the rollback probe must fail after the store write, got {outcome.Status}: {outcome.Body}");
        Check(
            outcome.Body.Contains("rollback probe", StringComparison.Ordinal),
            "the failure body must carry the persistence-layer rejection");

        var after = context.Observer.Capture();
        Check(after.BeginCount - before.BeginCount == 1, "the failing write must begin one unit of work");
        Check(after.RollbackCount - before.RollbackCount == 1, "the failing write must roll back");
        Check(after.CommitCount - before.CommitCount == 0, "the failing write must not commit");
        Check(
            context.Repository.FindByName(name) is null,
            "rollback must discard the row the repository had already written");
    }

    private static async Task StaleStampRejectedAsync(GateContext context)
    {
        var createName = "gate-stamp-" + Guid.NewGuid().ToString("N")[..8];
        var create = await context.SendAsync(
            HttpMethod.Post,
            "/api/widgets",
            $$"""{"name":"{{createName}}","price":6.5,"description":null}""");
        Check(create.Status == 200, $"stamp arrangement create must answer 200: {create.Body}");
        var (_, _, created) = ParseEnvelope(create);
        var id = created.GetProperty("id").GetGuid();
        var originalStamp = created.GetProperty("concurrencyStamp").GetString()!;

        var firstName = createName + "-first";
        var firstUpdate = await context.SendAsync(
            HttpMethod.Put,
            "/api/widgets/" + id,
            $$"""{"name":"{{firstName}}","price":6.75,"description":null,"concurrencyStamp":"{{originalStamp}}"}""");
        Check(firstUpdate.Status == 200, $"the first stamped update must succeed: {firstUpdate.Body}");

        var before = context.Observer.Capture();
        var staleUpdate = await context.SendAsync(
            HttpMethod.Put,
            "/api/widgets/" + id,
            $$"""{"name":"{{createName}}-stale","price":6.9,"description":null,"concurrencyStamp":"{{originalStamp}}"}""");
        Check(
            staleUpdate.Status == 500,
            $"a stale stamp must be rejected, got {staleUpdate.Status}: {staleUpdate.Body}");
        Check(
            staleUpdate.Body.Contains("Concurrency conflict", StringComparison.Ordinal),
            "the rejection body must carry the optimistic-concurrency contract");
        Check(
            !staleUpdate.Body.Contains("stale", StringComparison.Ordinal),
            "the rejected update must not be echoed as applied");

        var after = context.Observer.Capture();
        Check(after.BeginCount - before.BeginCount == 1, "the rejected update must begin one unit of work");
        Check(after.RollbackCount - before.RollbackCount == 1, "the rejected update must roll back");
        Check(after.CommitCount - before.CommitCount == 0, "the rejected update must not commit");

        var stored = context.Repository.Find(id);
        Check(
            stored is not null && stored.Name == firstName,
            "the stored row must keep exactly the state the committed first update left");
    }

    private static async Task CommitSurvivesResponseWriteFailureAsync(GateContext context)
    {
        var name = "gate-write-failure-" + Guid.NewGuid().ToString()![..8];
        var before = context.Observer.Capture();
        context.WriteGate.ArmForNextWrite();

        Exception? failure = null;
        var failedStatus = 0;
        try
        {
            using var response = await context.Client.PostAsync(
                "/api/widgets",
                CreateJsonContent($$"""{"name":"{{name}}","price":8.5,"description":null}"""));
            failedStatus = (int)response.StatusCode;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Check(
            failure is not null || failedStatus >= 500,
            $"the armed response-write gate must fail the response (failure='{failure?.GetType().Name}', status={failedStatus})");

        var after = context.Observer.Capture();
        Check(after.BeginCount - before.BeginCount == 1, "the failed-response write must begin one unit of work");
        Check(after.CommitCount - before.CommitCount == 1, "the unit of work had already committed before the write failed");
        Check(after.RollbackCount - before.RollbackCount == 0, "a response-side failure must not roll back a committed write");
        Check(
            context.Repository.FindByName(name) is not null,
            "the committed fact must survive the response-side failure (§7.3)");
    }

    private static async Task MetadataRejectionAsync(GateContext context)
    {
        // The second host activates the REAL generated contribution (descriptors, bindings, invokers
        // and the declared JSON roots); only the host's final resolver chain is broken afterwards, so
        // the map-time verification must reject the unanswered required root — a reflection resolver
        // never satisfies it and a zero-endpoint drift can never look green.
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddAccountability();
        builder.Services.RemoveAll<ValidationMiddleware>();
        builder.Services.RemoveAll<IBootstrapValidator>();
        builder.Services.RemoveAll<IDescriptorBindingStatusContributor>();
        builder.Services.AddSingleton(new CapabilityPipelineBuilder());
        builder.Services.AddCrestCompatibilityProjection();
        builder.Services.AddGeneratedCrud();
        builder.Services.Configure<JsonOptions>(
            options => options.SerializerOptions.TypeInfoResolverChain.Clear());

        var mutated = builder.Build();
        try
        {
            try
            {
                mutated.MapCrestCapabilityEndpoints();
                throw new InvalidOperationException(
                    "the mutated host (cleared resolver chain) must be rejected at map time");
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains("Required JSON contract root", StringComparison.Ordinal))
            {
                // Expected: the deterministic fail-closed rejection.
            }
        }
        finally
        {
            await mutated.DisposeAsync();
        }
    }

    private static string ResolveBaseUrl(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault();
        Check(!string.IsNullOrWhiteSpace(address), "the gate host must expose its bound loopback address");
        return address!.Replace("0.0.0.0", "127.0.0.1").Replace("[::]", "127.0.0.1").TrimEnd('/');
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static (int Code, string? Message, JsonElement Data) ParseEnvelope(GateOutcome outcome)
    {
        using var document = JsonDocument.Parse(outcome.Body);
        var root = document.RootElement;
        var code = root.GetProperty("code").GetInt32();
        var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        var data = root.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : default;
        return (code, message, data);
    }

    private static StringContent CreateJsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private sealed record GateOutcome(int Status, string Body);

    private sealed class GateContext(
        HttpClient client,
        MemoryWidgetRepository repository,
        MemoryUnitOfWorkObserver observer,
        ResponseWriteFailureGate writeGate,
        string mutation)
    {
        public HttpClient Client { get; } = client;

        public MemoryWidgetRepository Repository { get; } = repository;

        public MemoryUnitOfWorkObserver Observer { get; } = observer;

        public ResponseWriteFailureGate WriteGate { get; } = writeGate;

        public string Mutation { get; } = mutation;

        public Guid CreatedId { get; set; }

        public string CreatedName { get; set; } = string.Empty;

        public string CreatedStamp { get; set; } = string.Empty;

        public string UpdatedName { get; set; } = string.Empty;

        public string StampAfterUpdate { get; set; } = string.Empty;

        public string SearchPrefix { get; set; } = string.Empty;

        public string DefaultSortPrefix { get; set; } = string.Empty;

        public List<Guid> DefaultSortIds { get; } = [];

        public async Task<GateOutcome> SendAsync(
            HttpMethod method,
            string path,
            string? json,
            string? ifMatch = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (json is not null)
            {
                request.Content = CreateJsonContent(json);
            }

            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
            }

            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new GateOutcome((int)response.StatusCode, body);
        }
    }
}
