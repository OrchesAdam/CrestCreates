using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Build.Framework;
using Moq;
using Xunit;

namespace CrestCreates.BuildTasks.Tests;

public class ScanEntityPermissionsTests : IDisposable
{
    private readonly string _testOutputPath;
    private readonly string _testSourceDir;
    private readonly Mock<IBuildEngine> _buildEngineMock;

    public ScanEntityPermissionsTests()
    {
        _testOutputPath = Path.Combine(Path.GetTempPath(), $"manifest_{Guid.NewGuid()}.json");
        _testSourceDir = Path.Combine(Path.GetTempPath(), $"sources_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testSourceDir);

        _buildEngineMock = new Mock<IBuildEngine>();
        _buildEngineMock.Setup(x => x.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()));
        _buildEngineMock.Setup(x => x.LogWarningEvent(It.IsAny<BuildWarningEventArgs>()));
        _buildEngineMock.Setup(x => x.LogMessageEvent(It.IsAny<BuildMessageEventArgs>()));
        _buildEngineMock.Setup(x => x.LogCustomEvent(It.IsAny<CustomBuildEventArgs>()));
    }

    public void Dispose()
    {
        if (File.Exists(_testOutputPath))
        {
            File.Delete(_testOutputPath);
        }

        if (Directory.Exists(_testSourceDir))
        {
            Directory.Delete(_testSourceDir, true);
        }
    }

    private ITaskItem CreateTaskItem(string path)
    {
        var mock = new Mock<ITaskItem>();
        mock.Setup(x => x.ItemSpec).Returns(path);
        return mock.Object;
    }

    private ScanEntityPermissions CreateTask()
    {
        return new ScanEntityPermissions
        {
            BuildEngine = _buildEngineMock.Object
        };
    }

    private string CreateTestSourceFile(string className, string entityName, List<string> permissions)
    {
        var filePath = Path.Combine(_testSourceDir, $"{className}.cs");

        var permissionConstants = string.Join("\n        ", permissions.ConvertAll(p =>
        {
            var parts = p.Split('.');
            var actionName = parts.Length > 1 ? parts[1] : parts[0];
            return $"public const string {actionName} = \"{p}\";";
        }));

        var content = $@"using System.Collections.Generic;
using CrestCreates.Domain.Shared.Permissions;

namespace TestNamespace.Domain.Permissions
{{
    public partial class {className} : IEntityPermissions
    {{
        {permissionConstants}

        public string EntityName => ""{entityName}"";

        public IEnumerable<string> GetAllPermissions()
        {{
            yield return Create;
            yield return Update;
            yield return Delete;
        }}

        public static {className} Instance {{ get; }} = new {className}();
    }}
}}";

        File.WriteAllText(filePath, content);
        return filePath;
    }

    private string CreateTestSourceFileWithPropertyEntityName(string className, string entityName, List<string> permissions)
    {
        var filePath = Path.Combine(_testSourceDir, $"{className}_Property.cs");

        var permissionConstants = string.Join("\n        ", permissions.ConvertAll(p =>
        {
            var parts = p.Split('.');
            var actionName = parts.Length > 1 ? parts[1] : parts[0];
            return $"public const string {actionName} = \"{p}\";";
        }));

        var content = $@"using System.Collections.Generic;
using CrestCreates.Domain.Shared.Permissions;

namespace TestNamespace.Domain.Permissions
{{
    public partial class {className} : IEntityPermissions
    {{
        {permissionConstants}

        public string EntityName {{ get; }} = ""{entityName}"";

        public IEnumerable<string> GetAllPermissions()
        {{
            yield return Create;
        }}
    }}
}}";

        File.WriteAllText(filePath, content);
        return filePath;
    }

    private string CreateNonPermissionSourceFile()
    {
        var filePath = Path.Combine(_testSourceDir, "RegularClass.cs");
        var content = @"namespace TestNamespace.Domain.Entities
{
    public class RegularClass
    {
        public string Name { get; set; }
    }
}";
        File.WriteAllText(filePath, content);
        return filePath;
    }

