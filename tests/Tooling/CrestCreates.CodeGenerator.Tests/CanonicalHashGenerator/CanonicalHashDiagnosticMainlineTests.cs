using System.Linq;
using CrestCreates.CodeGenerator.CanonicalHashGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CanonicalHashGenerator;

public sealed class CanonicalHashDiagnosticMainlineTests
{
    private static class TestSources
    {
        /// <summary>
        /// Minimal copies of canonical hash attribute declarations so that
        /// source generator tests work without a project reference to Metadata.Abstractions.
        /// Uses traditional namespace blocks (not file-scoped) so that the test body
        /// can also use a traditional namespace block in the same file.
        /// </summary>
        private const string AttributeDeclarations = @"
using System;

namespace CrestCreates.Metadata.Abstractions
{
    public enum CanonicalHashArtifactKind { Descriptor = 0, ReviewResult = 1, Package = 2, Report = 3 }
    public enum DescriptorKind { Unknown = 0, Schema = 1, Capability = 2 }
    public enum CanonicalHashFieldClassification { Contract = 1, DefinitionOnly = 2, Excluded = 3 }
    public enum CanonicalHashCollectionOrderMode { None = 0, SourceOrder = 1, OrdinalByValue = 2, OrdinalByProperty = 3, OrderedKeyValue = 4 }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CanonicalHashProfileAttribute : Attribute
    {
        public CanonicalHashArtifactKind ArtifactKind { get; init; } = CanonicalHashArtifactKind.Descriptor;
        public DescriptorKind DescriptorKind { get; init; } = DescriptorKind.Unknown;
        public required Type TargetType { get; init; }
        public required string ContractShapeVersion { get; init; }
        public required string DefinitionShapeVersion { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CanonicalHashUnionProfileAttribute : Attribute
    {
        public required Type TargetType { get; init; }
        public required string Discriminator { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CanonicalHashUnionCaseAttribute : Attribute
    {
        public CanonicalHashUnionCaseAttribute(Type caseType, string discriminatorValue)
        {
            CaseType = caseType;
            DiscriminatorValue = discriminatorValue;
        }
        public Type CaseType { get; }
        public string DiscriminatorValue { get; }
        public required Type ValueProfile { get; init; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class CanonicalHashFieldAttribute : Attribute
    {
        public CanonicalHashFieldAttribute(string propertyName, CanonicalHashFieldClassification classification)
        {
            PropertyName = propertyName;
            Classification = classification;
        }
        public string PropertyName { get; }
        public CanonicalHashFieldClassification Classification { get; }
        public int Order { get; init; }
        public Type? ElementProfile { get; init; }
        public Type? ValueProfile { get; init; }
        public Type? Filter { get; init; }
        public CanonicalHashCollectionOrderMode CollectionOrderMode { get; init; } = CanonicalHashCollectionOrderMode.None;
        public string? OrderByProperty { get; init; }
        public string? Reason { get; init; }
        public Type? CustomWriter { get; init; }
    }
}
";

        public static string WithProfiles(string body)
        {
            // Extract using directives from body and place them at the top of the file
            // so they come before all namespace blocks. C# requires using directives
            // to precede namespace members in the containing scope.
            var lines = body.Split('\n');
            var usings = new System.Collections.Generic.List<string>();
            var restLines = new System.Collections.Generic.List<string>();
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("using ") && !line.TrimStart().StartsWith("using ("))
                    usings.Add(line);
                else
                    restLines.Add(line);
            }
            return string.Join("\n", usings) + "\n" + AttributeDeclarations + "\n" + string.Join("\n", restLines);
        }
    }

    [Fact]
    public void CCHASH015_UnionProfileMissingTargetType_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }

    [CanonicalHashUnionProfile(Discriminator = "type")]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH015");
    }

    [Fact]
    public void CCHASH015_UnionProfileMissingDiscriminator_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }

    [CanonicalHashUnionProfile(TargetType = typeof(Target))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH015");
    }

    [Fact]
    public void CCHASH016_UnionCaseTypeNotAssignable_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Base { }
    public sealed class Unrelated { }

