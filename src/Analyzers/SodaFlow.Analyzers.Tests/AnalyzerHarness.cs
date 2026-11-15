using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
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
    /// <param name="assemblyName">
    ///     The name of the assembly of the sample. The analyzer knows the library
    ///     by the names of its assemblies, thus the name is part of the sample.
    /// </param>
    /// <returns>The compilation, which has no errors.</returns>
    public static CSharpCompilation Compile(string source, IEnumerable<Assembly> libraries, string assemblyName) =>
        Compile(source: source, libraries: libraries, assemblyName: assemblyName, images: []);

    /// <summary>
    ///     Compiles the source of a sample that references assemblies with no
    ///     source. An analyzer of the sample cannot read the bodies of their
    ///     code.
    /// </summary>
    /// <param name="source">The sample.</param>
    /// <param name="libraries">The library assemblies that the sample references.</param>
    /// <param name="assemblyName">The name of the assembly of the sample.</param>
    /// <param name="images">
    ///     The assemblies with no source, each as <see cref="Emit" /> gives it.
    /// </param>
    /// <returns>The compilation, which has no errors.</returns>
    public static CSharpCompilation Compile(
        string source,
        IEnumerable<Assembly> libraries,
        string assemblyName,
        IEnumerable<byte[]> images)
    {
        List<MetadataReference> references =
        [
            .. PlatformReferences(),
            .. libraries.Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            .. images.Select(static image => MetadataReference.CreateFromImage(image))
        ];

        CSharpCompilation compilation =
            CSharpCompilation.Create(
                assemblyName: assemblyName,
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
    public static Task<IReadOnlyList<Diagnostic>> Analyze(DiagnosticAnalyzer analyzer, Compilation compilation) =>
        Analyze(analyzer: analyzer, compilation: compilation, cancellation: CancellationToken.None);

    /// <summary>
    ///     Runs an analyzer over a compiled sample, with a token that can cancel
    ///     the run.
    /// </summary>
    /// <param name="analyzer">The analyzer.</param>
    /// <param name="compilation">The compiled sample.</param>
    /// <param name="cancellation">The token. A run that it cancels throws.</param>
    /// <returns>The diagnostics of the analyzer.</returns>
    public static async Task<IReadOnlyList<Diagnostic>> Analyze(
        DiagnosticAnalyzer analyzer,
        Compilation compilation,
        CancellationToken cancellation)
    {
        ImmutableArray<Diagnostic> found =
            await compilation.WithAnalyzers([analyzer])
                .GetAnalyzerDiagnosticsAsync(cancellation)
                .ConfigureAwait(false);

        return [.. found.OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    /// <summary>
    ///     Gives the assembly of a compilation as bytes. A second sample can
    ///     reference it, and <see cref="Load" /> can load it.
    /// </summary>
    /// <param name="compilation">The compiled sample.</param>
    /// <returns>The bytes of the assembly.</returns>
    public static byte[] Emit(Compilation compilation)
    {
        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);

        return result.Success
            ? image.ToArray()
            : throw new InvalidOperationException($"The sample does not emit: {result.Diagnostics[0]}");
    }

    /// <summary>
    ///     Loads the compiled sample into this process, so that a test can run
    ///     its code. The sample uses the library assemblies of this process.
    /// </summary>
    /// <param name="compilation">The compiled sample.</param>
    /// <param name="images">
    ///     The assemblies with no source that the sample references. They are in
    ///     no file, thus the sample gets them from the context that loads it.
    /// </param>
    /// <returns>The assembly of the sample.</returns>
    public static Assembly Load(Compilation compilation, IEnumerable<byte[]> images)
    {
        SampleContext context = new();

        foreach (byte[] image in images)
        {
            context.Add(image);
        }

        return context.Add(Emit(compilation));
    }

    // The assemblies of the runtime that runs the tests, which the sample compiles against.
    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
        .Split(separator: Path.PathSeparator, options: StringSplitOptions.RemoveEmptyEntries)
        .Select(static path => MetadataReference.CreateFromFile(path));

    // The context of one sample. It gives the sample each assembly that it loaded from bytes, and
    // the process gives each other assembly.
    private sealed class SampleContext() : AssemblyLoadContext(name: "Sample")
    {
        private readonly Dictionary<string, Assembly> loaded = new(StringComparer.Ordinal);

        public Assembly Add(byte[] image)
        {
            using MemoryStream stream = new(image);
            Assembly assembly = this.LoadFromStream(stream);
            this.loaded[assembly.GetName().FullName] = assembly;

            return assembly;
        }

        protected override Assembly? Load(AssemblyName assemblyName) =>
            this.loaded.GetValueOrDefault(assemblyName.FullName);
    }
}
