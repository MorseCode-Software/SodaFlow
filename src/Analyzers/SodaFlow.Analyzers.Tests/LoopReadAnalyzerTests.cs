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
using SodaFlow.Collections;
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
///     with no entry include a read in a callback and a read after the block. A
///     shape with only SF1003 has a value that goes where the analyzer cannot
///     read. That is a delegate of an unknown source, or a chain of calls
///     that is deeper than the depth.
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
            ["HelperChains"] = ["SF1001", "SF1001", "SF1003"],
            ["RowMethodThroughSecondMethod"] = ["SF1005"],
            ["HelperSamplesThroughHelper"] = ["SF1003", "SF1004"],
            ["RecursiveHelper"] = ["SF1003"],
            ["ConstructorInitializerCallsHelper"] = ["SF1001"],
            ["CloseThroughSinkOfRow"] = ["SF1002"],
            ["AssertionAtTheArgument"] = ["SF1004"],
            ["CoreBlockSample"] = ["SF1001"],
            ["CoreBlockCloseWithPlaceholder"] = ["SF1002"],
            ["NamedArgumentsReorderedToTheLoop"] = ["SF1001"],
            ["GenericHelperSamples"] = ["SF1004"],
            ["GenericRowSamples"] = ["SF1004"],
            ["ViewFilteredByTheLoopRead"] = ["SF1001"],
            ["ViewFilteredByTheLoopAsTheClose"] = ["SF1002"],
            ["FilterOfPendingViewRead"] = ["SF1001"],
            ["ConditionalAccessSample"] = ["SF1001"],
            ["ConditionalAccessMapSample"] = ["SF1001"],
            ["ConditionalAccessBindableValue"] = ["SF1001"],
            ["ConditionalAccessRowMember"] = ["SF1001"],
            ["StaticFormSample"] = ["SF1001"],
            ["StaticFormMapSample"] = ["SF1001"],
            ["StaticFormNamedMapSample"] = ["SF1001"],
            ["LocalFunctionBlock"] = ["SF1001"],
            ["AnonymousMethodBlock"] = ["SF1001"],
            ["BlockThatReads"] = ["SF1001"],
            ["BlockThatClosesWithItself"] = ["SF1002"],
            ["RecordPositionalMember"] = ["SF1001"],
            ["RecordWithPendingMember"] = ["SF1001"],
            ["ObjectInitializerMember"] = ["SF1001"],
            ["ObjectInitializerNoArgumentList"] = ["SF1001"],
            ["ObjectInitializerReads"] = ["SF1001"],
            ["BaseConstructorMember"] = ["SF1001"],
            ["BaseConstructorSamples"] = ["SF1004"],
            ["WrittenConstructorChain"] = ["SF1001"],
            ["CollectionInitializerElement"] = ["SF1001"],
            ["HelperDerivesThenSamples"] = ["SF1004"],
            ["HelperLocalThenSamples"] = ["SF1004"],
            ["RowPassedToHelper"] = ["SF1004"],
            ["RowMethodDerivedRead"] = ["SF1005"],
            ["HelperBranchReturn"] = ["SF1001"],
            ["ComputedPropertyOfPendingMember"] = ["SF1001"],
            ["HelperPassesOn"] = ["SF1003"],
            ["LocalFunctionWithParameter"] = ["SF1004"],
            ["LocalFunctionReadsALocal"] = ["SF1001"],
            ["StoredLambdaThroughInvoke"] = ["SF1001"],
            ["StoredLambdaThroughConditionalInvoke"] = ["SF1001"],
            ["StoredLambdaWithParameter"] = ["SF1004"],
            ["PrimaryConstructorCaptureCalled"] = ["SF1005"],
            ["LambdaKeptByConstructorThenRun"] = ["SF1001"],
            ["LambdaRunByMethod"] = ["SF1001"],
            ["LambdaResultReadByMethod"] = ["SF1004"],
            ["LambdaGivenTheLoopByMethod"] = ["SF1004"],
            ["ContentFunctionGivesALocal"] = ["SF1002"],
            ["ForeachVariable"] = ["SF1001"],
            ["Deconstruction"] = ["SF1001"],
            ["PatternVariable"] = ["SF1001"],
            ["PatternVariableOfRowMember"] = ["SF1001"],
            ["SwitchPatternVariable"] = ["SF1001"],
            ["OutVariable"] = ["SF1001"],
            ["StaticField"] = ["SF1001"],
            ["StaticFieldReadByHelper"] = ["SF1005"],
            ["ListAddThenIndex"] = ["SF1001"],
            ["LinqLambdaParameter"] = ["SF1001"],
            ["FirstOfPendingList"] = ["SF1001"],
            ["DictionaryTryGetValue"] = ["SF1001"],
            ["MemberAssignedInTheBlock"] = ["SF1001"],
            ["MaybeMatchRunsItsFunction"] = ["SF1001"],
            ["MaybeOfPendingCellMatched"] = ["SF1001"],
            ["FoldSelectReads"] = ["SF1001"],
            ["FunctionWrapperCalled"] = ["SF1001"],
            ["LocalFunctionGivenToAQuery"] = ["SF1004"],
            ["QueryResultOfALambda"] = ["SF1001"],
            ["SampleOfPendingContent"] = ["SF1001"],
            ["RowFromSampleOfSink"] = ["SF1001"],
            ["BindableValueIsAPendingCell"] = ["SF1001"],
            ["ForeachOverASampledList"] = ["SF1001"],
            ["ReassignedLocalInABranch"] = ["SF1001"],
            ["ReassignedLocalToThePendingValue"] = ["SF1001"],
            ["MapFunctionReads"] = ["SF1001"],
            ["LiftFunctionReads"] = ["SF1001"],
            ["BehaviorMapFunctionReads"] = ["SF1001"],
            ["LazyMapFunctionReads"] = ["SF1001"],
            ["ViewPredicateReads"] = ["SF1001"],
            ["CloseWithMapWhoseFunctionReads"] = ["SF1006"],
            ["MapFunctionReadsThroughHelper"] = ["SF1001"],
            ["MapOfMapFunctionReads"] = ["SF1001"],
            ["PendingPartOfATupleFromAHelper"] = ["SF1001"],
            ["PendingElementOfATupleLocal"] = ["SF1001"],
            ["MemberThroughAnInterface"] = ["SF1001"],
            ["MethodThroughAnInterface"] = ["SF1005"],
            ["ObjectGivesItselfToAHelper"] = ["SF1004"],
            ["AssignedInAFunctionThatDoesNotRun"] = ["SF1001"],
            ["DelegateGetsAFunctionThenRuns"] = ["SF1001"],
            ["ValuesOfADictionary"] = ["SF1001"],
            ["FunctionKeptByAConstructorOfThisCode"] = ["SF1005"],
            ["FunctionFromAHelperCalled"] = ["SF1005"],
            ["LazyReadInTheBlock"] = ["SF1001"],
            ["LazyOfALocalFunctionRead"] = ["SF1001"],
            ["LazyReadInAHelper"] = ["SF1001"],
            ["LazyWhoseFunctionCallsAHelper"] = ["SF1004"],
            ["LazyConditionalRead"] = ["SF1001"],
            ["LazyMapRead"] = ["SF1001"],
            ["LazyMapSamplesAMadeCell"] = ["SF1004"],
            ["LazyLiftRead"] = ["SF1001"],
            ["LazyInAListRead"] = ["SF1001"],
            ["LazyOfAFunctionCalled"] = ["SF1001"],
            ["LazyOfAMadeFunctionCalled"] = ["SF1001"],
            ["LazyKeptByAConstructorOfThisCode"] = ["SF1005"],
            ["LazyFromAHelperRead"] = ["SF1005"],
            ["HeldInitialCellRead"] = ["SF1001"],
            ["SortByAnOrderThatReads"] = ["SF1001"],
            ["SortByAnOrderCellThatReads"] = ["SF1001"],
            ["ThenByThatReads"] = ["SF1001"],
            ["OrderByIdentityThatReads"] = ["SF1001"],
            ["OrderFromAHelper"] = ["SF1001"],
            ["OrderOfALocalFunction"] = ["SF1001"],
            ["OrderCellMappedToAnOrderThatReads"] = ["SF1001"],
            ["CloseWithAViewSortedByAnOrderCell"] = ["SF1006"]
        };

    // A shape whose block is a method, given by its name. The analyzer reports in that method.
    // Thus, the rules of the method say what the shape does at run time.
    private static readonly Dictionary<string, string> BlockMethods =
        new()
        {
            ["MethodBlock"] = "BlockThatReads",
            ["MethodBlockCloses"] = "BlockThatClosesWithItself",
            ["MethodBlockSnapshots"] = "BlockThatSnapshots"
        };

    // Core lets an assembly with this name use its internals. The Core block shapes must use them,
    // because only the library can call a Core block. The name is not one of the library, thus
    // the analyzer reads the shapes as code that uses the library.
    private const string ShapesAssembly = "SodaFlow.Tests.Internal";

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
        typeof(IOneWayBindableValue<>).Assembly,
        typeof(IIdentity<>).Assembly,
        typeof(CollectionExtensionMethods).Assembly
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
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly));

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
            AnalyzerHarness.Load(
                AnalyzerHarness.Compile(
                    source: await ReadShapes(),
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly));

        Type shapes =
            assembly.GetType("SodaFlow.Analyzers.Shapes.Shapes")
            ?? throw new InvalidOperationException("The shapes file has no class with the name Shapes.");

        List<string> mismatches = [];
        int count = 0;

        foreach (MethodInfo shape in shapes
                     .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(static method => !method.IsSpecialName && !NotRun.Contains(method.Name)))
        {
            string reportedIn =
                BlockMethods.TryGetValue(key: shape.Name, value: out string? blockMethod) ? blockMethod : shape.Name;

            string[] rules = Expected.TryGetValue(key: reportedIn, value: out string[]? listed) ? listed : [];

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