    [CanonicalHashProfile(
        TargetType = typeof(Unrelated),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class UnrelatedProfile
    {
        [CanonicalHashField("ToString", CanonicalHashFieldClassification.Excluded)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(Base), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Unrelated), "a", ValueProfile = typeof(UnrelatedProfile))]
    internal sealed class BaseUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH016");
    }

    [Fact]
    public void CCHASH017_UnionCaseMissingValueProfile_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }
    public sealed class Case1 : Target { public string Name { get; init; } = ""; }

    [CanonicalHashUnionProfile(TargetType = typeof(Target), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case1), "a" /* missing ValueProfile */)]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH017");
    }

    [Fact]
    public void CCHASH018_DuplicateUnionDiscriminator_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }
    public sealed class Case1 : Target { public string Name { get; init; } = ""; }
    public sealed class Case2 : Target { public int Value { get; init; } }

    [CanonicalHashProfile(
        TargetType = typeof(Case1),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case1Profile
    {
        [CanonicalHashField(nameof(Case1.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashProfile(
        TargetType = typeof(Case2),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case2Profile
    {
        [CanonicalHashField(nameof(Case2.Value), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(Target), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case1), "a", ValueProfile = typeof(Case1Profile))]
    [CanonicalHashUnionCase(typeof(Case2), "a", ValueProfile = typeof(Case2Profile))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH018");
    }

    [Fact]
    public void CCHASH019_DuplicateUnionCaseType_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }
    public sealed class Case1 : Target { public string Name { get; init; } = ""; }

    [CanonicalHashProfile(
        TargetType = typeof(Case1),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case1Profile
    {
        [CanonicalHashField(nameof(Case1.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(Target), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case1), "a", ValueProfile = typeof(Case1Profile))]
    [CanonicalHashUnionCase(typeof(Case1), "b", ValueProfile = typeof(Case1Profile))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH019");
    }

    [Fact]
    public void CCHASH020_UnionCaseTypeMustBeSealed_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }
    public class Case1 : Target { public string Name { get; init; } = ""; }

    [CanonicalHashProfile(
        TargetType = typeof(Case1),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case1Profile
    {
        [CanonicalHashField(nameof(Case1.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(Target), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case1), "a", ValueProfile = typeof(Case1Profile))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH020");
    }

    [Fact]
    public void CCHASH021_UnionCaseMissingKnownSubtype_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }
    public sealed class Case1 : Target { public string Name { get; init; } = ""; }
    public sealed class MissingCase : Target { public int Value { get; init; } }

    [CanonicalHashProfile(
        TargetType = typeof(Case1),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case1Profile
    {
        [CanonicalHashField(nameof(Case1.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(Target), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case1), "a", ValueProfile = typeof(Case1Profile))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH021");
    }

    [Fact]
    public void CCHASH022_UnionCaseValueProfileTargetMismatch_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public class Target { }
    public sealed class Case1 : Target { public string Name { get; init; } = ""; }
    public sealed class Case2 : Target { public int Value { get; init; } }

    [CanonicalHashProfile(
        TargetType = typeof(Case1),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case1Profile
    {
        [CanonicalHashField(nameof(Case1.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(Target), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case2), "a", ValueProfile = typeof(Case1Profile))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH022");
    }

    [Fact]
    public void CCHASH023_CustomWriterUsage_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public sealed class Target
    {
        public Nested Value { get; init; } = new();
    }

    public sealed class Nested
    {
        public string Id { get; init; } = "";
    }

    internal static class TargetWriter { }

    [CanonicalHashProfile(
        TargetType = typeof(Target),
        ContractShapeVersion = "target-contract-v1",
        DefinitionShapeVersion = "target-definition-v1")]
    internal sealed class TargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(Target.Value), CanonicalHashFieldClassification.Contract, CustomWriter = typeof(TargetWriter))]
        private static void Fields() { }
    }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH023");
    }

    [Fact]
    public void CCHASH024_FilterOnlyForCollection_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;
using System.Collections.Generic;

namespace TestNamespace
{
    public sealed class Target
    {
        public string Name { get; init; } = "";
    }

    public static class SomeFilter { public static bool Include(string value) => true; }

    [CanonicalHashProfile(
        TargetType = typeof(Target),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class TargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(Target.Name), CanonicalHashFieldClassification.Contract, Filter = typeof(SomeFilter))]
        private static void Fields() { }
    }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH024");
    }

    [Fact]
    public void CCHASH025_InvalidFilterSignature_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;
using System.Collections.Generic;

namespace TestNamespace
{
    public sealed class Target
    {
        public List<string> Items { get; init; } = new();
    }

    public static class BadFilter
    {
        public static bool Include() => true;
    }

    [CanonicalHashProfile(
        TargetType = typeof(Target),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class TargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(Target.Items), CanonicalHashFieldClassification.Contract,
            CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder,
            Filter = typeof(BadFilter))]
        private static void Fields() { }
    }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH025");
    }

    [Fact]
    public void CCHASH026_FilterElementTypeMismatch_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;
using System.Collections.Generic;

namespace TestNamespace
{
    public sealed class Target
    {
        public List<string> Items { get; init; } = new();
    }

    public static class MismatchFilter
    {
        public static bool Include(int value) => true;
    }

    [CanonicalHashProfile(
        TargetType = typeof(Target),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class TargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(Target.Items), CanonicalHashFieldClassification.Contract,
            CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder,
            Filter = typeof(MismatchFilter))]
        private static void Fields() { }
    }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH026");
    }

    [Fact]
    public void CCHASH021_UnionCaseMissingKnownSubtype_AbstractBase_ShouldEmitError()
    {
        // Abstract base type — CCHASH021 must still detect missing sealed subtypes
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public abstract class TargetBase { }
    public sealed class Case1 : TargetBase { public string Name { get; init; } = ""; }
    public sealed class MissingCase : TargetBase { public int Value { get; init; } }

    [CanonicalHashProfile(
        TargetType = typeof(Case1),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class Case1Profile
    {
        [CanonicalHashField(nameof(Case1.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(TargetBase), Discriminator = "type")]
    [CanonicalHashUnionCase(typeof(Case1), "a", ValueProfile = typeof(Case1Profile))]
    internal sealed class TargetUnionProfile { }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH021");
    }

    [Fact]
    public void CCHASH027_FilterOnDictionaryField_ShouldEmitError()
    {
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;
using System.Collections.Generic;

namespace TestNamespace
{
    public sealed class Target
    {
        public Dictionary<string, string> Props { get; init; } = new();
    }

    public static class DictFilter
    {
        public static bool Include(string value) => true;
    }

    [CanonicalHashProfile(
        TargetType = typeof(Target),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class TargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(Target.Props), CanonicalHashFieldClassification.Contract,
            Filter = typeof(DictFilter))]
        private static void Fields() { }
    }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CCHASH027");
    }

    [Fact]
    public void CCHASH028_UnsupportedScalarType_ShouldEmitError()
    {
        // A field with an unsupported scalar type (not string/int/long/bool/DateTime/double/float/decimal/enum/TimeSpan)
        // and no ElementProfile/ValueProfile should emit CCHASH004 (complex field requires profile).
        // CCHASH028 is a safety net in the writer — unsupported scalars that slip through IsComplexType
        // produce a #error in generated code. This test verifies the model-layer rejection.
        var source = TestSources.WithProfiles("""
using CrestCreates.Metadata.Abstractions;

namespace TestNamespace
{
    public sealed class Target
    {
        public Guid Uuid { get; init; }
    }

    [CanonicalHashProfile(
        TargetType = typeof(Target),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class TargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(Target.Uuid), CanonicalHashFieldClassification.Contract, Order = 1)]
        private static void Fields() { }
    }
}
""");

        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);
        var errors = result.GetErrors().ToList();

        // Guid is not a supported scalar type → CCHASH004 (complex field requires profile)
        Assert.Contains(errors, e => e.Id == "CCHASH004");
    }
}
