using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Runs <see cref="LoopReadAnalyzer" /> over the shapes file and checks
///     the rule it reports for each shape. Then runs each shape, and checks that
///     the library does what the rules say.
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
            ["AssertionAtTheArgument"] = ["SF1004"],
            ["CoreBlockSample"] = ["SF1001"],
            ["CoreBlockCloseWithPlaceholder"] = ["SF1002"]
        };

    // The shapes that the run-time test does not call.
    private static readonly HashSet<string> NotRun =
    [
        // The shapes file must have an entry point.
        "Main",
        // The helper calls itself with no end, and a run overflows the stack.
        "RecursiveHelper"
    ];

    private static readonly Assembly[] ShapeLibraries =
    [
        typeof(Cell<>).Assembly,
        typeof(CellExtensionMethods).Assembly,
        typeof(Maybe<>).Assembly,
        typeof(IBindableFactory).Assembly,
        typeof(IOneWayBindableValue<>).Assembly
    ];

    private static Task<string> ReadShapes() =>
        File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "Shapes.cs"));

    [Test]
    public async Task EachShapeReportsTheExpectedRules()
    {
        string source = await ReadShapes();

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(source: source, libraries: ShapeLibraries));

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

    /// <summary>
    ///     Holds the table of expected rules to the library. A rule for a read
    ///     means that the shape throws the exception of a looped value sampled
    ///     before its loop closes. A rule for a close means that the shape throws
    ///     the exception of that close. No rule means that the shape returns.
    /// </summary>
    /// <remarks>
    ///     Without this test, the table is only what the analyzer says. A change
    ///     to the library can change what throws. Then, the analyzer is incorrect
    ///     and no test fails. SF1003 says that the analyzer cannot see the callee,
    ///     thus a shape with only that rule has no known result and does not run.
    /// </remarks>
    [Test]
    public async Task EachShapeDoesAtRunTimeWhatItsRulesSay()
    {
        Assembly assembly =
            AnalyzerHarness.Load(AnalyzerHarness.Compile(source: await ReadShapes(), libraries: ShapeLibraries));

        Type shapes =
            assembly.GetType("SodaFlow.Analyzers.Shapes.Shapes")
            ?? throw new InvalidOperationException("The shapes file has no class with the name Shapes.");

        List<string> mismatches = [];
        int count = 0;

        foreach (MethodInfo shape in shapes
                     .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(static method => !method.IsSpecialName && !NotRun.Contains(method.Name)))
        {
            string[] rules = Expected.TryGetValue(key: shape.Name, value: out string[]? listed) ? listed : [];

            IReadOnlyList<Outcome> permitted =
                rules.Length == 0
                    ? [Outcome.Returns]
                    : [.. rules.Where(static rule => rule != "SF1003").Select(OutcomeOf).Distinct()];

            if (permitted.Count == 0)
            {
                continue;
            }

            (Outcome outcome, string description) = Run(shape);
            count++;

            if (!permitted.Contains(outcome))
            {
                mismatches.Add(
                    $"{shape.Name}: the rules [{string.Join(separator: " ", value: rules)}] permit "
                    + $"[{string.Join(separator: ", ", values: permitted)}], but the shape {description}");
            }
        }

        await Assert.That(mismatches).IsEmpty();

        await Assert.That(count).IsGreaterThanOrEqualTo(1).Because("the test found shapes to run");
    }

    private static Outcome OutcomeOf(string rule) =>
        rule switch
        {
            "SF1001" or "SF1004" or "SF1005" => Outcome.ReadsEarly,
            "SF1002" => Outcome.MakesCycle,
            "SF1006" => Outcome.ForcesItself,
            _ => throw new ArgumentOutOfRangeException(
                paramName: nameof(rule),
                actualValue: rule,
                message: "The test does not know what this rule means at run time.")
        };

    private static (Outcome Outcome, string Description) Run(MethodBase shape)
    {
        try
        {
            shape.Invoke(obj: null, parameters: [.. shape.GetParameters().Select(ArgumentFor)]);

            return (Outcome.Returns, "returns");
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is { } thrown)
        {
            Outcome outcome =
                thrown switch
                {
                    DependencyCycleException => Outcome.MakesCycle,
                    InvalidOperationException when Says(exception: thrown, text: "sampled before it was looped") =>
                        Outcome.ReadsEarly,
                    InvalidOperationException when Says(exception: thrown, text: "lazy sample forces itself") =>
                        Outcome.ForcesItself,
                    _ => Outcome.Other
                };

            return (outcome, $"throws {thrown.GetType().Name}: {thrown.Message}");
        }
    }

    private static bool Says(Exception exception, string text) =>
        exception.Message.Contains(value: text, comparisonType: StringComparison.Ordinal);

    // A shape with a parameter takes a value that the analyzer cannot see into. The run gives it a
    // value that does nothing with a looped value.
    private static object ArgumentFor(ParameterInfo parameter) =>
        parameter.ParameterType switch
        {
            { } type when type == typeof(IBindableFactory) => new BindableFactory(BindingScheduler.Immediate),
            { } type when type == typeof(Func<Cell<int>, int>) => (Func<Cell<int>, int>)(static _ => 0),
            { } type when type == typeof(Action<Action>) => (Action<Action>)(static _ => { }),
            _ => throw new InvalidOperationException(
                $"The test has no argument for parameter {parameter.Name} of {parameter.Member.Name}.")
        };

    /// <summary>What a shape does when the test calls it.</summary>
    private enum Outcome
    {
        /// <summary>The shape returns.</summary>
        Returns,

        /// <summary>The shape samples a looped value before the loop closes.</summary>
        ReadsEarly,

        /// <summary>The shape closes a loop with a value that is linked to the loop.</summary>
        MakesCycle,

        /// <summary>
        ///     The shape closes a loop with a value whose initial value is a lazy
        ///     sample of the loop.
        /// </summary>
        ForcesItself,

        /// <summary>The shape throws an exception that no rule is about.</summary>
        Other
    }
}
