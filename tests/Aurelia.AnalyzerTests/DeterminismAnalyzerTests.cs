// PERIMETER — tests for the ADR-0006 determinism analyzer. Each case compiles
// a small snippet with the analyzer attached and checks for AUR0006.
using System.Collections.Immutable;
using Aurelia.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Aurelia.AnalyzerTests;

public class DeterminismAnalyzerTests
{
    [Theory]
    [InlineData("var t = System.DateTime.UtcNow;")]
    [InlineData("var t = System.DateTime.Now;")]
    [InlineData("var t = System.DateTimeOffset.UtcNow;")]
    [InlineData("var r = new System.Random();")]
    [InlineData("var r = System.Random.Shared.Next();")]
    [InlineData("var g = System.Guid.NewGuid();")]
    [InlineData("var t = System.Environment.TickCount64;")]
    [InlineData("var s = System.Diagnostics.Stopwatch.StartNew();")]
    public async Task Banned_api_is_reported(string statement)
    {
        var diagnostics = await AnalyzeAsync(statement);
        Assert.Contains(diagnostics, d => d.Id == DeterminismAnalyzer.DiagnosticId);
    }

    [Theory]
    [InlineData("var x = 1 + 1;")]
    [InlineData("System.DateTimeOffset recorded = default; var ticks = recorded.Ticks;")]
    [InlineData("System.DateTimeOffset a = default, b = default; var later = a > b;")]
    [InlineData("var g = System.Guid.Parse(\"00000000-0000-0000-0000-000000000000\");")]
    public async Task Deterministic_code_is_not_reported(string statement)
    {
        var diagnostics = await AnalyzeAsync(statement);
        Assert.DoesNotContain(diagnostics, d => d.Id == DeterminismAnalyzer.DiagnosticId);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string statements)
    {
        var source = $$"""
            public class CoreUnderTest
            {
                public void Method()
                {
                    {{statements}}
                }
            }
            """;

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        var references = trustedAssemblies
            .Where(path => Path.GetFileName(path)
                is "System.Private.CoreLib.dll" or "System.Runtime.dll" or "netstandard.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "CoreUnderTest",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Guard the rig: a snippet that fails to compile would silently skip
        // symbol resolution and fake a pass.
        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(compileErrors.Count == 0,
            $"test snippet does not compile: {string.Join("; ", compileErrors)}");

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DeterminismAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
