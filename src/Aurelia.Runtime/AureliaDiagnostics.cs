// PERIMETER — AI-generated scaffolding (ADR-0003). The single ActivitySource
// for runtime traces; span names and attributes follow the OTel GenAI
// semantic conventions where they apply.
using System.Diagnostics;

namespace Aurelia.Runtime;

public static class AureliaDiagnostics
{
    public const string SourceName = "Aurelia.Runtime";

    public static readonly ActivitySource Source = new(SourceName);
}
