// PERIMETER — AI-generated scaffolding (ADR-0003). Compiler enforcement of
// ADR-0006: core code may not read time or randomness directly. Wired into
// src/Aurelia.Core only; violations are build errors.
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aurelia.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterminismAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AUR0006";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Core code may not read time or randomness",
        "Core code may not use '{0}': time and randomness must arrive as recorded events (ADR-0006)",
        "Aurelia.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Per ADR-0006, core execution logic never reads the wall clock or randomness "
            + "directly; such values are injected by the runtime and recorded as events so replay "
            + "stays deterministic.");

    // Any use of these types is banned in core.
    private static readonly ImmutableHashSet<string> BannedTypes = ImmutableHashSet.Create(
        "System.Random",
        "System.Security.Cryptography.RandomNumberGenerator",
        "System.Diagnostics.Stopwatch");

    // These specific members are banned; the rest of their types is fine
    // (e.g. DateTimeOffset values from events may be compared and formatted).
    private static readonly ImmutableHashSet<string> BannedMembers = ImmutableHashSet.Create(
        "System.DateTime.Now",
        "System.DateTime.UtcNow",
        "System.DateTime.Today",
        "System.DateTimeOffset.Now",
        "System.DateTimeOffset.UtcNow",
        "System.Guid.NewGuid",
        "System.Environment.TickCount",
        "System.Environment.TickCount64",
        "System.TimeProvider.System");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(
            AnalyzeOperation,
            OperationKind.Invocation,
            OperationKind.PropertyReference,
            OperationKind.ObjectCreation);
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        ISymbol? member;
        INamedTypeSymbol? type;
        switch (context.Operation)
        {
            case IInvocationOperation op:
                member = op.TargetMethod;
                type = op.TargetMethod.ContainingType;
                break;
            case IPropertyReferenceOperation op:
                member = op.Property;
                type = op.Property.ContainingType;
                break;
            case IObjectCreationOperation { Constructor: not null } op:
                member = op.Constructor;
                type = op.Constructor.ContainingType;
                break;
            default:
                return;
        }

        if (type is null)
            return;

        var typeName = FullName(type);
        string? offender = null;
        if (BannedTypes.Contains(typeName))
            offender = typeName;
        else if (BannedMembers.Contains($"{typeName}.{member.Name}"))
            offender = $"{typeName}.{member.Name}";

        if (offender is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(), offender));
        }
    }

    private static string FullName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? $"{ns.ToDisplayString()}.{type.Name}"
            : type.Name;
}
