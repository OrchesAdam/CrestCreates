using System;
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
        probe.BodyRan.Should().BeFalse("an unbound permission gate must not let the body run");
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
        probe.BodyRan.Should().BeFalse("a denied call must not execute the business method");
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
        probe.BodyRan.Should().BeTrue();
    }

    private static ServiceProvider BuildProvider(bool registerChecker, bool granted)
    {
        var services = new ServiceCollection();
        services.AddScoped<PermissionProbeService>();
        if (registerChecker)
        {
            services.AddSingleton(new StubPermissionChecker(granted));
            services.AddSingleton<IPermissionChecker>(sp => sp.GetRequiredService<StubPermissionChecker>());
        }

        return services.BuildServiceProvider();
    }

    public sealed class PermissionProbeService : IServiceProviderAccessor
    {
        public PermissionProbeService(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider? ServiceProvider { get; }

        public bool BodyRan { get; private set; }

        [PermissionMo("demo.permission")]
        public Task<string> RunAsync()
        {
            BodyRan = true;
            return Task.FromResult("ok");
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
