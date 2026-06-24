using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class SchemaCompatibilityRuleTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static SchemaDescriptor MakeSchema(
        string id = "S1", int version = 1,
        Dictionary<string, SchemaFieldDescriptor>? fields = null,
        SchemaChangeKind changeKind = SchemaChangeKind.Additive,
        IReadOnlyList<VersionedDescriptorRef<SchemaDescriptor>>? references = null)
    {
        return new SchemaDescriptor
        {
            Id = id, Name = "TestSchema", Version = version,
            State = DescriptorState.Active,
            Fields = fields?.Values.ToArray() ?? Array.Empty<SchemaFieldDescriptor>(),
            References = references ?? Array.Empty<VersionedDescriptorRef<SchemaDescriptor>>(),
            ChangeKind = changeKind
        };
    }

    private static SchemaFieldDescriptor MakeField(string name, string fieldType = "string",
        bool isRequired = false, bool isNullable = true, int? maxLength = null)
    {
        return new SchemaFieldDescriptor
        {
            Name = name, FieldType = fieldType,
            IsRequired = isRequired, IsNullable = isNullable,
            MaxLength = maxLength
        };
    }

    private static DescriptorChange MakeContractChange(string id, int version,
        string beforeHash = "h1", string afterHash = "h2")
    {
        return new DescriptorChange
        {
            Ref = new DescriptorRef("schema", id, version),
            Kind = DescriptorChangeKind.ContractHashChanged,
            BeforeContractHash = beforeHash,
            AfterContractHash = afterHash
        };
    }

    private static DescriptorChange MakeDefinitionChange(string id, int version,
        string beforeDefHash = "d1", string afterDefHash = "d2",
        string beforeContractHash = "h1", string afterContractHash = "h1")
    {
        return new DescriptorChange
        {
            Ref = new DescriptorRef("schema", id, version),
            Kind = DescriptorChangeKind.DefinitionHashChanged,
            BeforeContractHash = beforeContractHash,
            AfterContractHash = afterContractHash,
            BeforeDefinitionHash = beforeDefHash,
            AfterDefinitionHash = afterDefHash
        };
    }

    private static DescriptorImpactAnalysisReport MakeImpactReport(
        DescriptorChangeSet changeSet, DescriptorRef changedRef,
        params DescriptorRef[] affectedRefs)
    {
        var paths = affectedRefs.Select(r => new DescriptorImpactPath
        {
            SourceChange = changedRef, Affected = r,
            Segments = Array.Empty<DescriptorImpactPathSegment>()
        }).ToArray();

        return new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = affectedRefs.Select(r => new AffectedDescriptor
            {
                Ref = r, Kind = DescriptorKind.Schema, Name = r.FullId,
                Severity = DescriptorImpactSeverity.Low,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Schema },
                Paths = paths.Where(p => p.Affected == r).ToArray()
            }).ToArray(),
            Paths = paths,
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };
    }

    [Fact]
    public void Schema_OptionalFieldAdded_Compatible()
    {
        var before = MakeSchema(fields: new());
        var after = MakeSchema(fields: new() { ["newField"] = MakeField("newField", isRequired: false) });
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_OPTIONAL_FIELD_ADDED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void Schema_RequiredFieldAdded_Breaking()
    {
        var before = MakeSchema(fields: new());
        var after = MakeSchema(fields: new() { ["reqField"] = MakeField("reqField", isRequired: true) });
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_REQUIRED_FIELD_ADDED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void Schema_FieldRemoved_BreakingWithConsumers()
    {
        var before = MakeSchema(fields: new() { ["oldField"] = MakeField("oldField") });
        var after = MakeSchema(fields: new());
        var consumer = new DescriptorRef("form", "F1", 1);
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref, consumer);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_FIELD_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void Schema_FieldTypeChanged_Breaking()
    {
        var before = MakeSchema(fields: new() { ["f"] = MakeField("f", fieldType: "string") });
        var after = MakeSchema(fields: new() { ["f"] = MakeField("f", fieldType: "int") });
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_FIELD_TYPE_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void Schema_RequiredRelaxed_Compatible()
    {
        var before = MakeSchema(fields: new() { ["f"] = MakeField("f", isRequired: true) });
        var after = MakeSchema(fields: new() { ["f"] = MakeField("f", isRequired: false) });
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_FIELD_REQUIRED_RELAXED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void Schema_MaxLengthNarrowed_Breaking()
    {
        var before = MakeSchema(fields: new() { ["f"] = MakeField("f", maxLength: 100) });
        var after = MakeSchema(fields: new() { ["f"] = MakeField("f", maxLength: 50) });
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_MAX_LENGTH_NARROWED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void Schema_MaxLengthRelaxed_Compatible()
    {
        var before = MakeSchema(fields: new() { ["f"] = MakeField("f", maxLength: 50) });
        var after = MakeSchema(fields: new() { ["f"] = MakeField("f", maxLength: 100) });
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_MAX_LENGTH_RELAXED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void Schema_DeclaredBreaking_UpgradesToBreaking()
    {
        var before = MakeSchema();
        var after = MakeSchema(changeKind: SchemaChangeKind.Breaking);
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_DECLARED_BREAKING" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void OptionalFieldAdded_WithDefinitionHashChanged_ReturnsCompatible()
    {
        var before = MakeSchema(fields: new());
        var after = MakeSchema(fields: new() { ["newField"] = MakeField("newField", isRequired: false) });
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_OPTIONAL_FIELD_ADDED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void OptionalFieldRemoved_WithAffectedConsumers_ReturnsBreaking()
    {
        var before = MakeSchema(fields: new() { ["oldField"] = MakeField("oldField") });
        var after = MakeSchema(fields: new());
        var consumer = new DescriptorRef("form", "F1", 1);
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref, consumer);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_FIELD_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void OptionalFieldRemoved_WithoutAffectedConsumers_ReturnsRisky()
    {
        var before = MakeSchema(fields: new() { ["oldField"] = MakeField("oldField") });
        var after = MakeSchema(fields: new());
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_FIELD_REMOVED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void OptionalFieldTypeChanged_WithDefinitionHashChanged_ReturnsBreaking()
    {
        var before = MakeSchema(fields: new() { ["f"] = MakeField("f", fieldType: "string") });
        var after = MakeSchema(fields: new() { ["f"] = MakeField("f", fieldType: "int") });
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_FIELD_TYPE_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void ValidationRuleChanged_WithDefinitionHashChanged_ReturnsRisky()
    {
        var before = MakeSchema(fields: new() { ["f"] = MakeField("f", fieldType: "string", maxLength: 50) });
        var after = MakeSchema(fields: new() { ["f"] = MakeField("f", fieldType: "string", maxLength: 30) });
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_MAX_LENGTH_NARROWED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void SchemaValidationRulesChanged_WithDefinitionHashChanged_ReturnsRisky()
    {
        // ValidationRules property change (not field-level validation like MaxLength)
        var before = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "f", FieldType = "string" } },
            ValidationRules = new[]
            {
                new SchemaValidationRule { Name = "email", Expression = @"^[^@]+@[^@]+$" }
            }
        };
        var after = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "f", FieldType = "string" } },
            ValidationRules = new[]
            {
                new SchemaValidationRule { Name = "email", Expression = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$" }
            }
        };
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_VALIDATION_RULES_CHANGED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void SchemaValidationRulesAdded_WithDefinitionHashChanged_ReturnsRisky()
    {
        var before = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = Array.Empty<SchemaFieldDescriptor>(),
            ValidationRules = Array.Empty<SchemaValidationRule>()
        };
        var after = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = Array.Empty<SchemaFieldDescriptor>(),
            ValidationRules = new[]
            {
                new SchemaValidationRule { Name = "required", Expression = "value != null" }
            }
        };
        var change = MakeDefinitionChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_VALIDATION_RULES_CHANGED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void SchemaValidationRulesChanged_WithContractHashChanged_ReturnsBreaking()
    {
        var before = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = Array.Empty<SchemaFieldDescriptor>(),
            ValidationRules = new[]
            {
                new SchemaValidationRule { Name = "email", Expression = @"^[^@]+@[^@]+$" }
            }
        };
        var after = new SchemaDescriptor
        {
            Id = "S1", Name = "TestSchema", Version = 1,
            State = DescriptorState.Active,
            Fields = Array.Empty<SchemaFieldDescriptor>(),
            ValidationRules = new[]
            {
                new SchemaValidationRule { Name = "email", Expression = @"^[a-z]+@[a-z]+\.[a-z]+$" }
            }
        };
        var change = MakeContractChange("S1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs, change.Ref);

        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, report);

        result.Findings.Should().Contain(f =>
            f.RuleId == "COMPAT_SCHEMA_VALIDATION_RULES_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }
}
