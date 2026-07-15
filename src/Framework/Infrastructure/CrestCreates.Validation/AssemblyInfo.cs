using System.Diagnostics.CodeAnalysis;

// Tier 3: FluentValidation is reflection-based and not AOT-compatible.
// This project declares its own AOT readiness level separately.
[assembly: UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Tier 3 (Validation/FluentValidation): AOT capability separately declared. FluentValidation is inherently reflection-based.")]
[assembly: UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "Tier 3 (Validation/FluentValidation): AOT capability separately declared.")]
