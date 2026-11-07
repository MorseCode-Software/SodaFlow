using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Runs <see cref="LoopReadAnalyzer" /> over the shapes file and checks
///     the rule it reports for each shape.
/// </summary>
/// <remarks>
///     Each shape is one method of <c>Shapes.cs</c>, and the table here gives
///     the rules expected in it. A shape with no entry expects nothing. The table
///     is the record of what the analyzer sees and what it does not. The shapes
///     with no entry include a read in a callback and a read after the block. They
///     also include a delegate with an unknown result and a chain one call deeper
///     than the depth.
/// </remarks>
internal sealed class LoopReadAnalyzerTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Expected =
        new Dictionary<string, string[]>
        {
            ["DirectSample"] = ["SF1001"],
            ["MapSample"] = ["SF1001"],
            ["LiftSample"] = ["SF1001"],
            ["CalmSample"] = ["SF1001"],
            ["SwitchCSample"] = ["SF1001"],
            ["HoldLazySample"] = ["SF1001"],
            ["ConstantLazySample"] = ["SF1001"],
            ["ConstructorSamples"] = ["SF1004"],
            ["ConstructorSamplesAsserted"] = ["SF1004"],
            ["ConstructorStores"] = ["SF1002"],
            ["HelperSamples"] = ["SF1004"],
            ["DerivedIntoHelperSamples"] = ["SF1004"],
            ["CloseWithPlaceholder"] = ["SF1002"],
            ["CloseWithMap"] = ["SF1002"],
            ["CloseWithUpdatesHold"] = ["SF1002"],
            ["SwitchOverOwnOutput"] = ["SF1002"],
            ["ReadInNestedTransaction"] = ["SF1001"],
            ["ExplicitLoop"] = ["SF1001"],
            ["ReadInLinqLambda"] = ["SF1001"],
            ["ReadInLocalFunction"] = ["SF1001"],
            ["ForwardReferenceRead"] = ["SF1001"],
            ["ListLiftSample"] = ["SF1001"],
            ["DelegateCall"] = ["SF1003"],
            ["BehaviorMapSample"] = ["SF1001"],
            ["BindableRead"] = ["SF1001"],
            ["RowPropertyReadInside"] = ["SF1001"],
            ["RowMethodReadInside"] = ["SF1005"],
            ["ExplicitRowReadInside"] = ["SF1001"],
            ["RowIntoListAndComputedProperty"] = ["SF1005"],
            ["CloseWithOwnLazySample"] = ["SF1006"],
            ["CloseThroughHeldRow"] = ["SF1002"],
            ["StoredLambdas"] = ["SF1001"],
            ["DeferredSampleRead"] = ["SF1001"],
            ["BlockHelpers"] = ["SF1001"],
            ["NestedLoop"] = ["SF1001", "SF1002"],
            ["HelperChains"] = ["SF1001", "SF1001"],
            ["RowMethodThroughSecondMethod"] = ["SF1005"],
            ["HelperSamplesThroughHelper"] = ["SF1004"],
            ["ConstructorInitializerCallsHelper"] = ["SF1001"],
            ["CloseThroughSinkOfRow"] = ["SF1002"],
            ["AssertionAtTheArgument"] = ["SF1004"]
        };

    [Test]
    public async Task EachShapeReportsTheExpectedRules()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "Shapes.cs"));

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                source: source,
                libraries:
                [
                    typeof(Cell<>).Assembly,
                    typeof(CellExtensionMethods).Assembly,
                    typeof(Maybe<>).Assembly,
                    typeof(IBindableFactory).Assembly,
                    typeof(IOneWayBindableValue<>).Assembly
                ]);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        SyntaxNode root = await tree.GetRootAsync();

        IReadOnlyList<string> shapes =
        [
            .. root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(static method => method.Parent is ClassDeclarationSyntax { Identifier.ValueText: "Shapes" })
                .Select(static method => method.Identifier.ValueText)
        ];

        Dictionary<string, List<string>> actual =
            shapes.ToDictionary(keySelector: static shape => shape, elementSelector: static _ => new List<string>());

        foreach (Diagnostic diagnostic in diagnostics)
        {
            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan);

            MethodDeclarationSyntax? shape =
                node.AncestorsAndSelf()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(static method =>
                        method.Parent is ClassDeclarationSyntax { Identifier.ValueText: "Shapes" });

            if (shape == null)
            {
                throw new InvalidOperationException($"{diagnostic} is outside every shape.");
            }

            actual[shape.Identifier.ValueText].Add(diagnostic.Id);
        }

        // All mismatches together, so that one run says what changed.
        IReadOnlyList<string> mismatches =
        [
            .. shapes
                .Select(shape =>
                {
                    string expected =
                        string.Join(
                            separator: " ",
                            value: Expected.TryGetValue(key: shape, value: out string[]? rules) ? rules : []);

                    string found =
                        string.Join(
                            separator: " ",
                            values: actual[shape]
                                .OrderBy(keySelector: static id => id, comparer: StringComparer.Ordinal));

                    return expected == found ? null : $"{shape}: expected [{expected}], found [{found}]";
                })
                .Where(static line => line != null)
                .Cast<string>()
        ];

        await Assert.That(mismatches).IsEmpty();

        await Assert.That(Expected.Keys.Except(shapes)).IsEmpty().Because("every expectation names a shape");
    }
}
