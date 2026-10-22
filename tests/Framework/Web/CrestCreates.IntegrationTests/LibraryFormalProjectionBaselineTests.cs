using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.HttpProjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// #129 S4 exit: LibraryManagement publishes its HTTP surface exclusively through the formal
/// ExplicitHost projection; the legacy runtime/analyzer/Web packages are not part of the build.
/// </summary>
public class LibraryFormalProjectionBaselineTests
{
    [Fact]
    public void Legacy_assemblies_are_not_loaded_by_the_test_process()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .ToArray();

        loaded.Should().NotContain("CrestCreates.DynamicApi.Legacy");
        loaded.Should().NotContain("CrestCreates.Web.Legacy");
        loaded.Should().NotContain("CrestCreates.CodeGenerator.Legacy");
    }

    [Fact]
    public void Formal_contributions_publish_every_library_action_exactly_once()
    {
        var services = new ServiceCollection();
        services.AddGeneratedAppServiceHttp();
        using var provider = services.BuildServiceProvider();

        var contributions = provider.GetServices<CapabilityEndpointContribution>().ToArray();
        var endpoints = contributions.SelectMany(contribution => contribution.EndpointDescriptors).ToArray();
        var capabilities = contributions
            .SelectMany(contribution => contribution.CapabilityDescriptors)
            .GroupBy(capability => capability.Id)
            .ToDictionary(group => group.Key, group => group.Single());

        var routes = endpoints
            .Select(endpoint => $"{endpoint.HttpMethod} {endpoint.RoutePattern.TrimStart('/')}")
            .ToArray();

        routes.Should().Contain(new[]
        {
            "Get api/book/by-isbn/{isbn}",
            "Get api/book/search-books",
            "Post api/book",
            "Get api/book/{id}",
            "Get api/book",
            "Put api/book/{id}",
            "Delete api/book/{id}",
            "Post api/category",
            "Get api/category/{id}",
            "Get api/category/all",
            "Get api/category",
            "Post api/category/query",
            "Put api/category/{id}",
            "Delete api/category/{id}",
            "Get api/category/exists/{id}",
            "Get api/category/count",
            "Post api/loan/search",
            "Get api/loan/overdue-loans",
            "Post api/loan/return-book",
            "Post api/loan/extend-loan",
            "Post api/loan/process-overdue-loans",
            "Post api/loan",
            "Get api/loan/{id}",
            "Get api/loan/all",
            "Get api/loan",
            "Post api/loan/query",
            "Put api/loan/{id}",
            "Delete api/loan/{id}",
            "Get api/loan/exists/{id}",
            "Get api/loan/count",
            "Get api/member/by-email/{email}",
            "Get api/member/get-active-members",
            "Get api/member/pay-balance",
            "Post api/member",
            "Get api/member/{id}",
            "Get api/member/all",
            "Get api/member",
            "Post api/member/query",
            "Put api/member/{id}",
            "Delete api/member/{id}",
            "Get api/member/exists/{id}",
            "Get api/member/count",
        });

        routes.Should().OnlyHaveUniqueItems("each migrated route is published once by the formal contribution");

        var permissions = endpoints.ToDictionary(
            endpoint => $"{endpoint.HttpMethod} {endpoint.RoutePattern.TrimStart('/')}",
            endpoint => capabilities[endpoint.Capability.Id].Permissions.ToArray());

        permissions["Post api/book"].Should().BeEquivalentTo("Book.Create");
        permissions["Post api/loan/search"].Should().BeEquivalentTo("Loan.Search");
        permissions["Get api/member/pay-balance"].Should().BeEquivalentTo("Member.Search");
        permissions["Delete api/category/{id}"].Should().BeEquivalentTo("Category.Delete");

        var requiredServices = contributions.SelectMany(contribution => contribution.RequiredServiceTypes).ToArray();
        requiredServices.Should().Contain(new[]
        {
            typeof(LibraryManagement.Application.Projections.BookHttpProjection),
            typeof(LibraryManagement.Application.Projections.CategoryHttpProjection),
            typeof(LibraryManagement.Application.Projections.LoanHttpProjection),
            typeof(LibraryManagement.Application.Projections.MemberHttpProjection),
        });
        requiredServices.Select(type => type.FullName).Should().NotContain(
            "CrestCreates.Validation.Modules.IValidationService",
            "the pre-exit Library host did not register validation; the formal migration must not add new body validation");

        contributions.Should().OnlyContain(contribution =>
            contribution.JsonContext != null && contribution.RequiredJsonRootTypes.Count > 0);
    }
}
