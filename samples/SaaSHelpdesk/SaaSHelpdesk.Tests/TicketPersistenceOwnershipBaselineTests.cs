using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CrestCreates.Data.Abstractions;
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
    public void U02_production_composition_declares_the_binding_more_than_once()
    {
        var callers = AssembliesDeclaringBinding().OrderBy(name => name, StringComparer.Ordinal).ToList();

        callers.Should().HaveCount(1,
            "exactly one composition owner may declare the EfCore binding (rev.2 14.3 PlatformDefault vs "
            + "ApplicationOwned). Assemblies whose metadata references the binding-declaring API: "
            + string.Join(", ", callers));
    }

    [Fact]
    public void U03_test_measurement_facility_does_not_declare_an_owner()
    {
        AssembliesDeclaringBinding().Should().NotContain(
            typeof(TicketPersistenceOwnershipBaselineTests).Assembly.GetName().Name!,
            "the fixture must not act as a third unit-of-work owner; only the measurement decorator in " +
            "TicketProjectionGapBaselineTests may normalise the observed duplicate, and it says so explicitly");
    }

    [Fact]
    public void U04_test_host_observes_two_conflicting_bindings()
    {
        Factory.Services.GetServices<UnitOfWorkProviderBinding>()
            .Count(binding => binding.Provider == OrmProvider.EfCore).Should().BeGreaterThan(1,
                "the duplicate must stay observable in the host so R01 cannot silently turn green by " +
                "removing the measurement path instead of the composition defect");
    }

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
