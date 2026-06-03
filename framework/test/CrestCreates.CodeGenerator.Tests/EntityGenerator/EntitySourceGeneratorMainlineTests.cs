using Xunit;
using CrestCreates.CodeGenerator.EntityGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.EntityGenerator
{
    public class EntitySourceGeneratorMainlineTests
    {
        [Fact]
        public void GeneratedUpdateDtoAndApplyTo_ShouldUseTheSameInputFieldSet()
        {
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    public interface IMultiTenant
    {
        string? TenantId { get; set; }
    }

    public interface IHasConcurrencyStamp
    {
        string ConcurrencyStamp { get; set; }
    }

    [Entity(GenerateRepository = false, GeneratePermissions = false)]
    public class RegressionCustomer : IMultiTenant, IHasConcurrencyStamp
    {
        public Guid Id { get; set; }
        public string? TenantId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletionTime { get; set; }
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
        public string ConcurrencyStamp { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public RegressionCustomerProfile Profile { get; set; } = new();
    }

    public class RegressionCustomerProfile
    {
        public string DisplayName { get; set; } = string.Empty;
    }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<EntitySourceGenerator>(source);

            Assert.True(result.ContainsFile("UpdateRegressionCustomerDto.g.cs"));
            Assert.True(result.ContainsFile("RegressionCustomerObjectMappings.g.cs"));

            var updateDto = result.GetSourceByFileName("UpdateRegressionCustomerDto.g.cs");
            Assert.NotNull(updateDto);
            Assert.DoesNotContain("source.Id", updateDto!.SourceText);
            Assert.True(updateDto.SourceText.Contains("ConcurrencyStamp { get; set; }"), updateDto.SourceText);
            Assert.Contains("public string Name { get; set; }", updateDto.SourceText);
            Assert.DoesNotContain("TenantId", updateDto.SourceText);
            Assert.DoesNotContain("IsDeleted", updateDto.SourceText);
            Assert.DoesNotContain("DeletionTime", updateDto.SourceText);
            Assert.DoesNotContain("CreationTime", updateDto.SourceText);
            Assert.DoesNotContain("CreatorId", updateDto.SourceText);
            Assert.DoesNotContain("LastModificationTime", updateDto.SourceText);
            Assert.DoesNotContain("LastModifierId", updateDto.SourceText);
            Assert.DoesNotContain("Profile", updateDto.SourceText);

            var mapping = result.GetSourceByFileName("RegressionCustomerObjectMappings.g.cs");
            Assert.NotNull(mapping);
            Assert.Contains("[GenerateObjectMapping(typeof(global::TestNamespace.RegressionCustomer), typeof(RegressionCustomerDto))]", mapping!.SourceText);
            Assert.Contains("[GenerateObjectMapping(typeof(CreateRegressionCustomerDto), typeof(global::TestNamespace.RegressionCustomer), Direction = MapDirection.Create)]", mapping.SourceText);
            Assert.Contains("[GenerateObjectMapping(typeof(UpdateRegressionCustomerDto), typeof(global::TestNamespace.RegressionCustomer), Direction = MapDirection.Apply)]", mapping.SourceText);
            Assert.Contains("public static partial class RegressionCustomerObjectMappings", mapping.SourceText);
            Assert.DoesNotContain("destination.Name = source.Name;", mapping.SourceText);
            Assert.DoesNotContain("destination.Id = source.Id;", mapping.SourceText);
            Assert.DoesNotContain("destination.ConcurrencyStamp = source.ConcurrencyStamp;", mapping.SourceText);
            Assert.DoesNotContain("destination.TenantId = source.TenantId;", mapping.SourceText);
            Assert.DoesNotContain("destination.IsDeleted = source.IsDeleted;", mapping.SourceText);
            Assert.DoesNotContain("destination.DeletionTime = source.DeletionTime;", mapping.SourceText);
            Assert.DoesNotContain("destination.CreationTime = source.CreationTime;", mapping.SourceText);
            Assert.DoesNotContain("destination.CreatorId = source.CreatorId;", mapping.SourceText);
            Assert.DoesNotContain("destination.LastModificationTime = source.LastModificationTime;", mapping.SourceText);
            Assert.DoesNotContain("destination.LastModifierId = source.LastModifierId;", mapping.SourceText);
            Assert.DoesNotContain("destination.Profile = source.Profile;", mapping.SourceText);
        }

        [Fact]
        public void GeneratedCreateAndUpdateDtos_ShouldIncludePublicReadablePrivateSetterInputs()
        {
            const string source = """
using System;
using System.Collections.Generic;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    public interface IMultiTenant
    {
        string? TenantId { get; set; }
    }

    public interface IHasConcurrencyStamp
    {
        string ConcurrencyStamp { get; set; }
    }

    [Entity(GenerateRepository = false, GeneratePermissions = false)]
    public class RegressionTicket : IMultiTenant, IHasConcurrencyStamp
    {
        public Guid Id { get; set; }
        public string? TenantId { get; set; }
        public string ConcurrencyStamp { get; set; } = string.Empty;

        public string Title { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public RegressionPriority Priority { get; private set; }
        public Guid CustomerId { get; private set; }

        public RegressionCustomer? Customer { get; private set; }
        public string ComputedDisplayName => $"{Title} / {CustomerId}";
        public string ReadOnlyCode { get; } = string.Empty;
        public List<RegressionCustomer> RelatedCustomers { get; private set; } = [];
    }

    public class RegressionCustomer
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public enum RegressionPriority
    {
        Low = 0,
        Normal = 1,
        High = 2
    }
}
""";

            var result = SourceGeneratorTestHelper.RunGenerator<EntitySourceGenerator>(source);

            var createDto = result.GetSourceByFileName("CreateRegressionTicketDto.g.cs");
            var updateDto = result.GetSourceByFileName("UpdateRegressionTicketDto.g.cs");

            Assert.NotNull(createDto);
            Assert.NotNull(updateDto);

            Assert.Contains("public string Title { get; set; }", createDto!.SourceText);
            Assert.Contains("public string? Description { get; set; }", createDto.SourceText);
            Assert.Contains("Priority { get; set; }", createDto.SourceText);
            Assert.Contains("public System.Guid CustomerId { get; set; }", createDto.SourceText);
            Assert.DoesNotContain("Customer { get; set; }", createDto.SourceText);
            Assert.DoesNotContain("ComputedDisplayName", createDto.SourceText);
            Assert.DoesNotContain("ReadOnlyCode", createDto.SourceText);
            Assert.DoesNotContain("RelatedCustomers", createDto.SourceText);
            Assert.DoesNotContain("TenantId", createDto.SourceText);
            Assert.DoesNotContain("ConcurrencyStamp", createDto.SourceText);

            Assert.Contains("Id { get; set; }", updateDto!.SourceText);
            Assert.Contains("public string Title { get; set; }", updateDto.SourceText);
            Assert.Contains("public string? Description { get; set; }", updateDto.SourceText);
            Assert.Contains("Priority { get; set; }", updateDto.SourceText);
            Assert.Contains("public System.Guid CustomerId { get; set; }", updateDto.SourceText);
            Assert.Contains("public string ConcurrencyStamp { get; set; }", updateDto.SourceText);
            Assert.DoesNotContain("Customer { get; set; }", updateDto.SourceText);
            Assert.DoesNotContain("ComputedDisplayName", updateDto.SourceText);
            Assert.DoesNotContain("ReadOnlyCode", updateDto.SourceText);
            Assert.DoesNotContain("RelatedCustomers", updateDto.SourceText);
            Assert.DoesNotContain("TenantId", updateDto.SourceText);
        }

        [Fact]
        public void GeneratedObjectMappings_ForDomainEntities_ShouldUseApplicationContractsDtosNamespace()
        {
            const string source = """
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace Sample.Domain.Entities;

[Entity(GenerateRepository = false, GeneratePermissions = false)]
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
""";

            var result = SourceGeneratorTestHelper.RunGenerator<EntitySourceGenerator>(source);

            var mapping = result.GetSourceByFileName("CategoryObjectMappings.g.cs");
            Assert.NotNull(mapping);
            Assert.Contains("using Sample.Application.Contracts.DTOs;", mapping!.SourceText);
            Assert.DoesNotContain("using Sample.Domain.Entities.Dtos;", mapping.SourceText);
            Assert.Contains("[GenerateObjectMapping(typeof(global::Sample.Domain.Entities.Category), typeof(CategoryDto))]", mapping.SourceText);
        }
    }
}
