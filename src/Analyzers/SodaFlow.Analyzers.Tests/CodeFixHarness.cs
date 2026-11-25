using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Puts a sample of code in a workspace, as an editor does. It then
///     applies the fixes of a code fix provider to what an analyzer reports.
/// </summary>
/// <remarks>
///     Each sample must compile, and so must each result of a fix. A result must
///     also have no warning that the sample did not have, and the analyzer must
///     report one thing less in it. Thus, a fix that breaks the code, adds a
///     warning, or leaves its report fails the test that applies it.
/// </remarks>
internal static class CodeFixHarness
{
    /// <summary>
    ///     Gives each fix that the provider offers, for each report of the
    ///     analyzer on the sample, in the order of the source.
    /// </summary>
    /// <param name="analyzer">The analyzer.</param>
    /// <param name="provider">The code fix provider.</param>
    /// <param name="source">The sample, which the fixes change.</param>
    /// <param name="libraries">The library assemblies that the sample references.</param>
    /// <param name="languageVersion">
    ///     The version of C# of the sample. A sample before C# 8 has no
    ///     nullable context.
    /// </param>
    /// <returns>The fixes, each with the sample as the fix leaves it.</returns>
    public static async Task<IReadOnlyList<Offer>> Offers(
        DiagnosticAnalyzer analyzer,
        CodeFixProvider provider,
        string source,
        IEnumerable<Assembly> libraries,
        LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        Document document = Open(source: source, libraries: libraries, languageVersion: languageVersion);

        Compilation compilation =
            await document.Project.GetCompilationAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("The sample has no compilation.");

        ThrowOnErrors(compilation: compilation, what: "The sample");

        IReadOnlyList<Diagnostic> reports =
            await AnalyzerHarness.Analyze(analyzer: analyzer, compilation: compilation).ConfigureAwait(false);

        string[] warnings = [.. Warnings(compilation)];
        List<Offer> offers = [];

        for (int report = 0; report < reports.Count; report++)
        {
            List<CodeAction> actions = [];

            await provider.RegisterCodeFixesAsync(
                    new CodeFixContext(
                        document: document,
                        diagnostic: reports[report],
                        registerCodeFix: (action, _) => actions.Add(action),
                        cancellationToken: CancellationToken.None))
                .ConfigureAwait(false);

            foreach (CodeAction action in actions)
            {
                string what =
                    $"The fix '{action.Title}' of the report at line {reports[report].Location.GetLineSpan().StartLinePosition.Line + 1}";

                offers.Add(
                    new Offer(
                        report: report,
                        title: action.Title,
                        key: action.EquivalenceKey,
                        result: await Apply(
                                document: document,
                                action: action,
                                analyzer: analyzer,
                                check: new Check(What: what, Reports: reports.Count, WarningsBefore: warnings))
                            .ConfigureAwait(false)));
            }
        }

        return offers;
    }

    private static Document Open(string source, IEnumerable<Assembly> libraries, LanguageVersion languageVersion)
    {
        // The references of the analyzer harness: the platform, and the libraries.
        CSharpCompilation references =
            AnalyzerHarness.Compile(source: string.Empty, libraries: libraries, assemblyName: "Sample");

        AdhocWorkspace workspace = new();

        Project project =
            workspace.AddProject(
                ProjectInfo.Create(
                    id: ProjectId.CreateNewId(),
                    version: VersionStamp.Create(),
                    name: "Sample",
                    assemblyName: "Sample",
                    language: LanguageNames.CSharp,
                    compilationOptions: references.Options.WithNullableContextOptions(
                        languageVersion >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable),
                    parseOptions: new CSharpParseOptions(languageVersion: languageVersion),
                    metadataReferences: references.References));

        return workspace.AddDocument(projectId: project.Id, name: "Sample.cs", text: SourceText.From(source));
    }

    // The text of the document after the CodeFixProvider changes it. The check holds this text to the sample.
    private static async Task<string> Apply(TextDocument document, CodeAction action, DiagnosticAnalyzer analyzer, Check check)
    {
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);

        Document result =
            operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException($"{check.What} removes the document.");

        Compilation compilation =
            await result.Project.GetCompilationAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{check.What} has no compilation.");

        SourceText text = await result.GetTextAsync().ConfigureAwait(false);

        ThrowOnErrors(compilation: compilation, what: check.What);

        if (Warnings(compilation).FirstOrDefault(warning => !check.WarningsBefore.Contains(warning)) is { } added)
        {
            throw new InvalidOperationException($"{check.What} adds a warning: {added}\n{text}");
        }

        IReadOnlyList<Diagnostic> reports = await AnalyzerHarness.Analyze(analyzer: analyzer, compilation: compilation).ConfigureAwait(false);

        return reports.Count != check.Reports - 1
            ? throw new InvalidOperationException($"{check.What} leaves {reports.Count} reports of {check.Reports}:\n{text}")
            : text.ToString();
    }

    private static void ThrowOnErrors(Compilation compilation, string what)
    {
        if (compilation.GetDiagnostics().FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) is { } error)
        {
            throw new InvalidOperationException($"{what} does not compile: {error}");
        }
    }

    // Each warning of the compiler as its id and message. It has no location, because a code change moves code.
    private static IEnumerable<string> Warnings(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.Id + " " + diagnostic.GetMessage());

    /// <summary>A fix that a provider offers for one report.</summary>
    /// <param name="report">The index of the report, in the order of the source.</param>
    /// <param name="title">The title that an editor shows.</param>
    /// <param name="key">The equivalence key, which says which fix it is.</param>
    /// <param name="result">The sample as the fix leaves it.</param>
    internal sealed class Offer(int report, string title, string? key, string result)
    {
        /// <summary>Gets the index of the report, in the order of the source.</summary>
        public int Report { get; } = report;

        /// <summary>Gets the title that an editor shows.</summary>
        public string Title { get; } = title;

        /// <summary>Gets the equivalence key.</summary>
        public string? Key { get; } = key;

        /// <summary>Gets the sample as the fix leaves it.</summary>
        public string Result { get; } = result;
    }

    // What a result must hold to. These are the name of the CodeFixProvider for a failure message, the count of the
    // reports on the sample, and the warnings of the sample.
    private sealed record Check(string What, int Reports, IReadOnlyCollection<string> WarningsBefore);
}
