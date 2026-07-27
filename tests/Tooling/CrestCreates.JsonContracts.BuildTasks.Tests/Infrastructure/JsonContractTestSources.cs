namespace CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;

public static class JsonContractTestSources
{
    public static (string Path, string Text) MinimalContext(
        string surfaceSource = "",
        string contextOptions = "")
    {
        var surfaceText = string.IsNullOrWhiteSpace(surfaceSource)
            ? @"
public interface ISampleService
{
    System.Threading.Tasks.Task<string> GetAsync(System.Threading.CancellationToken ct);
}"
            : surfaceSource;

        var contextCode = $@"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

{surfaceText}

[JsonContractSurface(typeof(ISampleService))]
[JsonSerializable(typeof(object))]
public partial class SampleJsonSerializerContext : JsonSerializerContext
{{
{contextOptions}
}}";

        return ("SampleContext.cs", contextCode);
    }

    public static (string Path, string Text) InheritedSurface()
    {
        return ("InheritedSurface.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface IBaseService
{
    System.Threading.Tasks.Task<string> BaseMethodAsync(System.Threading.CancellationToken ct);
}

public interface IDerivedService : IBaseService
{
    System.Threading.Tasks.Task<int> DerivedMethodAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IDerivedService))]
public partial class InheritedContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) DiamondSurface()
    {
        return ("DiamondSurface.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface IAlpha
{
    System.Threading.Tasks.Task<string> AlphaMethodAsync(System.Threading.CancellationToken ct);
}

public interface IBeta : IAlpha
{
    System.Threading.Tasks.Task<int> BetaMethodAsync(System.Threading.CancellationToken ct);
}

public interface IGamma : IAlpha
{
    System.Threading.Tasks.Task<double> GammaMethodAsync(System.Threading.CancellationToken ct);
}

public interface IDiamond : IBeta, IGamma
{
    System.Threading.Tasks.Task<bool> DiamondMethodAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IDiamond))]
public partial class DiamondContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) MultipleParameterSurface()
    {
        return ("MultiParamSurface.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public record RequestDto(string Name);
public record ResultDto(int Count);

public interface IMultiParamService
{
    System.Threading.Tasks.Task<ResultDto> ExecuteAsync(RequestDto request, System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IMultiParamService))]
public partial class MultiParamContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) ExplicitDuplicateSurface()
    {
        return ("ExplicitDuplicateSurface.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public record SharedDto(string Value);

public interface IFirstService
{
    System.Threading.Tasks.Task<SharedDto> FirstAsync(System.Threading.CancellationToken ct);
}

public interface ISecondService
{
    System.Threading.Tasks.Task<SharedDto> SecondAsync(SharedDto input, System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IFirstService))]
[JsonContractSurface(typeof(ISecondService))]
public partial class DuplicateContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) MultipleContextProject()
    {
        return ("MultipleContexts.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface IFirstContract
{
    System.Threading.Tasks.Task<string> FirstAsync(System.Threading.CancellationToken ct);
}

public interface ISecondContract
{
    System.Threading.Tasks.Task<int> SecondAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IFirstContract))]
public partial class FirstContext : JsonSerializerContext { }

[JsonContractSurface(typeof(ISecondContract))]
public partial class SecondContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) InvalidContext()
    {
        return ("InvalidContext.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface ISomeService
{
    System.Threading.Tasks.Task<string> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(ISomeService))]
public class NonPartialContext : JsonSerializerContext { }

public class OuterClass
{
    [JsonContractSurface(typeof(ISomeService))]
    public partial class NestedContext : JsonSerializerContext { }
}

[JsonContractSurface(typeof(ISomeService))]
public partial class GenericContext<T> : JsonSerializerContext { }");
    }

    public static (string Path, string Text) SameProjectUnresolvedType()
    {
        return ("UnresolvedType.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface IUnresolvedService
{
    System.Threading.Tasks.Task<UnresolvedDto> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IUnresolvedService))]
public partial class UnresolvedContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) NonInterfaceSurface()
    {
        return ("NonInterfaceSurface.cs", @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public class ConcreteClass
{
    public System.Threading.Tasks.Task<string> GetAsync(System.Threading.CancellationToken ct) => null!;
}

[JsonContractSurface(typeof(ConcreteClass))]
public partial class NonInterfaceContext : JsonSerializerContext { }");
    }

    public static (string Path, string Text) NoMarkedContext()
    {
        return ("NoMarkedContext.cs", @"
using System.Text.Json.Serialization;

public partial class UnmarkedContext : JsonSerializerContext { }");
    }
}
