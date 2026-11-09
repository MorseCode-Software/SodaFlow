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
using Microsoft.CodeAnalysis.Emit;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Compiles a sample of code against the library. A test then runs an
///     analyzer over the sample, or loads the sample and runs it.
/// </summary>
internal static class AnalyzerHarness
{
    /// <summary>
    ///     Compiles the source. The sample must compile, because an analyzer does
    ///     not examine code that does not bind.
    /// </summary>
    /// <param name="source">The sample.</param>
    /// <param name="libraries">The library assemblies that the sample references.</param>
    /// <returns>The compilation, which has no errors.</returns>
    public static CSharpCompilation Compile(string source, IEnumerable<Assembly> libraries)
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

        return errors.Length > 0
            ? throw new InvalidOperationException($"The sample does not compile: {errors[0]}")
            : compilation;
    }

    /// <summary>
    ///     Runs the analyzer over the compilation and gives its diagnostics, in
    ///     the order of the source.
    /// </summary>
    /// <param name="analyzer">The analyzer.</param>
    /// <param name="compilation">The compiled sample.</param>
    /// <returns>The diagnostics of the analyzer.</returns>
    public static async Task<IReadOnlyList<Diagnostic>> Analyze(DiagnosticAnalyzer analyzer, Compilation compilation)
    {
        ImmutableArray<Diagnostic> found =
            await compilation.WithAnalyzers([analyzer])
                .GetAnalyzerDiagnosticsAsync()
                .ConfigureAwait(false);

        return [.. found.OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    /// <summary>
    ///     Loads the compiled sample into this process, so that a test can run
    ///     its code. The sample uses the library assemblies of this process.
    /// </summary>
    /// <param name="compilation">The compiled sample.</param>
    /// <returns>The assembly of the sample.</returns>
    public static Assembly Load(Compilation compilation)
    {
        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);

        return result.Success
            ? Assembly.Load(image.ToArray())
            : throw new InvalidOperationException($"The sample does not emit: {result.Diagnostics[0]}");
    }

    // The assemblies of the runtime that runs the tests, which the sample compiles against.
    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
        .Split(separator: Path.PathSeparator, options: StringSplitOptions.RemoveEmptyEntries)
        .Select(static path => MetadataReference.CreateFromFile(path));
}
