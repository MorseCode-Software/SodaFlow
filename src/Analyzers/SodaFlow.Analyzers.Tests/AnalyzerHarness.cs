using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Compiles a sample of code against the library, runs one analyzer over
///     it, and gives what the analyzer reports.
/// </summary>
internal static class AnalyzerHarness
{
    /// <summary>
    ///     Compiles the source and gives the diagnostics of the analyzer, in the
    ///     order of the source. The sample must compile, because an analyzer does not
    ///     examine code that does not bind.
    /// </summary>
    /// <param name="analyzer">The analyzer.</param>
    /// <param name="source">The sample.</param>
    /// <param name="libraries">The library assemblies that the sample references.</param>
    /// <returns>The diagnostics of the analyzer.</returns>
    public static async Task<IReadOnlyList<Diagnostic>> Analyze(
        DiagnosticAnalyzer analyzer,
        string source,
        IEnumerable<Assembly> libraries)
    {
        List<MetadataReference> references =
        [
            .. PlatformReferences(),
            .. libraries.Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
        ];

        CSharpCompilation compilation =
            CSharpCompilation.Create(
                // Core lets an assembly with this name use its internals. The Core block shapes
                // must use them, because only the library can call a Core block. The analyzer
                // counts an assembly whose name has ".Tests" as code that uses the library, as it
                // counts the shapes.
                assemblyName: "SodaFlow.Tests.Internal",
                syntaxTrees:
                [
                    CSharpSyntaxTree.ParseText(
                        text: source,
                        options: new CSharpParseOptions(languageVersion: LanguageVersion.Preview))
                ],
                references: references,
                options: new CSharpCompilationOptions(
                    outputKind: OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

        ImmutableArray<Diagnostic> errors =
        [
            .. compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        ];

        if (errors.Length > 0)
        {
            throw new InvalidOperationException($"The sample does not compile: {errors[0]}");
        }

        ImmutableArray<Diagnostic> found =
            await compilation.WithAnalyzers([analyzer])
                .GetAnalyzerDiagnosticsAsync()
                .ConfigureAwait(false);

        return [.. found.OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    // The assemblies of the runtime that runs the tests, which the sample compiles against.
    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
        .Split(separator: Path.PathSeparator, options: StringSplitOptions.RemoveEmptyEntries)
        .Select(static path => MetadataReference.CreateFromFile(path));
}