    [Fact]
    public void Execute_WithValidPermissionFiles_ShouldGenerateManifest()
    {
        var filePath1 = CreateTestSourceFile("BookPermissions", "Book", 
            new List<string> { "Book.Create", "Book.Update", "Book.Delete" });
        var filePath2 = CreateTestSourceFile("MemberPermissions", "Member",
            new List<string> { "Member.Create", "Member.Update" });

        var task = CreateTask();
        task.SourceFiles = new[] { CreateTaskItem(filePath1), CreateTaskItem(filePath2) };
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();
        File.Exists(_testOutputPath).Should().BeTrue();

        var json = File.ReadAllText(_testOutputPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        manifest.GetProperty("version").GetString().Should().Be("1.0");
        manifest.GetProperty("permissions").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Execute_WithPropertyEntityName_ShouldExtractCorrectly()
    {
        var filePath = CreateTestSourceFileWithPropertyEntityName("LoanPermissions", "Loan",
            new List<string> { "Loan.Create" });

        var task = CreateTask();
        task.SourceFiles = new[] { CreateTaskItem(filePath) };
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();

        var json = File.ReadAllText(_testOutputPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        var permissions = manifest.GetProperty("permissions");
        permissions[0].GetProperty("entityName").GetString().Should().Be("Loan");
    }

    [Fact]
    public void Execute_WithNonPermissionFiles_ShouldSkipThem()
    {
        var permissionFile = CreateTestSourceFile("BookPermissions", "Book",
            new List<string> { "Book.Create" });
        var regularFile = CreateNonPermissionSourceFile();

        var task = CreateTask();
        task.SourceFiles = new[] { CreateTaskItem(permissionFile), CreateTaskItem(regularFile) };
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();

        var json = File.ReadAllText(_testOutputPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        manifest.GetProperty("permissions").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Execute_WithEmptySourceFiles_ShouldGenerateEmptyManifest()
    {
        var task = CreateTask();
        task.SourceFiles = Array.Empty<ITaskItem>();
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();
        File.Exists(_testOutputPath).Should().BeTrue();

        var json = File.ReadAllText(_testOutputPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        manifest.GetProperty("permissions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Execute_ShouldExtractCorrectNamespace()
    {
        var filePath = CreateTestSourceFile("BookPermissions", "Book",
            new List<string> { "Book.Create" });

        var task = CreateTask();
        task.SourceFiles = new[] { CreateTaskItem(filePath) };
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();

        var json = File.ReadAllText(_testOutputPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        var permissions = manifest.GetProperty("permissions");
        permissions[0].GetProperty("namespace").GetString().Should().Be("TestNamespace.Domain.Permissions");
    }

    [Fact]
    public void Execute_ShouldExtractAllPermissionConstants()
    {
        var filePath = CreateTestSourceFile("BookPermissions", "Book",
            new List<string> { "Book.Create", "Book.Update", "Book.Delete", "Book.Search", "Book.Export" });

        var task = CreateTask();
        task.SourceFiles = new[] { CreateTaskItem(filePath) };
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();

        var json = File.ReadAllText(_testOutputPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(json);

        var permissions = manifest.GetProperty("permissions");
        var permissionList = permissions[0].GetProperty("permissions");

        permissionList.GetArrayLength().Should().Be(5);
    }

    [Fact]
    public void Execute_WithNonExistentFile_ShouldSkipGracefully()
    {
        var validFile = CreateTestSourceFile("BookPermissions", "Book",
            new List<string> { "Book.Create" });
        var nonExistentFile = Path.Combine(_testSourceDir, "NonExistent.cs");

        var task = CreateTask();
        task.SourceFiles = new[] { CreateTaskItem(validFile), CreateTaskItem(nonExistentFile) };
        task.OutputPath = _testOutputPath;

        var result = task.Execute();

        result.Should().BeTrue();
    }

    [Fact]
    public void Execute_ShouldCreateOutputDirectoryIfNotExists()
    {
        var nestedOutputPath = Path.Combine(Path.GetTempPath(), $"nested_{Guid.NewGuid()}", "manifest.json");
        var filePath = CreateTestSourceFile("BookPermissions", "Book",
            new List<string> { "Book.Create" });

        try
        {
            var task = CreateTask();
            task.SourceFiles = new[] { CreateTaskItem(filePath) };
            task.OutputPath = nestedOutputPath;

            var result = task.Execute();

            result.Should().BeTrue();
            File.Exists(nestedOutputPath).Should().BeTrue();
        }
        finally
        {
            var dir = Path.GetDirectoryName(nestedOutputPath);
            if (dir != null && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
