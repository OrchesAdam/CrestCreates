using System;
using System.Linq;
using System.Security.Claims;
using CrestCreates.Infrastructure.Authorization;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Identity;

public class IdentityClaimsBuilderTests
{
    private readonly IdentityClaimsBuilder _builder = new();

    [Fact]
    public void Build_WithFullContext_ContainsAllExpectedClaims()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "alice",
            Email = "alice@test.com",
            TenantId = "tenant-a",
            OrganizationId = Guid.NewGuid(),
            IsSuperAdmin = true,
            Roles = new[] { "Admin", "User" },
            Permissions = new[] { "Books.Create", "Books.Delete" }
        };

        var claims = _builder.Build(context);

        claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == context.UserId.ToString());
        claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "alice");
        claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == "alice@test.com");
        claims.Should().Contain(c => c.Type == "tenantid" && c.Value == "tenant-a");
        claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "tenant-a");
        claims.Should().Contain(c => c.Type == "is_super_admin" && c.Value == "true");
        claims.Should().Contain(c => c.Type == "org_id" && c.Value == context.OrganizationId.ToString());
    }

    [Fact]
    public void Build_WithRoles_ContainsRoleClaims()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "bob",
            Roles = new[] { "Admin", "Editor" }
        };

        var claims = _builder.Build(context);

        var roleClaims = claims.Where(c => c.Type == ClaimTypes.Role).ToList();
        roleClaims.Should().HaveCount(2);
        roleClaims.Should().Contain(c => c.Value == "Admin");
        roleClaims.Should().Contain(c => c.Value == "Editor");
    }

    [Fact]
    public void Build_WithPermissions_ContainsPermissionClaims()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "carol",
            Permissions = new[] { "Books.Create", "Books.Delete" }
        };

        var claims = _builder.Build(context);

        var permClaims = claims.Where(c => c.Type == "permission").ToList();
        permClaims.Should().HaveCount(2);
        permClaims.Should().Contain(c => c.Value == "Books.Create");
        permClaims.Should().Contain(c => c.Value == "Books.Delete");
    }

    [Fact]
    public void Build_WithoutOrganizationId_OmitsOrgIdClaim()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "dave",
            OrganizationId = null
        };

        var claims = _builder.Build(context);

        claims.Should().NotContain(c => c.Type == "org_id");
    }

    [Fact]
    public void Build_WithTenantId_ContainsBothTenantClaimTypes()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "eve",
            TenantId = "tenant-x"
        };

        var claims = _builder.Build(context);

        claims.Should().Contain(c => c.Type == "tenantid" && c.Value == "tenant-x");
        claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "tenant-x");
    }

    [Fact]
    public void Build_WithEmptyTenantId_ContainsEmptyTenantClaims()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "frank",
            TenantId = null
        };

        var claims = _builder.Build(context);

        claims.Should().Contain(c => c.Type == "tenantid" && c.Value == string.Empty);
        claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == string.Empty);
    }

    [Fact]
    public void Build_DeduplicatesRoles_CaseInsensitive()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "grace",
            Roles = new[] { "admin", "ADMIN", "Admin" }
        };

        var claims = _builder.Build(context);

        var roleClaims = claims.Where(c => c.Type == ClaimTypes.Role).ToList();
        roleClaims.Should().HaveCount(1);
    }

    [Fact]
    public void Build_DeduplicatesPermissions_CaseInsensitive()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "hank",
            Permissions = new[] { "books.create", "BOOKS.CREATE", "Books.Create" }
        };

        var claims = _builder.Build(context);

        var permClaims = claims.Where(c => c.Type == "permission").ToList();
        permClaims.Should().HaveCount(1);
    }

    [Fact]
    public void Build_AlwaysContainsJtiClaim()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "ivy"
        };

        var claims = _builder.Build(context);

        claims.Should().Contain(c => c.Type == "jti");
        claims.First(c => c.Type == "jti").Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Build_WithNonSuperAdmin_ContainsFalseValue()
    {
        var context = new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = "judy",
            IsSuperAdmin = false
        };

        var claims = _builder.Build(context);

        claims.Should().Contain(c => c.Type == "is_super_admin" && c.Value == "false");
    }
}
