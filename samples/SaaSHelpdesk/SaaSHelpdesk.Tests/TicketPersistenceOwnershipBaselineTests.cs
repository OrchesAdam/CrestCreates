using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CrestCreates.Data.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using SaaSHelpdesk.EntityFrameworkCore.Modules;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 rev.2 14.3 / 14.7.5: attribute who declares the EfCore unit-of-work binding instead of
/// only counting bindings. Call sites are read from assembly metadata, so a removed duplicate cannot
/// pass by editing a comment.
/// </summary>
public class TicketPersistenceOwnershipBaselineTests : BaseTest
{
    private const string BindingDeclaringMethod = "AddEfCoreUnitOfWork";
    private const string DeclaringTypeName = "EfCoreUnitOfWorkServiceCollectionExtensions";

    public TicketPersistenceOwnershipBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void U01_sample_persistence_module_alone_declares_exactly_one_efcore_binding()
    {
        var services = new ServiceCollection();

        new EntityFrameworkCoreModule().OnConfigureServices(services);

        EfCoreBindingDeclarations(services).Should().Be(1,
            "the application-owned persistence module must be able to declare the provider binding on its own");
    }

    [Fact]
    public void U02_binding_declaring_call_sites_are_attributed_by_assembly_metadata()
    {
        var callers = AssembliesDeclaringBinding().OrderBy(name => name, StringComparer.Ordinal).ToList();

        // This is attribution evidence, not the invariant. Referring to the binding-declaring API does
        // not make an assembly an owner of the final graph. Rev.2 14.3 resolved the ownership decision
        // in favor of ApplicationOwned: the platform composes nothing, and exactly the sample's
        // persistence module declares the binding.
        callers.Should().BeEquivalentTo(
            new[] { "SaaSHelpdesk.EntityFrameworkCore" },
            "the application-owned persistence module must be the only production declaration site; the "
            + "platform preset was removed. Observed callers: " + string.Join(", ", callers));
    }

    [Fact]
    public void U05_registry_accepts_one_binding_and_refuses_a_conflicting_second_one()
    {
        var single = new UnitOfWorkProviderBinding(
            OrmProvider.EfCore, static _ => null!, supportsRequiresNew: false);
        var others = new[]
        {
            new UnitOfWorkProviderBinding(OrmProvider.FreeSql, static _ => null!, supportsRequiresNew: false),
        };

        var merged = new UnitOfWorkProviderBindingRegistry(new[] { single }.Concat(others));
        merged.GetRequired(OrmProvider.EfCore).Should().BeSameAs(single);
        merged.GetRequired(OrmProvider.FreeSql).Should().BeSameAs(others[0]);

        var conflict = new[]
        {
            single,
            new UnitOfWorkProviderBinding(OrmProvider.EfCore, static _ => null!, supportsRequiresNew: false),
        };

        var refused = Assert.Throws<InvalidOperationException>(() => BuildRegistry(conflict));
        refused.Message.Should().Contain("Duplicate unit-of-work binding for provider 'EfCore'");

        // #137 semantics: an empty graph is not refused at construction time; the refusal happens when a
        // provider is required. The duplicate, by contrast, is refused while building the index.
        var empty = BuildRegistry(Array.Empty<UnitOfWorkProviderBinding>());
        Assert.Throws<InvalidOperationException>(() => empty.GetRequired(OrmProvider.EfCore))
            .Message.Should().Contain("Register the provider package's unit-of-work services");
    }

    [Fact]
    public void U03_test_measurement_facility_does_not_declare_an_owner()
    {
        AssembliesDeclaringBinding().Should().NotContain(
            typeof(TicketPersistenceOwnershipBaselineTests).Assembly.GetName().Name!,
            "the fixture must not act as a unit-of-work owner; the sample's persistence module is the " +
            "single application-owned declaration and no test-side normalization exists");
    }

    [Fact]
    public void U04_test_host_observes_exactly_one_efcore_binding()
    {
        Factory.Services.GetServices<UnitOfWorkProviderBinding>()
            .Count(binding => binding.Provider == OrmProvider.EfCore).Should().Be(1,
                "the application-owned module is the single owner; no measurement normalization may " +
                "collapse a duplicate that the production composition still declares");
    }

    [Fact]
    public void H07_host_without_a_provider_binding_fails_before_serving()
    {
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.RemoveAll<UnitOfWorkProviderBinding>()));

        var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        failure.ToString().Should().Contain("CCHTTP007");
        failure.ToString().Should().Contain("no ORM provider binding");
    }

    [Fact]
    public void H07_duplicate_provider_binding_fails_before_serving()
    {
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(new UnitOfWorkProviderBinding(
                OrmProvider.EfCore, static _ => null!, supportsRequiresNew: false))));

        var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        failure.ToString().Should().Contain("Duplicate unit-of-work binding for provider 'EfCore'");
    }

    private static UnitOfWorkProviderBindingRegistry BuildRegistry(IEnumerable<UnitOfWorkProviderBinding> bindings)
        => new(bindings);

    private static int EfCoreBindingDeclarations(IServiceCollection services)
        => services.Count(d => d.ServiceType == typeof(UnitOfWorkProviderBinding)
                                && d.ImplementationInstance is UnitOfWorkProviderBinding binding
                                && binding.Provider == OrmProvider.EfCore);

    private static List<string> AssembliesDeclaringBinding()
    {
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var peReader = new PEReader(stream);
                var reader = peReader.GetMetadataReader();
                foreach (var handle in reader.MemberReferences)
                {
                    var memberRef = reader.GetMemberReference(handle);
                    if (reader.GetString(memberRef.Name) != BindingDeclaringMethod)
                    {
                        continue;
                    }

                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        if (reader.GetString(typeRef.Name) == DeclaringTypeName)
                        {
                            found.Add(reader.GetString(reader.GetAssemblyDefinition().Name));
                            break;
                        }
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // native or non-managed payload in the output folder
            }
        }

        return found;
    }
}
