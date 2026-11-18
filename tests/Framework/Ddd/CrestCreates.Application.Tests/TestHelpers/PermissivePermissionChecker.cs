using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using Moq;

namespace CrestCreates.Application.Tests.TestHelpers;

/// <summary>
/// Grants every permission so service-level admission passes; used by business-behavior
/// tests that are not exercising authorization decisions.
/// </summary>
public static class PermissivePermissionChecker
{
    public static IPermissionChecker Create()
    {
        var mock = new Mock<IPermissionChecker>();
        mock.Setup(checker => checker.CheckAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mock.Setup(checker => checker.IsGrantedAsync(It.IsAny<string>()))
            .Returns(Task.FromResult(true));
        mock.Setup(checker => checker.IsGrantedAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
            .Returns(Task.FromResult(true));
        mock.Setup(checker => checker.IsGrantedAsync(It.IsAny<string[]>()))
            .Returns((string[] permissionNames) => Task.FromResult(AllGranted(permissionNames)));
        mock.Setup(checker => checker.IsGrantedAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string[]>()))
            .Returns((ClaimsPrincipal _, string[] permissionNames) => Task.FromResult(AllGranted(permissionNames)));
        return mock.Object;
    }

    private static MultiplePermissionGrantResult AllGranted(string[] permissionNames)
        => new(permissionNames.ToDictionary(name => name, _ => true));
}