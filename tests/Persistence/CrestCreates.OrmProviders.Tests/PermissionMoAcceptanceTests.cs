using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Authorization.Interceptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// [PermissionMo] 织入路径验收：权限门必须 fail-closed —— 解析不到 checker 时确定性拒绝，
/// 拒绝时异常向外传播且业务方法体不执行；只有通过检查才进入方法体。
/// </summary>
public class PermissionMoAcceptanceTests
{
    [Fact]
    public async Task Missing_Checker_Fails_Closed_Without_Running_The_Body()
    {
        using var provider = BuildProvider(registerChecker: false, granted: false);
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<PermissionProbeService>();

        var act = async () => await probe.RunAsync();

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.And.Message.Should().Contain(nameof(IPermissionChecker),
            "the diagnostic must name the service the permission gate could not resolve");
        probe.BodyRunCount.Should().Be(0, "an unbound permission gate must not let the body run");
    }

    [Fact]
    public async Task Denied_Permission_Propagates_And_Does_Not_Run_The_Body()
    {
        using var provider = BuildProvider(registerChecker: true, granted: false);
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<PermissionProbeService>();
        var checker = scope.ServiceProvider.GetRequiredService<StubPermissionChecker>();

        var act = async () => await probe.RunAsync();

        await act.Should().ThrowAsync<CrestPermissionException>();
        checker.CheckedPermission.Should().Be("demo.permission");
        probe.BodyRunCount.Should().Be(0, "a denied call must not execute the business method");
    }

    [Fact]
    public async Task Granted_Permission_Runs_The_Body()
    {
        using var provider = BuildProvider(registerChecker: true, granted: true);
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<PermissionProbeService>();
        var checker = scope.ServiceProvider.GetRequiredService<StubPermissionChecker>();

        var result = await probe.RunAsync();

        result.Should().Be("ok");
        checker.CheckedPermission.Should().Be("demo.permission");
        probe.BodyRunCount.Should().Be(1, "the body runs exactly once after a granted check");
    }

    [Fact]
    public async Task Faulted_Checker_After_Await_Propagates_And_Does_Not_Run_The_Body()
    {
        using var provider = BuildProvider(registerChecker: true, granted: false, faulted: true);
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<PermissionProbeService>();

        var act = async () => await probe.RunAsync();

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.And.Message.Should().Contain("permission store is unavailable",
            "the checker fault must leave the interceptor unchanged instead of being swallowed");
        probe.BodyRunCount.Should().Be(0, "a checker fault must fail closed, not allow the body");
    }

    [Fact]
    public async Task Denial_Thrown_After_Await_Still_Does_Not_Run_The_Body()
    {
        using var provider = BuildProvider(new DelayedDenialPermissionChecker());
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<PermissionProbeService>();

        var act = async () => await probe.RunAsync();

        await act.Should().ThrowAsync<CrestPermissionException>();
        probe.BodyRunCount.Should().Be(0, "a denial raised after the async hop must still block the body");
    }

    [Fact]
    public async Task Two_Hosts_Use_Their_Own_Checker_Without_Cross_Talk()
    {
        using var grantingProvider = BuildProvider(registerChecker: true, granted: true);
        using var denyingProvider = BuildProvider(registerChecker: true, granted: false);
        using var grantingScope = grantingProvider.CreateScope();
        using var denyingScope = denyingProvider.CreateScope();
        var grantingProbe = grantingScope.ServiceProvider.GetRequiredService<PermissionProbeService>();
        var denyingProbe = denyingScope.ServiceProvider.GetRequiredService<PermissionProbeService>();
        var grantingChecker = grantingScope.ServiceProvider.GetRequiredService<StubPermissionChecker>();
        var denyingChecker = denyingScope.ServiceProvider.GetRequiredService<StubPermissionChecker>();

        (await grantingProbe.RunAsync()).Should().Be("ok");
        var denied = async () => await denyingProbe.RunAsync();
        await denied.Should().ThrowAsync<CrestPermissionException>();

        grantingProbe.BodyRunCount.Should().Be(1);
        denyingProbe.BodyRunCount.Should().Be(0, "each host must resolve its own checker");
        grantingChecker.CheckedPermission.Should().Be("demo.permission");
        denyingChecker.CheckedPermission.Should().Be("demo.permission");
    }

    private static ServiceProvider BuildProvider(bool registerChecker, bool granted, bool faulted = false)
    {
        var services = new ServiceCollection();
        services.AddScoped<PermissionProbeService>();
        if (registerChecker)
        {
            if (faulted)
            {
                services.AddSingleton<IPermissionChecker>(new FaultingPermissionChecker());
            }
            else
            {
                services.AddSingleton(new StubPermissionChecker(granted));
                services.AddSingleton<IPermissionChecker>(sp => sp.GetRequiredService<StubPermissionChecker>());
            }
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProvider(IPermissionChecker checker)
    {
        var services = new ServiceCollection();
        services.AddScoped<PermissionProbeService>();
        services.AddSingleton(checker);
        return services.BuildServiceProvider();
    }

    public sealed class PermissionProbeService : IServiceProviderAccessor
    {
        public PermissionProbeService(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider? ServiceProvider { get; }

        public int BodyRunCount { get; private set; }

        public bool BodyRan => BodyRunCount > 0;

        [PermissionMo("demo.permission")]
        public Task<string> RunAsync()
        {
            BodyRunCount++;
            return Task.FromResult("ok");
        }
    }

    private sealed class DelayedDenialPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(false);

        public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
            => Task.FromResult(false);

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(new Dictionary<string, bool>()));

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(
            ClaimsPrincipal principal, string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(new Dictionary<string, bool>()));

        public async Task CheckAsync(string permissionName)
        {
            await Task.Yield();
            throw new CrestPermissionException(permissionName);
        }
    }

    private sealed class FaultingPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(false);

        public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
            => Task.FromResult(false);

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(new Dictionary<string, bool>()));

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(
            ClaimsPrincipal principal, string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(new Dictionary<string, bool>()));

        public async Task CheckAsync(string permissionName)
        {
            await Task.Yield();
            throw new InvalidOperationException("permission store is unavailable");
        }
    }

    private sealed class StubPermissionChecker : IPermissionChecker
    {
        private readonly bool _granted;

        public StubPermissionChecker(bool granted)
        {
            _granted = granted;
        }

        public string? CheckedPermission { get; private set; }

        public Task<bool> IsGrantedAsync(string permissionName)
            => Task.FromResult(_granted);

        public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
            => Task.FromResult(_granted);

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
            => Task.FromResult(BuildResult(permissionNames));

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(
            ClaimsPrincipal principal, string[] permissionNames)
            => Task.FromResult(BuildResult(permissionNames));

        public Task CheckAsync(string permissionName)
        {
            CheckedPermission = permissionName;
            if (!_granted)
            {
                throw new CrestPermissionException(permissionName);
            }

            return Task.CompletedTask;
        }

        private MultiplePermissionGrantResult BuildResult(string[] permissionNames)
        {
            var result = new System.Collections.Generic.Dictionary<string, bool>();
            foreach (var permissionName in permissionNames)
            {
                result[permissionName] = _granted;
            }

            return new MultiplePermissionGrantResult(result);
        }
    }
}
