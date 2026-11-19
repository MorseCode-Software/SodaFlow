using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SodaFlow.Analyzers.Timing;

/// <summary>
///     Code that the analyzer reads: the source files of one or more projects,
///     compiled as one assembly.
/// </summary>
/// <param name="Name">The name in the results.</param>
/// <param name="Projects">The project files, relative to the root of the repository.</param>
internal sealed record Workload(string Name, ImmutableArray<string> Projects)
{
    // The assembly name of the compiled workload. The analyzer does not check an assembly of the
    // library, and it knows those assemblies by a name that starts with "SodaFlow". Thus, this name
    // must not start with "SodaFlow".
    private const string AssemblyName = "Timing.Workload";

    /// <summary>
    ///     The workloads of the check. Each one was in the timing by hand of
    ///     the analyzer, before this check existed.
    /// </summary>
    public static ImmutableArray<Workload> All { get; } =
    [
        new(Name: "SodaFlow.Tests", Projects: ["src/CSharp/SodaFlow.Tests/SodaFlow.Tests.csproj"]),
        new(
            Name: "Accounts and Bounce view models",
            Projects:
            [
                "samples/Accounts/SodaFlow.Samples.Accounts.ViewModels/SodaFlow.Samples.Accounts.ViewModels.csproj",
                "samples/Bounce/SodaFlow.Samples.Bounce.ViewModels/SodaFlow.Samples.Bounce.ViewModels.csproj"
            ]),
        new(Name: "SodaFlow.Core", Projects: ["src/SodaFlow.Core/SodaFlow.Core.csproj"]),
        new(
            Name: "SodaFlow.Collections.Tests",
            Projects: ["src/Collections/CSharp/SodaFlow.Collections.Tests/SodaFlow.Collections.Tests.csproj"])
    ];

    /// <summary>
    ///     Compiles the workload. The source files are the <c>Compile</c> items
    ///     of the projects, because each project lists its files.
    /// </summary>
    /// <param name="repository">The root of the repository.</param>
    /// <param name="references">
    ///     A folder with the library assemblies and the TUnit assemblies. The
    ///     output of the analyzer tests has all of them.
    /// </param>
    /// <returns>The compilation, which has no errors.</returns>
    public CSharpCompilation Compile(string repository, string references)
    {
        ImmutableArray<string> projects = [.. this.Projects.Select(project => Path.Combine(repository, project))];

        ImmutableHashSet<string> ownAssemblies =
        [
            .. projects.Select(static project => Path.GetFileNameWithoutExtension(project))
        ];

        // The language version is Preview because the Roslyn of this app is older than C# 14, which
        // the projects use. The compilation has no symbols of the target frameworks of the projects.
        CSharpParseOptions parseOptions = new(languageVersion: LanguageVersion.Preview);

        ImmutableArray<SyntaxTree> trees =
        [
            .. projects
                .SelectMany(static project => SourceFiles(project))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(file => CSharpSyntaxTree.ParseText(
                    text: File.ReadAllText(file),
                    options: parseOptions,
                    path: file))
        ];

        CSharpCompilation compilation =
            CSharpCompilation.Create(
                assemblyName: AssemblyName,
                syntaxTrees: trees,
                references: [.. PlatformReferences(), .. LibraryReferences(references, ownAssemblies)],
                options: new CSharpCompilationOptions(
                    outputKind: OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable,
                    allowUnsafe: true));

        // A workload that does not compile is not the code of the project. Thus, the analyzer reads
        // code that a consumer cannot have, and its time is not the time to compare.
        ImmutableArray<Diagnostic> errors =
        [
            .. compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        ];

        return errors.Length > 0
            ? throw new InvalidOperationException(
                $"The workload {this.Name} has {errors.Length} compile errors. The first is: {errors[0]}")
            : compilation;
    }

    private static IEnumerable<string> SourceFiles(string project)
    {
        string folder = Path.GetDirectoryName(project) ?? throw new ArgumentException($"No folder: {project}");

        // Each Compile item, also one with a condition. The conditions of these projects select
        // files for a target framework that this compilation does not have.
        return XDocument.Load(project)
            .Descendants("Compile")
            .Select(static item => (string?)item.Attribute("Include"))
            .OfType<string>()
            .Select(include => Path.GetFullPath(Path.Combine(folder, include)));
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(static path => MetadataReference.CreateFromFile(path));

    private static IEnumerable<MetadataReference> LibraryReferences(
        string folder,
        ImmutableHashSet<string> ownAssemblies) =>
        Directory.GetFiles(folder, "*.dll")
            .Where(static path => Path.GetFileName(path).StartsWith("SodaFlow", StringComparison.Ordinal)
                                  || Path.GetFileName(path).StartsWith("TUnit", StringComparison.Ordinal))
            .Where(static path => !Path.GetFileName(path).StartsWith("SodaFlow.Analyzers", StringComparison.Ordinal))
            // A workload does not reference the assembly of its own source. SodaFlow.Core is one.
            .Where(path => !ownAssemblies.Contains(Path.GetFileNameWithoutExtension(path)))
            .Order(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path));
}
