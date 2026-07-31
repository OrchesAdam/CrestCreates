using CrestCreates.Authorization.Abstractions;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Internal;
using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class PermissionCapabilityAuthorizationServiceTests
{
    // === T1: Empty permissions → allow execution, even with null IPermissionChecker ===
    [Fact]
    public async Task Authorize_EmptyPermissions_AllowsExecution()
    {
        var mockChecker = new Mock<IPermissionChecker>();
        var service = new PermissionCapabilityAuthorizationService(mockChecker.Object);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", Array.Empty<string>(), CancellationToken.None);

        result.Should().BeTrue();
        mockChecker.Verify(
            c => c.IsGrantedAsync(It.IsAny<string[]>()),
            Times.Never);
    }

    // === T1b: Empty permissions with null checker → still allowed ===
    [Fact]
    public async Task Authorize_EmptyPermissions_WithNullChecker_AllowsExecution()
    {
        var service = new PermissionCapabilityAuthorizationService(null);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", Array.Empty<string>(), CancellationToken.None);

        result.Should().BeTrue();
    }

    // === T1c: Non-empty permissions with null checker → throws ===
    [Fact]
    public async Task Authorize_NonEmptyPermissions_WithNullChecker_Throws()
    {
        var service = new PermissionCapabilityAuthorizationService(null);

        var act = () => service.AuthorizeAsync(
            "test.cap", "user1", new[] { "perm.read" }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "non-empty permissions with no IPermissionChecker must fail fast");
    }

    // === T2: All permissions granted → allow execution ===
    [Fact]
    public async Task Authorize_AllPermissionsGranted_AllowsExecution()
    {
        var mockChecker = new Mock<IPermissionChecker>();
        mockChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.read"] = true, ["perm.write"] = true }));

        var service = new PermissionCapabilityAuthorizationService(mockChecker.Object);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", new[] { "perm.read", "perm.write" }, CancellationToken.None);

        result.Should().BeTrue();
    }

    // === T3: Any permission denied → return false ===
    [Fact]
    public async Task Authorize_AnyPermissionDenied_ReturnsUnauthorized()
    {
        var mockChecker = new Mock<IPermissionChecker>();
        mockChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.read"] = true, ["perm.write"] = false }));

        var service = new PermissionCapabilityAuthorizationService(mockChecker.Object);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", new[] { "perm.read", "perm.write" }, CancellationToken.None);

        result.Should().BeFalse();
    }

    // === T4: AuthorizationMiddleware passes RequiredPermissions (not capability name) ===
    [Fact]
    public async Task AuthorizationMiddleware_UsesDescriptorPermissions_NotCapabilityName()
    {
        string? capturedPermission = null;
        var mockAuthService = new Mock<ICapabilityAuthorizationService>();
        mockAuthService
            .Setup(s => s.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, IReadOnlyList<string>, CancellationToken>(
                (name, userId, permissions, ct) => capturedPermission = permissions.FirstOrDefault())
            .ReturnsAsync(true);

        var middleware = new AuthorizationMiddleware(mockAuthService.Object);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.echo",
            CapabilityName = "Echo",
            UserId = "user1",
            RequiredPermissions = new[] { "perm.read" }
        };

        var result = await middleware.InvokeAsync(context, _ => Task.FromResult(
            CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        capturedPermission.Should().Be("perm.read",
            "middleware must pass descriptor permissions, not capability name");
    }

    // === T5: configureContext cannot clear RequiredPermissions — real pipeline test ===
    [Fact]
    public async Task Pipeline_SetsRequiredPermissions_AfterConfigureContext()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "secure.cap",
            Name = "Secure Cap",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active,
            Permissions = new[] { "perm.read", "perm.write" }
        };

        // Capture what permissions the checker actually receives
        string[]? capturedPermissions = null;
        var mockPermissionChecker = new Mock<IPermissionChecker>();
        mockPermissionChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .Callback<string[]>(perms => capturedPermissions = perms)
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.read"] = true, ["perm.write"] = true }));

        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(mockPermissionChecker.Object);
        services.AddSingleton<ILogger<AuditMiddleware>>(NullLogger<AuditMiddleware>.Instance);
        services.AddSingleton<ISchemaValidator>(new Mock<ISchemaValidator>().Object);

        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("secure.cap", new EchoHandlerInvoker());
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddCapabilityRuntime();
        services.AddAccountability();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestDescriptorProvider([descriptor])]);
        services.AddSingleton<ICapabilityRegistry>(registry);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

        // configureContext tries to clear permissions
        var result = await pipeline.ExecuteAsync("secure.cap", input: "test",
            configureContext: ctx => ctx.RequiredPermissions = Array.Empty<string>());

        result.IsSuccess.Should().BeTrue();
        capturedPermissions.Should().NotBeNull("IPermissionChecker must be called");
        capturedPermissions.Should().BeEquivalentTo(new[] { "perm.read", "perm.write" },
            "pipeline must set descriptor.Permissions after configureContext, even when caller clears");
    }

    // === T6: Full pipeline with granted permission → handler invoked ===
    [Fact]
    public async Task Pipeline_WithDescriptorPermissions_AndGrantedPermission_InvokesHandler()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "secure.echo",
            Name = "Secure Echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active,
            Permissions = new[] { "perm.read" }
        };

        var mockPermissionChecker = new Mock<IPermissionChecker>();
        mockPermissionChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.read"] = true }));

        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(mockPermissionChecker.Object);
        services.AddSingleton<ILogger<AuditMiddleware>>(NullLogger<AuditMiddleware>.Instance);
        services.AddSingleton<ISchemaValidator>(new Mock<ISchemaValidator>().Object);

        // Register handler resolver BEFORE AddCapabilityRuntime so TryAddSingleton
        // inside AddCapabilityPipeline won't create an empty second resolver
        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("secure.echo", new EchoHandlerInvoker());
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddCapabilityRuntime();
        services.AddAccountability();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestDescriptorProvider([descriptor])]);
        services.AddSingleton<ICapabilityRegistry>(registry);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

        var result = await pipeline.ExecuteAsync("secure.echo", input: "hello");

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("ECHO: hello");
    }

    // === T7: Full pipeline with denied permission → UNAUTHORIZED ===
    [Fact]
    public async Task Pipeline_WithDescriptorPermissions_AndDeniedPermission_ReturnsUnauthorized()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "secure.write",
            Name = "Secure Write",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            State = DescriptorState.Active,
            Permissions = new[] { "perm.write" }
        };

        var mockPermissionChecker = new Mock<IPermissionChecker>();
        mockPermissionChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.write"] = false }));

        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(mockPermissionChecker.Object);
        services.AddSingleton<ILogger<AuditMiddleware>>(NullLogger<AuditMiddleware>.Instance);
        services.AddSingleton<ISchemaValidator>(new Mock<ISchemaValidator>().Object);

        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("secure.write", new EchoHandlerInvoker());
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddCapabilityRuntime();
        services.AddAccountability();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestDescriptorProvider([descriptor])]);
        services.AddSingleton<ICapabilityRegistry>(registry);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

        var result = await pipeline.ExecuteAsync("secure.write", input: "data");

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    // === T8: AddCapabilityPipeline registers default ICapabilityAuthorizationService ===
    [Fact]
    public void AddCapabilityPipeline_RegistersDefaultAuthorizationService()
    {
        var mockPermissionChecker = new Mock<IPermissionChecker>();
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(mockPermissionChecker.Object);

        services.AddCapabilityPipeline();
        services.AddAccountability();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var authService = scope.ServiceProvider.GetService<ICapabilityAuthorizationService>();

        authService.Should().NotBeNull("AddCapabilityPipeline must register a default auth service");
        authService.Should().BeOfType<PermissionCapabilityAuthorizationService>(
            "default implementation must be PermissionCapabilityAuthorizationService");
    }

    // === T9: AddCapabilityRuntime inherits auth service from AddCapabilityPipeline ===
    [Fact]
    public void AddCapabilityRuntime_RegistersDefaultAuthorizationService()
    {
        var mockPermissionChecker = new Mock<IPermissionChecker>();
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(mockPermissionChecker.Object);

        services.AddCapabilityRuntime();
        services.AddAccountability();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var authService = scope.ServiceProvider.GetService<ICapabilityAuthorizationService>();

        authService.Should().NotBeNull("AddCapabilityRuntime must register auth service via AddCapabilityPipeline");
        authService.Should().BeOfType<PermissionCapabilityAuthorizationService>(
            "default implementation must be PermissionCapabilityAuthorizationService");
    }

    // === T10: Missing IPermissionChecker causes fail-fast, NOT silent skip ===
    [Fact]
    public async Task AddCapabilityPipeline_WithoutPermissionChecker_FailsFast()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "secure.cap",
            Name = "Secure Cap",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active,
            Permissions = new[] { "perm.read" }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AuditMiddleware>>(NullLogger<AuditMiddleware>.Instance);
        services.AddSingleton<ISchemaValidator>(new Mock<ISchemaValidator>().Object);

        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("secure.cap", new EchoHandlerInvoker());
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddCapabilityRuntime();
        services.AddAccountability();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestDescriptorProvider([descriptor])]);
        services.AddSingleton<ICapabilityRegistry>(registry);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

        // Executing a capability with permissions but no IPermissionChecker
        // must fail, not silently skip authorization.
        var result = await pipeline.ExecuteAsync("secure.cap", input: "test");

        result.IsSuccess.Should().BeFalse(
            "missing IPermissionChecker must cause pipeline failure, not silent auth skip");
    }

    // === T11: Empty permissions + no IPermissionChecker + full pipeline → succeeds ===
    [Fact]
    public async Task Pipeline_WithEmptyPermissions_AndNoPermissionChecker_Succeeds()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "open.cap",
            Name = "Open Cap",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active,
            Permissions = Array.Empty<string>() // empty — no permissions required
        };

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AuditMiddleware>>(NullLogger<AuditMiddleware>.Instance);
        services.AddSingleton<ISchemaValidator>(new Mock<ISchemaValidator>().Object);

        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("open.cap", new EchoHandlerInvoker());
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddCapabilityRuntime();
        services.AddAccountability();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestDescriptorProvider([descriptor])]);
        services.AddSingleton<ICapabilityRegistry>(registry);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

        var result = await pipeline.ExecuteAsync("open.cap", input: "hello");

        result.IsSuccess.Should().BeTrue(
            "empty permissions must allow execution even without IPermissionChecker registered");
        result.Output.Should().Be("ECHO: hello");
    }

    // === Helpers ===

    private sealed class TestDescriptorProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestDescriptorProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    private sealed class EchoHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>($"ECHO: {input}");
    }
}
