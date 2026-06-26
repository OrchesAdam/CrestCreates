using System.Linq;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

// semantic-string-guard: allow

namespace CrestCreates.CodeGenerator.Tests.ObjectMappingGenerator
{
    public class ObjectMappingDiagnosticMainlineTests
    {
        [Fact]
        public void OM001_TargetPropertyNotMapped_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Name { get; set; } = string.Empty; }
    public class Target
    {
        public string Name { get; set; } = string.Empty;
        public string Extra { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM001");
        }

        [Fact]
        public void OM002_SourcePropertyNotFound_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Name { get; set; } = string.Empty; }
    public class Target
    {
        [MapFrom(""Missing"")]
        public string Value { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM002");
        }

        [Fact]
        public void OM003_AmbiguousMapping_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;

        [MapName(""Name"")]
        public string DisplayName { get; set; } = string.Empty;
    }

    public class Target
    {
        public string Name { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM003");
        }

        [Fact]
        public void OM004_TypeIncompatibility_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public int Value { get; set; } }
    public class Target { public System.DateTime Value { get; set; } }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM004");
        }

        [Fact]
        public void OM005_NullabilityUnsafe_ShouldEmitError()
        {
            var source = @"
#nullable enable
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string? Name { get; set; } }
    public class Target { public string Name { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM005");
        }

        [Fact]
        public void OM006_ReadOnlyTarget_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Name { get; set; } = string.Empty; }
    public class Target { public string Name { get; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(Target), Direction = MapDirection.Apply)]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM006");
        }

        [Fact]
        public void OM007_MissingElementMapping_ShouldEmitError()
        {
            var source = @"
using System.Collections.Generic;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public List<string> Items { get; set; } = new(); }
    public class Target { public List<int> Items { get; set; } = new(); }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM007");
        }

        [Fact]
        public void OM008_InvalidNavigationPath_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book { public string Title { get; set; } = string.Empty; }
    public class BookDto
    {
        [MapFrom(""Author.Name"")]
        public string AuthorName { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM008");
        }

        [Fact]
        public void OM009_ProtectedInputField_ShouldEmitError()
        {
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateDto
    {
        [MapName(""CreationTime"")]
        public DateTime ClientCreationTime { get; set; }
    }

    public class Entity
    {
        public DateTime CreationTime { get; set; }
    }

    [GenerateObjectMapping(typeof(UpdateDto), typeof(Entity), Direction = MapDirection.Apply)]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM009");
        }

        [Fact]
        public void OM100_SourceTypeNotFound_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Target { public string Name { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(NonExistentType), typeof(Target))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM100");
        }

        [Fact]
        public void OM101_TargetTypeNotFound_ShouldEmitError()
        {
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Name { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(NonExistentType))]
    public static partial class TestMapper { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);
            var errors = result.GetErrors().ToList();

            Assert.Contains(errors, e => e.Id == "OM101");
        }
    }
}
