using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Form.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class FormCompatibilityRuleTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static FormDescriptor MakeForm(string id = "F1", int version = 1,
        Dictionary<string, FormFieldDescriptor>? fields = null,
        string schemaId = "S1", int schemaVersion = 1)
    {
        return new FormDescriptor
        {
            Id = id, Name = "TestForm", Version = version,
            State = DescriptorState.Active,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = schemaId, Version = schemaVersion },
            Fields = fields?.Values.ToArray() ?? Array.Empty<FormFieldDescriptor>()
        };
    }

    private static FormFieldDescriptor MakeField(string schemaFieldName,
        bool? isRequiredOverride = null, string? controlType = null)
    {
        return new FormFieldDescriptor { SchemaFieldName = schemaFieldName, IsRequiredOverride = isRequiredOverride, ControlType = controlType };
    }

    private static DescriptorImpactAnalysisReport MakeImpactReport(
        DescriptorChangeSet changeSet, DescriptorRef changedRef, params DescriptorRef[] affectedRefs)
    {
        var paths = affectedRefs.Select(r => new DescriptorImpactPath { SourceChange = changedRef, Affected = r, Segments = Array.Empty<DescriptorImpactPathSegment>() }).ToArray();
        return new DescriptorImpactAnalysisReport { ChangeSet = changeSet, AffectedDescriptors = affectedRefs.Select(r => new AffectedDescriptor { Ref = r, Kind = DescriptorKind.Form, Name = r.FullId, Severity = DescriptorImpactSeverity.Low, RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Form }, Paths = paths.Where(p => p.Affected == r).ToArray() }).ToArray(), Paths = paths, MaxSeverity = DescriptorImpactSeverity.Low, Diagnostics = Array.Empty<DescriptorImpactDiagnostic>() };
    }

    private static DescriptorChange MakeChange(string id, int version) => new()
    {
        Ref = new DescriptorRef("form", id, version), Kind = DescriptorChangeKind.ContractHashChanged,
        BeforeContractHash = "h1", AfterContractHash = "h2"
    };

    [Fact] public void Form_SchemaRefChanged_Breaking()
    {
        var before = MakeForm(schemaId: "S1");
        var after = MakeForm(schemaId: "S2");
        var change = MakeChange("F1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_FORM_SCHEMA_CHANGED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Form_FieldRemoved_BreakingWithConsumers()
    {
        var before = MakeForm(fields: new() { ["field1"] = MakeField("field1") });
        var after = MakeForm(fields: new());
        var change = MakeChange("F1", 1);
        var consumer = new DescriptorRef("humantask", "H1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref, consumer));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_FORM_FIELD_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Form_FieldAdded_Compatible()
    {
        var before = MakeForm(fields: new());
        var after = MakeForm(fields: new() { ["newField"] = MakeField("newField") });
        var change = MakeChange("F1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_FORM_FIELD_ADDED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact] public void Form_RequiredOverrideAdded_Breaking()
    {
        var before = MakeForm(fields: new() { ["field1"] = MakeField("field1") });
        var after = MakeForm(fields: new() { ["field1"] = MakeField("field1", isRequiredOverride: true) });
        var change = MakeChange("F1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_FORM_REQUIRED_OVERRIDE_ADDED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact] public void Form_ControlTypeChanged_Risky()
    {
        var before = MakeForm(fields: new() { ["field1"] = MakeField("field1") });
        var after = MakeForm(fields: new() { ["field1"] = MakeField("field1", controlType: "dropdown") });
        var change = MakeChange("F1", 1);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var result = Analyzer.Analyze(new IDescriptor[] { before }, new IDescriptor[] { after }, cs, MakeImpactReport(cs, change.Ref));
        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_FORM_CONTROL_CHANGED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }
}
