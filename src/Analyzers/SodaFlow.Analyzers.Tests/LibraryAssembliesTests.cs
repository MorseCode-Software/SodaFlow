using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Holds <see cref="SodaFlowSymbols.LibraryAssemblyNames" /> to the
///     solution, and checks that a name must be equal to one in the list.
/// </summary>
/// <remarks>
///     The analyzer gives a method of the library a rule from its table, and
///     does not read its body. It follows the body of each other method. Thus,
///     the list of the library assemblies decides what the analyzer sees. A new
///     project that makes a package fails the first test until the list names
///     it.
/// </remarks>
internal sealed class LibraryAssembliesTests
{
    private const string SolutionFile = "SodaFlow.slnx";

    // A helper of this sample samples its parameter. The analyzer finds that read only when it
    // follows the helper, and it follows only a method that is not of the library.
    private const string HelperThatSamples =
        """
        using SodaFlow;

        namespace SodaFlow.Extras;

        public static class Sample
        {
            private static readonly StreamSink<int> S = Stream.CreateSink<int>();

            public static Cell<int> HelperSamples() => Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadIt(l)));

            private static int ReadIt(Cell<int> c) => c.Sample();
        }
        """;

    [Test]
    public async Task TheListIsTheProjectsOfTheSolutionThatMakeAPackage()
    {
        string solution = FindSolution();
        string directory = Path.GetDirectoryName(solution) ?? throw new InvalidOperationException(solution);

        IReadOnlyList<string> packaged =
        [
            .. XDocument.Load(solution)
                .Descendants("Project")
                .Select(static project => project.Attribute("Path")?.Value)
                .OfType<string>()
                .Select(path => Path.Combine(path1: directory, path2: path))
                .Select(static path => (Path: path, Project: XDocument.Load(path)))
                // A project that makes no package says so. A test project, a benchmark, and the
                // analyzer project each have this property.
                .Where(static project =>
                    project.Project.Descendants("IsPackable").All(static packable => packable.Value != "false"))
                .Select(static project =>
                    project.Project.Descendants("AssemblyName").FirstOrDefault()?.Value
                    ?? Path.GetFileNameWithoutExtension(project.Path))
                .OrderBy(keySelector: static name => name, comparer: StringComparer.Ordinal)
        ];

        await Assert.That(packaged).IsNotEmpty();

        await Assert.That(packaged.Except(SodaFlowSymbols.LibraryAssemblyNames))
            .IsEmpty()
            .Because("each project that makes a package is an assembly of the library");

        await Assert.That(SodaFlowSymbols.LibraryAssemblyNames.Except(packaged))
            .IsEmpty()
            .Because("each name in the list is a project that makes a package");
    }

    [Test]
    public async Task AnAssemblyWithTheLibraryPrefixIsNotTheLibrary()
    {
        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: HelperThatSamples,
                    libraries: [typeof(Cell<>).Assembly, typeof(CellExtensionMethods).Assembly],
                    assemblyName: "SodaFlow.Extras"));

        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id)).IsEquivalentTo(["SF1004"]);
    }

    // The tests run from the output directory of this project, which is in the directory of the
    // solution.
    private static string FindSolution()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(path1: directory.FullName, path2: SolutionFile);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No directory above {AppContext.BaseDirectory} has {SolutionFile}.");
    }
}
