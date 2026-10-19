using CrestCreates.JsonContracts.BuildTasks.HttpProjection;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
namespace CrestCreates.JsonContracts.BuildTasks.Tests.HttpProjection;
public sealed class ReviewS22BR2RouteProbes : JsonContractCompilationTestBase
{
    [Theory]
    [InlineData("api/orders", "all", "api/customers", "all", false)]
    [InlineData("api/orders", "all", "api", "orders/all", true)]
    [InlineData("api/orders", "all", "api/orders", "all", true)]
    [InlineData("api/orders", "all", "api/customers", "list", false)]
    public void Cross_contract_conflicts_must_use_the_final_emitted_route(string prefixA, string routeA, string prefixB, string routeB, bool conflict) {
        var source = $$"""
            using System.Threading; using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;
            namespace Probe {
              public interface IOrders {
                [AppServiceHttpAction(Verb=HttpProjectionVerb.Get, Route="{{routeA}}")]
                Task<string> GetAsync(CancellationToken cancellationToken=default);
              }
              public interface ICustomers {
                [AppServiceHttpAction(Verb=HttpProjectionVerb.Get, Route="{{routeB}}")]
                Task<string> GetAsync(CancellationToken cancellationToken=default);
              }
              [CapabilityCompatibilityProjection(ContractType=typeof(IOrders),RoutePrefix="{{prefixA}}")]
              public static class OrdersAnchor { }
              [CapabilityCompatibilityProjection(ContractType=typeof(ICustomers),RoutePrefix="{{prefixB}}")]
              public static class CustomersAnchor { }
            }
            """;
        var compilation = CreateCompilation("Probe.Http",new[] { ("Declaration.cs",source) });
        compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).Should().BeEmpty();
        var models = new[] { "Probe.OrdersAnchor", "Probe.CustomersAnchor" }.Select(n=>AppServiceHttpProjectionModelBuilder.Build(compilation,compilation.GetTypeByMetadataName(n)!)).ToArray();
        models.Should().OnlyContain(m=>!m.HasErrors && m.Model!=null);
        var fullA="/"+prefixA.Trim('/')+"/"+routeA.Trim('/');
        var fullB="/"+prefixB.Trim('/')+"/"+routeB.Trim('/');
        (fullA==fullB).Should().Be(conflict);
        foreach(var model in models.Select(m=>m.Model!)) {
            var emitted=AppServiceHttpProjectionEmitter.Emit(model,AppServiceHttpEmissionOptions.ForContract(model.ContractFullName)).Single().Source;
            emitted.Should().Contain("RoutePattern = \"" + (model.ContractName=="IOrders"?fullA:fullB) + "\"");
        }
        var result=new AppServiceHttpPrecompileEngine().Run("Probe.Http",new[] { ("Declaration.cs",source) },GetDefaultReferences(),"latest",Array.Empty<string>(),"enable",false);
        result.Succeeded.Should().Be(!conflict, string.Join(" | ",result.Diagnostics.Select(d=>d.Id+": "+d.Message)));
        if(conflict) {
            result.Diagnostics.Should().Contain(d=>d.Id==AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict && d.IsError);
            result.Artifacts.Should().BeEmpty();
        } else result.Artifacts.Should().HaveCount(3);
    }
}
