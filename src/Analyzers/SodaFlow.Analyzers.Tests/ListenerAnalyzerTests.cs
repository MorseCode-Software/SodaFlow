using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Runs <see cref="ListenerAnalyzer" /> over the listener shapes, and checks
///     that it reports each line that the shapes mark and no other line. Then
///     runs each shape, and checks the listener of each shape: a reported shape
///     loses it at a collection, and each other shape keeps it.
/// </summary>
/// <remarks>
///     A shape marks a line with the comment <c>SF2001</c> at its end. Without
///     the run, the marks are only what the analyzer says. The run holds the
///     marks to the runtime. A mark on a listener that lives is a false report.
///     A shape with no mark whose listener stops is a miss.
/// </remarks>
internal sealed class ListenerAnalyzerTests
{
    private const string Mark = "// SF2001";

    private const string ShapesAssembly = "SodaFlow.Tests.Listeners";

    private const string ShapesType = "SodaFlow.Analyzers.ListenerShapes.ListenerShapes";

    // The shapes whose listener stops at a collection, and that the rule does not report. Each is
    // an owner that nothing holds, which the rule does not follow.
    private static readonly Dictionary<string, string> Misses =
        new()
        {
            ["ListenIntoTheFieldOfADroppedOwner"] = "The listener is in a field of an object that nothing holds.",
            ["ListenIntoADroppedList"] = "The listener is in a list that nothing holds.",
            ["ListenIntoALocalThatADroppedLambdaReads"] =
                "A lambda reads the local, and nothing holds the lambda. The rule reads each capture as a read."
        };

    private static readonly Assembly[] ShapeLibraries =
    [
        typeof(Cell<>).Assembly,
        typeof(CellExtensionMethods).Assembly
    ];

    private static Task<string> ReadShapes() =>
        File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "ListenerShapes.cs"));

    [Test]
    public async Task TheRuleReportsEachMarkedLineAndNoOther()
    {
        string source = await ReadShapes();

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new ListenerAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly));

        string[] lines = source.Split('\n');

        IReadOnlyList<int> marked =
        [
            .. Enumerable.Range(start: 0, count: lines.Length)
                .Where(index => lines[index].TrimEnd().EndsWith(value: Mark, comparisonType: StringComparison.Ordinal))
                .Select(static index => index + 1)
        ];

        IReadOnlyList<string> reported =
        [
            .. diagnostics.Select(static diagnostic =>
                $"{diagnostic.Id} at line {diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}")
        ];

        await Assert.That(marked).IsNotEmpty();

        await Assert.That(reported)
            .IsEquivalentTo(marked.Select(static line => $"SF2001 at line {line}"))
            .Because("the rule reports each marked line one time, and no other line");
    }

    [Test]
    public async Task TheRuleReportsTheNameOfTheCall()
    {
        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new ListenerAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source:
                    """
                    using SodaFlow;

                    public static class Sample
                    {
                        public static void Drops(StreamSink<int> s, StreamSink<int>? maybe)
                        {
                            s.Map(static x => x).ListenOnce(static _ => { });
                            maybe?.Listen(static _ => { });
                            StreamExtensionMethods.Listen(s, static _ => { });
                        }
                    }
                    """,
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly));

        IReadOnlyList<string> texts =
        [
            .. diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? string.Empty)
        ];

        IReadOnlyList<string> messages = [.. diagnostics.Select(static diagnostic => diagnostic.GetMessage())];

        await Assert.That(texts).IsEquivalentTo(["ListenOnce", "Listen", "Listen"]);

        await Assert.That(messages[0])
            .IsEqualTo(
                "Nothing holds the listener that 'ListenOnce' returns, thus a garbage collection can stop it with no "
                + "message. Keep it in a field, or in a local that later code reads.");
    }

    [Test]
    public async Task TheRuleIsQuietInCodeThatDoesNotReferenceTheLibrary()
    {
        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new ListenerAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source:
                    """
                    public static class Sample
                    {
                        public static object Listen(this object value, System.Action<int> handler) => value;

                        public static void Drops() => new object().Listen(static _ => { });
                    }
                    """,
                    libraries: [],
                    assemblyName: ShapesAssembly));

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task TheRuleIsQuietAtTheInternalListenOfCore()
    {
        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new ListenerAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source:
                    """
                    using SodaFlow;

                    internal static class Sample
                    {
                        public static void Links(Stream<int> s) => s.Listen(target: Node<int>.Null, action: static (_, _) => { });
                    }
                    """,
                    libraries: ShapeLibraries,
                    assemblyName: "SodaFlow.Tests.Internal"));

        await Assert.That(diagnostics).IsEmpty().Because("Core links its operators with internal methods of that name");
    }

    /// <summary>
    ///     Holds the marks to the runtime. Each shape runs in an optimized build,
    ///     and each method of the shapes gets the full optimization of the JIT at
    ///     its first call.
    /// </summary>
    [Test]
    public async Task EachShapeKeepsOrLosesItsListenerAsTheMarksSay()
    {
        string source = await ReadShapes();

        CSharpCompilation compilation =
            AnalyzerHarness.Compile(source: source, libraries: ShapeLibraries, assemblyName: ShapesAssembly);

        Type shapes =
            AnalyzerHarness.Load(compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release)))
                .GetType(ShapesType)
            ?? throw new InvalidOperationException("The shapes file has no class ListenerShapes.");

        SyntaxNode root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();

        HashSet<string> reported =
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(static method => method.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ListenerShapes" })
                .Where(static method => method.ToFullString().Contains(value: Mark, comparisonType: StringComparison.Ordinal))
                .Select(static method => method.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);

        List<string> mismatches = [];
        int count = 0;

        foreach (MethodInfo shape in shapes.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            StreamSink<int> sink = Stream.CreateSink<int>();
            List<int> seen = [];

            shape.Invoke(obj: null, parameters: [sink, seen]);
            count++;

            bool lives = seen.Contains(1);
            bool expected = !reported.Contains(shape.Name) && !Misses.ContainsKey(shape.Name);

            if (lives != expected)
            {
                string why =
                    Misses.TryGetValue(key: shape.Name, value: out string? reason)
                        ? $" It is a known miss: {reason}"
                        : string.Empty;

                mismatches.Add(
                    $"{shape.Name}: expected the listener to {(expected ? "live" : "stop")}, but it "
                    + $"{(lives ? "lived" : "stopped")}.{why}");
            }
        }

        await Assert.That(mismatches).IsEmpty();

        await Assert.That(count).IsGreaterThanOrEqualTo(reported.Count + Misses.Count + 1);

        await Assert.That(Misses.Keys.Except(shapes.GetMethods().Select(static method => method.Name)))
            .IsEmpty()
            .Because("each known miss names a shape");
    }
}
