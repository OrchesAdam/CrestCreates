using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// The ASP.NET Core assemblies a real formal-CRUD consumer receives from
/// <c>&lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt;</c>. The harness closes the
/// compilation over the framework as a set — not over hand-picked file names — because in .NET 10 the
/// producer's required types live in split assemblies (for example
/// <c>Microsoft.AspNetCore.Http.HttpContext</c> is declared in
/// <c>Microsoft.AspNetCore.Http.Abstractions</c>, not <c>Microsoft.AspNetCore.Http</c>), and a real
/// consumer never depends on which physical assembly each type happens to be in.
/// The shared-framework files are not copied next to the test assembly, so they are resolved from the
/// runtime directory of the loaded framework, in a deterministic order.
/// </summary>
internal static class CrudConsumerFrameworkClosure
{
    internal static IEnumerable<string> AspNetCoreSharedFrameworkAssemblies()
    {
        var directory = Path.GetDirectoryName(
            typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location);
        if (string.IsNullOrEmpty(directory))
        {
            yield break;
        }

        foreach (var path in Directory
                     .EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return path;
        }
    }
}
