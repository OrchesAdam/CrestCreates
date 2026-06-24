// Abstractions sub-namespaces (no type conflicts)
global using CrestCreates.Metadata.Abstractions.Bootstrap;
global using CrestCreates.Metadata.Abstractions.CanonicalHashing;
global using CrestCreates.Metadata.Abstractions.DescriptorBinding;
global using CrestCreates.Metadata.Abstractions.DescriptorCapability;
global using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
global using CrestCreates.Metadata.Abstractions.Registry;

// Metadata sub-namespaces (no type conflicts)
global using CrestCreates.Metadata.Bootstrap;
global using CrestCreates.Metadata.CanonicalHashing;
global using CrestCreates.Metadata.DescriptorImpact;
global using CrestCreates.Metadata.Registry;

// Type aliases for types whose namespace would shadow type name
// NOTE: DescriptorRelationship alias must be at file-level in test files because the global using
// alias can't override namespace imports in some C# versions.
global using DescriptorPackage = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage;
global using DescriptorPackageEvidence = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageEvidence;
global using DescriptorPackageRelationshipEntry = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageRelationshipEntry;
global using DescriptorPackageDiagnosticCode = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageDiagnosticCode;
global using EvidenceFinding = CrestCreates.Metadata.Abstractions.Evidence.EvidenceFinding;
global using EvidenceFindingCount = CrestCreates.Metadata.Abstractions.Evidence.EvidenceFindingCount;

// Specific type aliases from Metadata.Descriptor* namespaces (interfaces)
global using IDescriptorPackageBuilder = CrestCreates.Metadata.Abstractions.DescriptorPackage.IDescriptorPackageBuilder;
global using IDescriptorPackageDiffer = CrestCreates.Metadata.Abstractions.DescriptorPackage.IDescriptorPackageDiffer;
global using IDescriptorPackageSerializer = CrestCreates.Metadata.Abstractions.DescriptorPackage.IDescriptorPackageSerializer;
global using DescriptorPackageBuildRequest = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageBuildRequest;

// Metadata implementation types (from namespaces that otherwise would shadow types)
global using BindingStatusSynthesizer = CrestCreates.Metadata.DescriptorBinding.BindingStatusSynthesizer;
global using BootstrapCoordinator = CrestCreates.Metadata.Bootstrap.BootstrapCoordinator;
global using CapabilityRegistry = CrestCreates.Metadata.DescriptorCapability.CapabilityRegistry;
global using DefaultDescriptorRelationshipProvider = CrestCreates.Metadata.DescriptorRelationship.DefaultDescriptorRelationshipProvider;
global using DefaultDescriptorRuntimeBindingStatusProvider = CrestCreates.Metadata.DescriptorBinding.DefaultDescriptorRuntimeBindingStatusProvider;
global using SchemaRelationshipExtractor = CrestCreates.Metadata.DescriptorRelationship.SchemaRelationshipExtractor;

// Metadata.DescriptorPackage implementation types
global using DefaultDescriptorPackageBuilder = CrestCreates.Metadata.DescriptorPackage.DefaultDescriptorPackageBuilder;
global using DescriptorManifestSerializer = CrestCreates.Metadata.DescriptorPackage.DescriptorManifestSerializer;
global using DescriptorPackageDiffer = CrestCreates.Metadata.DescriptorPackage.DescriptorPackageDiffer;
global using DescriptorPackageHashComputer = CrestCreates.Metadata.DescriptorPackage.DescriptorPackageHashComputer;
global using DescriptorPackageSerializer = CrestCreates.Metadata.DescriptorPackage.DescriptorPackageSerializer;
