using System.Text.Json.Serialization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PostgreSqlDescriptorDraftDocument))]
[JsonSerializable(typeof(OrganizationUnit))]
[JsonSerializable(typeof(Position))]
[JsonSerializable(typeof(UserOrganizationMembership))]
[JsonSerializable(typeof(UserOrganizationRoleAssignment))]
internal sealed partial class PostgreSqlControlPlaneReferenceDataJsonSerializerContext : JsonSerializerContext
{
}
