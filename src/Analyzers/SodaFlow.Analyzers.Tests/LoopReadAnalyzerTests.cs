using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SodaFlow.Async;
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
///     Each shape is one method of <c>Shapes.cs</c>, and the table
///     <c>Expected</c> gives the rules expected in it. A shape with no entry
///     expects nothing. The table is the record of what the analyzer sees and
///     what it does not. The shapes with no entry include a read in a callback
///     and a read after the block. A shape with only SF1003 has a value that
///     goes where the analyzer cannot read. That is a delegate of an unknown
///     source, or a chain of calls that is deeper than the depth.
/// </remarks>
internal sealed partial class LoopReadAnalyzerTests
{
    // A shape whose block is a method, given by its name. The analyzer reports in that method.
    // Thus, the rules of the method say what the shape does at run time.
    private static readonly Dictionary<string, string> BlockMethods =
        new()
        {
            ["MethodBlock"] = "BlockThatReads",
            ["MethodBlockCloses"] = "BlockThatClosesWithItself",
            ["MethodBlockSnapshots"] = "BlockThatSnapshots",
            ["ListSortWithAnOptionalComparerOfTheMethodOfTheBlock"] = "SortInABlock"
        };

    // The rules of a shape for the helper assembly with no reference to the library. Only a shape
    // whose rules are different from the first table is here. See the test for a package.
    private static readonly Dictionary<string, string[]> ExpectedForAPackage =
        new()
        {
            ["Ab80ACellThatAPackageKeepsAndGivesBack"] = ["SF1001"],
            ["Ab110ACellThatCodeInAFieldKeepsAndGivesBack"] = ["SF1001"],
            ["DvLinksEscapeOfThePlaceholderAsObject"] = [],
            ["DvLinksEscapeThroughAMethod"] = [],
            ["DvLinksEscapeThroughAnIndexerSetter"] = [],
            ["DvLinksEscapeThroughAPropertySetter"] = [],
            ["DvLinksEscapeOfAnArrayOfThePlaceholder"] = [],
            ["DvLinksEscapeOfThePlaceholderCastToObject"] = [],
            ["Ab252MarkInALocalThatGetsASecondValue"] = [],
            ["Ab252LocalWithNoMarkGivenAway"] = [],
            ["Ab331MarkInALocalAssignedTwiceGivenAway"] = [],
            ["Ag99ACalleeWithOnlyAFunctionCountsTheQueryThatItGives"] = ["SF1001"]
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

    // The shapes whose rules are a report on the careful side, of a type that docs/docs/loops.md
    // names. Each one returns at run time.
    private static readonly HashSet<string> ReportsOnTheCarefulSide =
    [
        // The function of a merge runs only when the two streams fire in one transaction. The block
        // sends into one stream only.
        "R4ApiMergeFunctionThatReturnsALinkedCellWithOneSend",
        // The updates of a cell that is not a sink have the content of the cell, with its initial
        // value.
        "R4ApiUpdatesOfAHoldWithALinkedInitialValue",
        // The switch has no default and no pattern that matches each value. Thus, the analyzer
        // cannot know that a section runs before the code after the switch.
        "R5FuAwaitUsingInASwitchSectionStopsTheCodeAfter",
        "Ab285AwaitInTheOnlySectionOfASwitchWithNoDefault",
        "Ab285AwaitInTheSectionsOfASwitchOnTrueAndFalse",
        // A call that gets this can give a delegate member of this a new value. The analyzer does
        // not read that call. Thus, it counts each value that the code of the class gives the member.
        "Ab352CallWithThisThatDoesNotSetTheMember"
    ];

    private static readonly Assembly[] ShapeLibraries =
    [
        typeof(Cell<>).Assembly,
        typeof(CellExtensionMethods).Assembly,
        typeof(Maybe<>).Assembly,
        typeof(IBindableFactory).Assembly,
        typeof(IOneWayBindableValue<>).Assembly,
        typeof(IIdentity<>).Assembly,
        typeof(CollectionExtensionMethods).Assembly,
        typeof(AsyncMapStatus).Assembly,
        typeof(AsyncStreamExtensions).Assembly,
        // The shapes of the serializer rule call it. The run of the shapes holds the rule to it.
        typeof(Newtonsoft.Json.JsonConvert).Assembly
    ];

    private static Task<string> ReadShapes() =>
        File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "Shapes.cs"));

    // The line of ShapesNoBody.cs that makes its assembly reference the library.
    private const string ReferenceToTheLibrary = "KnowsTheLibrary = typeof(Cell<>);";

    // The assembly that the shapes call as code with no body to read. The analyzer gets its bytes
    // and no source.
    private static Task<byte[]> CompileNoBody() => CompileNoBody(knowsTheLibrary: true);

    // The same assembly. With false, no code of it names a type of the library. Thus, the
    // assembly has no reference to the library.
    private static async Task<byte[]> CompileNoBody(bool knowsTheLibrary)
    {
        string source =
            await File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "ShapesNoBody.cs"));

        if (!source.Contains(value: ReferenceToTheLibrary, comparisonType: StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ShapesNoBody.cs does not have the line that names the library.");
        }

        return AnalyzerHarness.Emit(
            AnalyzerHarness.Compile(
                source: knowsTheLibrary
                    ? source
                    : source.Replace(
                        oldValue: ReferenceToTheLibrary,
                        newValue: "KnowsTheLibrary = typeof(List<>);",
                        comparisonType: StringComparison.Ordinal),
                libraries: ShapeLibraries,
                assemblyName: "SodaFlow.Analyzers.ShapesNoBody"));
    }

    [Test]
    public async Task EachShapeReportsTheExpectedRules() =>
        await EachShapeReportsTheExpectedRulesWith(noBody: await CompileNoBody(), isAPackage: false);

    /// <summary>
    ///     Runs the shapes with a helper assembly that has no reference to the
    ///     library. The analyzer then knows that the code of that assembly cannot
    ///     sample a cell. That code can keep each value that it gets, and can
    ///     put an object into each collection that it can get to. Thus, a shape
    ///     has the same rules as with the reference.
    /// </summary>
    /// <remarks>
    ///     A pending value that goes to code which can know the library gets
    ///     SF1003, and the walk does not follow it. The same value to this
    ///     assembly gets no SF1003, and the walk follows it to a read. A shape
    ///     with such a value has its rules for this assembly in
    ///     <see cref="ExpectedForAPackage" />.
    /// </remarks>
    [Test]
    public async Task EachShapeReportsTheSameRulesForAPackageThatCannotKnowTheLibrary()
    {
        byte[] package = await CompileNoBody(knowsTheLibrary: false);

        await Assert.That(LibraryReferencesOf(await CompileNoBody()))
            .IsNotEmpty()
            .Because("the helper assembly of the other tests has a reference to the library");

        await Assert.That(LibraryReferencesOf(package))
            .IsEmpty()
            .Because("this helper assembly has no reference to the library");

        await EachShapeReportsTheExpectedRulesWith(noBody: package, isAPackage: true);
    }

    // The name of each assembly of the library that an image references.
    private static IReadOnlyList<string> LibraryReferencesOf(IEnumerable<byte> image)
    {
        PortableExecutableReference reference = MetadataReference.CreateFromImage(image);

        ISymbol? read =
            CSharpCompilation.Create(assemblyName: "Reader", references: [reference])
                .GetAssemblyOrModuleSymbol(reference);

        return read is IAssemblySymbol assembly
            ?
            [
                .. assembly.Modules
                    .SelectMany(static module => module.ReferencedAssemblies)
                    .Select(static identity => identity.Name)
                    .Where(static name => name.StartsWith(value: "SodaFlow", comparisonType: StringComparison.Ordinal))
            ]
            : throw new InvalidOperationException("The image is not an assembly.");
    }

    private static async Task EachShapeReportsTheExpectedRulesWith(byte[] noBody, bool isAPackage)
    {
        string source = await ReadShapes();

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly,
                    images: [noBody]));

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
                            value: isAPackage
                                   && ExpectedForAPackage.TryGetValue(key: shape, value: out string[]? forAPackage)
                                ? forAPackage
                                : Expected.TryGetValue(key: shape, value: out string[]? rules)
                                    ? rules
                                    : []);

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

        await NoMismatches(mismatches);

        await Assert.That(Expected.Keys.Concat(ExpectedForAPackage.Keys).Except(shapes))
            .IsEmpty()
            .Because("every expectation names a shape");
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
        byte[] noBody = await CompileNoBody();

        Assembly assembly =
            AnalyzerHarness.Load(
                compilation: AnalyzerHarness.Compile(
                    source: await ReadShapes(),
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly,
                    images: [noBody]),
                images: [noBody]);

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

            // The rules for a package have a rule for the read, where the usual rules have only
            // SF1003. The shape does the same at run time for each of the two.
            string[] rules =
                ExpectedForAPackage.TryGetValue(key: reportedIn, value: out string[]? forAPackage)
                    ? forAPackage
                    : Expected.TryGetValue(key: reportedIn, value: out string[]? listed)
                        ? listed
                        : [];

            IReadOnlyList<Outcome> permitted =
                rules.Length == 0 || ReportsOnTheCarefulSide.Contains(reportedIn)
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

        await NoMismatches(mismatches);

        await Assert.That(count).IsGreaterThanOrEqualTo(1).Because("the test found shapes to run");
    }

    /// <summary>
    ///     The analyzer skips the search for a member that the platform calls
    ///     when the compilation declares no such member. A comparer is one of
    ///     them. The shapes file declares each kind, thus it cannot show that the
    ///     search runs for a compilation that declares only a comparer. The
    ///     search has one gate for a call and one for an indexer, and the code has
    ///     the two. The first put into each empty collection calls nothing, thus
    ///     each block has one report, for its second put.
    /// </summary>
    [Test]
    public async Task AKeptComparerIsReadInACompilationThatDeclaresNoOtherMember()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();

                public static Cell<int> Kept() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        SortedSet<int> set = new(new ByOffset(l.Map(v => v)));
                        set.Add(1);
                        set.Add(2);

                        return S.Hold(set.Count);
                    });

                public static Cell<int> KeptByAnIndexer() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        SortedDictionary<int, int> all = new(new ByOffset(l.Map(v => v)));
                        all[1] = 1;
                        all[2] = 2;

                        return S.Hold(all.Count);
                    });

                public sealed class ByOffset(Cell<int> offset) : IComparer<int>
                {
                    public int Compare(int x, int y) => x.CompareTo(y + offset.Sample());
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyAComparer"));

        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1005", "SF1005"]);

        // The lines of the second Add and of the second assignment through the indexer.
        await Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line))
            .IsEquivalentTo([12, 22]);
    }

    /// <summary>
    ///     The analyzer asks the compiler for a <c>Deconstruct</c> method in two
    ///     cases. The compilation declares one, or the value is a pair with a
    ///     state for each part. The shapes file declares one, thus it cannot show
    ///     the second case. An assignment, a foreach, and a positional pattern
    ///     each have that condition. A state must keep the answer for the second
    ///     case in three more places. The state goes to a method, the code sets a
    ///     member of the collection that has the pair, or a join fills a part.
    /// </summary>
    [Test]
    public async Task APairGivesItsPartsInACompilationThatDeclaresNoDeconstruct()
    {
        const string source =
            """
            using System.Collections.Generic;
            using System.Linq;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);
                private static readonly List<object> Kept = [];

                public static Cell<int> PlainKeyByAnAssignment() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        (Shown k, Shown _) = p;
                        Kept.Add(k.ToString());

                        return S.Hold(0);
                    });

                public static Cell<int> PendingValueByAnAssignment() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        (Shown _, Shown v) = p;
                        Kept.Add(v.ToString());

                        return S.Hold(0);
                    });

                public static Cell<int> PlainKeyByAForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<KeyValuePair<Shown, Shown>> all = [new(new Shown(Plain), new Shown(l.Map(v => v)))];

                        foreach ((Shown k, Shown _) in all)
                        {
                            Kept.Add(k.ToString());
                        }

                        return S.Hold(0);
                    });

                public static Cell<int> PendingValueByAForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<KeyValuePair<Shown, Shown>> all = [new(new Shown(Plain), new Shown(l.Map(v => v)))];

                        foreach ((Shown _, Shown v) in all)
                        {
                            Kept.Add(v.ToString());
                        }

                        return S.Hold(0);
                    });

                public static Cell<int> PlainKeyByAPattern() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));

                        if (p is (var k, _))
                        {
                            Kept.Add(k.ToString());
                        }

                        return S.Hold(0);
                    });

                public static Cell<int> PendingValueByAPattern() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));

                        if (p is (_, var v))
                        {
                            Kept.Add(v.ToString());
                        }

                        return S.Hold(0);
                    });

                // The pair goes to a method, which gives each of its states an origin.
                public static Cell<int> PlainKeyInAMethod() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        Kept.Add(KeyTextOf(p));

                        return S.Hold(0);
                    });

                // The code sets a member of the list that has the pair.
                public static Cell<int> PlainKeyAfterAMemberOfTheList() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<KeyValuePair<Shown, Shown>> all = [new(new Shown(Plain), new Shown(l.Map(v => v)))];
                        all.Capacity = 4;

                        foreach ((Shown k, Shown _) in all)
                        {
                            Kept.Add(k.ToString());
                        }

                        return S.Hold(0);
                    });

                // The value is one of two pairs, and the one from the dictionary has no state for
                // its parts.
                public static Cell<int> PlainKeyOfOneOfTwoPairs() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Dictionary<Shown, Shown> d = new() { { new Shown(Plain), new Shown(Plain) } };
                        KeyValuePair<Shown, Shown> made = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        KeyValuePair<Shown, Shown> p = d.Count > 5 ? made : d.First();
                        (Shown k, Shown _) = p;
                        Kept.Add(k.ToString());

                        return S.Hold(0);
                    });

                private static string KeyTextOf(KeyValuePair<Shown, Shown> pair)
                {
                    (Shown k, Shown _) = pair;

                    return k.ToString();
                }

                public sealed class Shown(Cell<int> share)
                {
                    public override string ToString() => share.Sample().ToString();
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyAPair"));

        // One report for each read of the pending value, and none for a read of the plain key.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1005", "SF1005", "SF1005"]);
    }

    /// <summary>
    ///     A literal that no code changes keeps its positions. An index that the
    ///     literal has no element for, and <c>First</c> of a literal with no
    ///     element, give no position. The analyzer then reads the collection as one
    ///     state. At run time, each of these reads throws an exception that is not
    ///     the one of a loop, thus the shapes file cannot have them.
    /// </summary>
    [Test]
    public async Task AnIndexOutsideALiteralGivesNoPosition()
    {
        const string source =
            """
            using System.Collections.Generic;
            using System.Linq;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> AfterTheLastElement() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [Plain, l.Map(v => v)];

                        return S.Hold(cs[2].Sample());
                    });

                public static Cell<int> BeforeTheFirstElement() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [Plain, l.Map(v => v)];

                        return S.Hold(cs[-1].Sample());
                    });

                public static Cell<int> FirstOfNoElement() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [];

                        return S.Hold(cs.First().Sample() + l.Sample());
                    });
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyALiteral"));

        // The two reads by an index have the state of all the elements, and one of those is
        // pending. The third method reads the looped cell itself.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1001", "SF1001", "SF1001"]);
    }

    /// <summary>
    ///     Only <c>First</c> of <c>System.Linq.Enumerable</c> gives the first
    ///     position. A class with the name <c>Enumerable</c> in a different
    ///     namespace can give a different element. The shapes file cannot declare
    ///     such a class, because its own uses of <c>Enumerable</c> then name that
    ///     class. The first method has a literal, and the second one reads the
    ///     positions of the store through a second local.
    /// </summary>
    [Test]
    public async Task FirstOfAnEnumerableOutsideSystemLinqGivesNoPosition()
    {
        const string source =
            """
            using System.Collections.Generic;
            using Other;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> FirstOfALiteral() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [Plain, l.Map(v => v)];

                        return S.Hold(cs.First().Sample());
                    });

                public static Cell<int> FirstThroughASecondLocal() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [Plain, l.Map(v => v)];
                        List<Cell<int>> same = cs;

                        return S.Hold(same.First().Sample());
                    });
            }

            namespace Other
            {
                public static class Enumerable
                {
                    public static T First<T>(this List<T> all) => all[all.Count - 1];
                }
            }
            """;

        CSharpCompilation compilation =
            AnalyzerHarness.Compile(source: source, libraries: ShapeLibraries, assemblyName: "OnlyAnEnumerable");

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(analyzer: new LoopReadAnalyzer(), compilation: compilation);

        // First gives the last element, which is the pending cell.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1001", "SF1001"]);

        Type only =
            AnalyzerHarness.Load(compilation: compilation, images: []).GetType("Only")
            ?? throw new InvalidOperationException("The source has no class with the name Only.");

        await Assert.That(
                only.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(static method => Run(method).Outcome))
            .IsEquivalentTo([Outcome.ReadsEarly, Outcome.ReadsEarly]);
    }

    /// <summary>
    ///     A put with an index that the list has no position for gives no
    ///     position. The analyzer then reads the list as one state. At run time,
    ///     each of these puts throws an exception that is not the one of a loop,
    ///     thus the shapes file cannot have them.
    /// </summary>
    [Test]
    public async Task APutOutsideAListGivesNoPosition()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> AnInsertAfterTheLastPosition() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [l.Map(v => v)];
                        List<Cell<int>> same = cs;
                        same.Insert(3, Plain);

                        return S.Hold(cs[0].Sample());
                    });

                public static Cell<int> ARemoveAtAfterTheLastPosition() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [l.Map(v => v)];
                        List<Cell<int>> same = cs;
                        same.RemoveAt(3);

                        return S.Hold(cs[0].Sample());
                    });

                public static Cell<int> AnAssignmentAfterTheLastPosition() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [l.Map(v => v)];
                        List<Cell<int>> same = cs;
                        same[3] = Plain;

                        return S.Hold(cs[0].Sample());
                    });
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyAList"));

        // Each list has the pending cell, and each read is of it.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1001", "SF1001", "SF1001"]);
    }

    /// <summary>
    ///     A method of an object reads a field of that object with no
    ///     <c>this</c> before the name. The field is the one collection that the
    ///     constructor gave it. Thus, a plain cell through its first element
    ///     replaces the pending one, and the read has no report.
    /// </summary>
    /// <remarks>
    ///     The shapes file cannot have this method: the code of this solution
    ///     must have <c>this</c> before each member.
    /// </remarks>
    [Test]
    public async Task AFieldWithNoThisBeforeItsNameIsTheCollectionOfItsObject()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public sealed class Slot
            {
                public Cell<int>? Share { get; set; }
            }

            public sealed class Rows
            {
                private readonly List<Slot> all;

                public Rows(Slot slot)
                {
                    all = [slot];
                }

                public void MakeTheFirstPlain(Cell<int> plain) => all[0].Share = plain;
            }

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> TheFirstOfTheRows() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Slot y = new() { Share = l.Map(v => v) };
                        Rows rows = new(y);
                        rows.MakeTheFirstPlain(Plain);

                        return S.Hold(y.Share!.Sample());
                    });
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyAField"));

        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id)).IsEmpty();
    }

    /// <summary>
    ///     The walk has a limit for the number of callee bodies that it reads for
    ///     one block. A member that the block calls after that limit is code that
    ///     the analyzer does not read. The object of the member has a pending cell, thus the call
    ///     gets the report for such code.
    /// </summary>
    /// <remarks>
    ///     The helpers call each other eight times at each of three levels, which
    ///     is more bodies than the limit. The shapes file cannot have this block:
    ///     its calls after the limit get the same rule, and a shape has only a set
    ///     of rules.
    /// </remarks>
    [Test]
    public async Task AMemberThatTheBlockCallsPastTheLimitOfBodiesGetsAReport()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> PastTheLimit() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Reader reader = new(l.Map(v => v));
                        Kept.Add(Wide3(l.Map(v => v)));
                        Kept.Add(reader.Now);

                        return S.Hold(0);
                    });

                private static int Wide0(Cell<int> c) => c == null ? 1 : 0;

                private static int Wide1(Cell<int> c) =>
                    Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c);

                private static int Wide2(Cell<int> c) =>
                    Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c);

                private static int Wide3(Cell<int> c) =>
                    Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c);

                public sealed class Reader(Cell<int> share)
                {
                    public int Now => share.Sample();
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyPastTheLimit"));

        string[] notRead =
        [
            .. diagnostics
                .Where(static diagnostic => diagnostic.Id == "SF1003")
                .Select(static diagnostic =>
                    diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? string.Empty)
        ];

        await Assert.That(notRead).Contains("reader.Now");
    }

    /// <summary>
    ///     A callee that gets only an object of the store, and no state from the
    ///     loop, has its own limit of bodies. A block that calls many such callees
    ///     has each body that a pending value needs.
    /// </summary>
    /// <remarks>
    ///     The helpers get an empty list, and call each other eight times at each
    ///     of three levels. That is more bodies than the limit. The helper after
    ///     them samples a pending cell, and the walk reads it.
    /// </remarks>
    [Test]
    public async Task CalleesThatGetOnlyObjectsDoNotUseTheBodiesOfAPendingValue()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ManyCalleesWithAnObject() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> list = [];
                        Kept.Add(Wide3(list));
                        Kept.Add(NowOf(l.Map(v => v)));

                        return S.Hold(0);
                    });

                private static int NowOf(Cell<int> c) => c.Sample();

                private static int Wide0(List<Cell<int>> xs) => xs.Count;

                private static int Wide1(List<Cell<int>> xs) =>
                    Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs);

                private static int Wide2(List<Cell<int>> xs) =>
                    Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs);

                private static int Wide3(List<Cell<int>> xs) =>
                    Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs);
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyManyCallees"));

        // The helper samples its parameter, and the walk says so. A helper that the walk does not
        // read gets the report for such code and no more.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id)).IsEquivalentTo(["SF1004"]);
    }

    /// <summary>
    ///     An editor cancels the analysis of a file at each change to the file.
    ///     The walk of one block must end at that time, and not at the end of the
    ///     block.
    /// </summary>
    /// <remarks>
    ///     The test gives the walk the action that gets each diagnostic. That
    ///     action cancels the token at the first diagnostic. The block has a second
    ///     read after that one. Thus, a walk that does not look at the token gives
    ///     two diagnostics and throws nothing.
    /// </remarks>
    [Test]
    public async Task TheWalkStopsWhenTheCompilerCancelsTheAnalysis()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Canceled
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> TwoReads() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Kept.Add(l.Sample());
                        Kept.Add(l.Map(v => v).Sample());

                        return S.Hold(0);
                    });
            }
            """;

        CSharpCompilation compilation =
            AnalyzerHarness.Compile(source: source, libraries: ShapeLibraries, assemblyName: "OnlyCanceled");

        SyntaxTree tree = compilation.SyntaxTrees.Single();

        InvocationExpressionSyntax block =
            (await tree.GetRootAsync())
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(static call =>
                call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WithoutCaptures" });

        // The control: no code cancels the token, and the walk reports the two reads.
        List<Diagnostic> all = [];

        LoopReadAnalyzer.AnalyzeLoopBlock(
            invocation: block,
            model: compilation.GetSemanticModel(tree),
            symbols: new SodaFlowSymbols(compilation),
            report: all.Add,
            cancellation: CancellationToken.None);

        await Assert.That(all.Select(static diagnostic => diagnostic.Id)).IsEquivalentTo(["SF1001", "SF1001"]);

        using CancellationTokenSource stop = new();
        List<Diagnostic> beforeTheStop = [];
        bool stopped = false;

        try
        {
            LoopReadAnalyzer.AnalyzeLoopBlock(
                invocation: block,
                model: compilation.GetSemanticModel(tree),
                symbols: new SodaFlowSymbols(compilation),
                report: KeepsAndCancels(keep: beforeTheStop.Add, stop: stop),
                cancellation: stop.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        await Assert.That(stopped).IsTrue();
        await Assert.That(beforeTheStop.Count).IsEqualTo(1);
    }

    // An action for the walk that keeps each diagnostic and cancels the token at each one.
    private static Action<Diagnostic> KeepsAndCancels(Action<Diagnostic> keep, CancellationTokenSource stop) =>
        diagnostic =>
        {
            keep(diagnostic);
            stop.Cancel();
        };

    /// <summary>
    ///     A function that a callee runs for each element can be in a second one.
    ///     The walk reads each of them again while a read leaves a new state. A
    ///     read that does not end multiplies with each level.
    /// </summary>
    /// <remarks>
    ///     Each level makes a list and gives a pending cell to a chain of three
    ///     locals. The mark of the list of a turn stayed after the turn, and each
    ///     read then left a different number. Four levels took 40 seconds, and
    ///     the six levels here did not end. The token has a time that is much
    ///     longer than the walk needs, and the walk stops when the time is over.
    /// </remarks>
    [Test]
    public async Task AFunctionForEachElementInASecondOneEndsInAShortTime()
    {
        const int levels = 6;
        StringBuilder body = new();

        for (int level = 0; level < levels; level++)
        {
            body.AppendLine($"Cell<int>? a{level} = null; Cell<int>? b{level} = null; Cell<int>? c{level} = null;");
            body.AppendLine($"new List<int> {{ 1, 2, 3, 4 }}.ForEach(e{level} =>");
            body.AppendLine("{");
            body.AppendLine($"if (c{level} != null) {{ Kept.Add(c{level}.Sample()); }}");
            body.AppendLine($"c{level} = b{level}; b{level} = a{level}; a{level} = l.Map(v => v);");
        }

        for (int level = 0; level < levels; level++)
        {
            body.AppendLine("});");
        }

        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(60));

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: OneBlock(body.ToString()),
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyNestedFunctions"),
                cancellation: limit.Token);

        // Each level samples the last local of its chain.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(Enumerable.Repeat(element: "SF1001", count: levels));
    }

    /// <summary>
    ///     The analyzer reads the getter of an indexer of this compilation only
    ///     when the compilation declares an indexer or a <c>Slice</c> method. The
    ///     shapes file declares the two, thus it cannot show that each one alone
    ///     is sufficient. Each source here declares one of them, and that member
    ///     samples the pending cell.
    /// </summary>
    [Test]
    public async Task AnIndexerOrASliceIsReadInACompilationThatDeclaresOnlyThatOne()
    {
        const string indexer =
            """
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();

                public static Cell<int> ByAnIndexer() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Box box = new(l.Map(v => v));

                        return S.Hold(box[0]);
                    });

                public sealed class Box(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public int this[int index] => this.share.Sample() + index;
                }
            }
            """;

        const string slice =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ByASlice() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Run run = new(l.Map(v => v));
                        Kept.Add(run[0..1]);

                        return S.Hold(0);
                    });

                public sealed class Run(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public int Length => 2;

                    public int Slice(int start, int length) => this.share.Sample() + start + length;
                }
            }
            """;

        await Assert.That(await RulesIn(source: indexer, assemblyName: "OnlyAnIndexer", images: []))
            .IsEquivalentTo(["SF1005"]);

        await Assert.That(await RulesIn(source: slice, assemblyName: "OnlyASlice", images: []))
            .IsEquivalentTo(["SF1005"]);
    }

    /// <summary>
    ///     The walk reads a function with no call only when a node of it can run
    ///     code of the compilation. A getter with an expression body is such
    ///     code. The shapes file declares an implicit conversion, thus each name
    ///     there can run code, and the shapes cannot show the getter alone. This
    ///     source declares no conversion and no operator. The function of the map
    ///     reads only the getter, and the getter samples the pending cell.
    /// </summary>
    [Test]
    public async Task AGetterWithAnExpressionBodyIsReadInAFunctionWithNoCall()
    {
        const string source =
            """
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> ByAGetter() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Box box = new(l.Map(v => v));

                        return S.Hold(Plain.Map(_ => box.Now).Sample());
                    });

                public sealed class Box(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public int Now => this.share.Sample();
                }
            }
            """;

        await Assert.That(await RulesIn(source: source, assemblyName: "OnlyAGetter", images: []))
            .IsEquivalentTo(["SF1001"]);
    }

    /// <summary>
    ///     An indexer of the platform calls <c>GetHashCode</c> of a key of this
    ///     compilation. The walk reads a function whose only code is such an
    ///     indexer also in a compilation that declares no indexer. The shapes
    ///     file declares one, thus it cannot show this case. The function of the
    ///     map reads a dictionary, and the key samples the pending cell.
    /// </summary>
    [Test]
    public async Task APlatformIndexerIsReadInACompilationThatDeclaresNoIndexer()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> ByADictionary() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Dictionary<Key, int> all = new() { [new Key(Plain)] = 1 };
                        Key key = new(l.Map(v => v));

                        return S.Hold(Plain.Map(_ => all[key]).Sample());
                    });

                public sealed class Key(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public override int GetHashCode() => this.share.Sample();
                }
            }
            """;

        await Assert.That(await RulesIn(source: source, assemblyName: "OnlyAKey", images: []))
            .IsEquivalentTo(["SF1001"]);
    }

    /// <summary>
    ///     The analyzer reads a conversion that the compiler adds only when the
    ///     compilation declares an implicit or an explicit operator. The shapes
    ///     file declares the two. Each source here declares only one of them, and
    ///     the operator samples the pending cell. A foreach adds an explicit
    ///     conversion of each element to the type of its variable.
    /// </summary>
    [Test]
    public async Task AConversionIsReadInACompilationThatDeclaresOnlyOneOperator()
    {
        const string implicitOnly =
            """
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();

                public static Cell<int> ByAnImplicitConversion() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Box box = new(l.Map(v => v));
                        int number = box;

                        return S.Hold(number);
                    });

                public sealed class Box(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public static implicit operator int(Box box) => box.share.Sample();
                }
            }
            """;

        const string explicitOnly =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ByTheConversionOfAForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Box> all = [new Box(l.Map(v => v))];

                        foreach (int number in all)
                        {
                            Kept.Add(number);
                        }

                        return S.Hold(0);
                    });

                public sealed class Box(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public static explicit operator int(Box box) => box.share.Sample();
                }
            }
            """;

        await Assert.That(await RulesIn(source: implicitOnly, assemblyName: "OnlyAnImplicitOperator", images: []))
            .IsEquivalentTo(["SF1004"]);

        await Assert.That(await RulesIn(source: explicitOnly, assemblyName: "OnlyAnExplicitOperator", images: []))
            .IsEquivalentTo(["SF1004"]);
    }

    /// <summary>
    ///     The analyzer reads the <c>DisposeAsync</c> of an <c>await using</c>
    ///     only in a compilation that declares a method with that name. The
    ///     shapes file also declares <c>Dispose</c>. This source declares only
    ///     <c>DisposeAsync</c>, and it samples the pending cell.
    /// </summary>
    [Test]
    public async Task ADisposeAsyncIsReadInACompilationThatDeclaresNoDispose()
    {
        const string source =
            """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ByAnAwaitUsing() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Func<Task> run = async () => { await using Release release = new(l.Map(v => v)); };
                        run().Wait(0);

                        return S.Hold(0);
                    });

                public sealed class Release(Cell<int> share) : IAsyncDisposable
                {
                    private readonly Cell<int> share = share;

                    public ValueTask DisposeAsync()
                    {
                        Kept.Add(this.share.Sample());

                        return ValueTask.CompletedTask;
                    }
                }
            }
            """;

        await Assert.That(await RulesIn(source: source, assemblyName: "OnlyADisposeAsync", images: []))
            .IsEquivalentTo(["SF1005"]);
    }

    /// <summary>
    ///     An await foreach calls <c>GetAsyncEnumerator</c>, and
    ///     <c>DisposeAsync</c> of the enumerator at the end. The analyzer reads
    ///     each one only in a compilation that declares a method with that name.
    ///     The shapes file also declares <c>GetEnumerator</c> and <c>Dispose</c>,
    ///     and these sources do not. In the first, the enumerator samples the
    ///     pending cell. In the second, its <c>DisposeAsync</c> does.
    /// </summary>
    [Test]
    public async Task AnAsyncEnumeratorIsReadInACompilationThatDeclaresNoEnumerator()
    {
        const string start =
            """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ByAnAwaitForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Func<Task> run = async () =>
                        {
                            await foreach (int item in new Items(l.Map(v => v)))
                            {
                                Kept.Add(item);
                            }
                        };

                        run().Wait(0);

                        return S.Hold(0);
                    });

                public sealed class Items(Cell<int> share) : IAsyncEnumerable<int>
                {
                    private readonly Cell<int> share = share;

                    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                    {
                        Kept.Add(this.share.Sample());

                        return new Walker();
                    }
                }

                public sealed class Walker : IAsyncEnumerator<int>
                {
                    public int Current => 1;

                    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);

                    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                }
            }
            """;

        const string end =
            """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ByAnAwaitForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Func<Task> run = async () =>
                        {
                            await foreach (int item in new Items(l.Map(v => v)))
                            {
                                Kept.Add(item);
                            }
                        };

                        run().Wait(0);

                        return S.Hold(0);
                    });

                public sealed class Items(Cell<int> share) : IAsyncEnumerable<int>
                {
                    private readonly Cell<int> share = share;

                    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
                        new Walker(this.share);
                }

                public sealed class Walker(Cell<int> share) : IAsyncEnumerator<int>
                {
                    private readonly Cell<int> share = share;

                    public int Current => 1;

                    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);

                    public ValueTask DisposeAsync()
                    {
                        Kept.Add(this.share.Sample());

                        return ValueTask.CompletedTask;
                    }
                }
            }
            """;

        await Assert.That(await RulesIn(source: start, assemblyName: "OnlyAGetAsyncEnumerator", images: []))
            .IsEquivalentTo(["SF1005"]);

        await Assert.That(await RulesIn(source: end, assemblyName: "OnlyADisposeAsyncOfAnEnumerator", images: []))
            .IsEquivalentTo(["SF1005"]);
    }

    /// <summary>
    ///     The compiler writes the <c>==</c> of a record, and no declaration of
    ///     the code names it. This source declares a record and no operator or
    ///     <c>ToString</c>. The <c>==</c> calls the <c>Equals</c> of each member,
    ///     and that <c>Equals</c> samples the pending cell.
    /// </summary>
    [Test]
    public async Task TheEqualityOfARecordIsReadInACompilationThatDeclaresNoOperator()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);
                private static readonly List<object> Kept = [];

                public static Cell<int> ByTheEqualityOfARecord() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Pair a = new(new Alike(l.Map(v => v)));
                        Pair b = new(new Alike(Plain));
                        Kept.Add(a == b);

                        return S.Hold(0);
                    });

                public sealed record Pair(Alike A);

                public sealed class Alike(Cell<int> share)
                {
                    private readonly Cell<int> share = share;

                    public override bool Equals(object? obj) =>
                        obj is Alike other && other.share.Sample() == this.share.Sample();

                    public override int GetHashCode() => 1;
                }
            }
            """;

        await Assert.That(await RulesIn(source: source, assemblyName: "OnlyARecord", images: []))
            .IsEquivalentTo(["SF1005"]);
    }

    /// <summary>
    ///     A block can be the name of a method with no body, as a method of an
    ///     interface is. The analyzer has no code to read for that block. Thus,
    ///     it reports SF1003 at the block, because an implementation can read the
    ///     placeholder. The method that runs here returns and reads nothing.
    /// </summary>
    [Test]
    public async Task AMethodWithNoBodyIsNoBlockToRead()
    {
        const string source =
            """
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly IMaker Maker = new PlainMaker();

                public static Cell<int> Run() => Cell.Loop<int>().WithoutCaptures(Maker.Make);

                public interface IMaker
                {
                    Cell<int> Make(LoopedCell<int> l);
                }

                public sealed class PlainMaker : IMaker
                {
                    public Cell<int> Make(LoopedCell<int> l) => S.Hold(0);
                }
            }
            """;

        await Assert.That(await RulesIn(source: source, assemblyName: "OnlyAnInterfaceBlock", images: []))
            .IsEquivalentTo(["SF1003"]);
    }

    /// <summary>
    ///     The analyzer reads a call of <c>Exchange</c> of a type with the name
    ///     <c>Interlocked</c> in <c>System.Threading</c> as a write to a location.
    ///     That needs a location and a value. A package can declare such a type
    ///     in a type of that namespace, with a method of one parameter. A call of
    ///     it is a call of code that cannot know the library. That code gives
    ///     back the object, and the object then gets a plain cell.
    /// </summary>
    [Test]
    public async Task AnExchangeWithOneParameterIsNoExchange()
    {
        const string package =
            """
            namespace System.Threading
            {
                public static class Outer
                {
                    public static class Interlocked
                    {
                        public static T Exchange<T>(T only) => only;
                    }
                }
            }
            """;

        const string source =
            """
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> GivesBackItsArgument() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Slot x = new() { Share = l.Map(v => v) };
                        object y = System.Threading.Outer.Interlocked.Exchange(x);
                        x.Share = Plain;

                        return S.Hold(((Slot)y).Share!.Sample());
                    });

                public sealed class Slot
                {
                    public Cell<int>? Share { get; set; }
                }
            }
            """;

        byte[] image =
            AnalyzerHarness.Emit(
                AnalyzerHarness.Compile(source: package, libraries: [], assemblyName: "OnlyAnExchange"));

        await Assert.That(await RulesIn(source: source, assemblyName: "OnlyACallOfAnExchange", images: [image]))
            .IsEmpty();
    }

    /// <summary>
    ///     The analyzer reads the initial value of a hold only for a
    ///     <c>Hold</c> or an <c>Accum</c> that is an extension method of a stream.
    ///     Each such method of the library is one. A compilation with the name of
    ///     an assembly of the library is a part of the library. Thus, it can
    ///     declare a <c>Hold</c> of a different value.
    ///     That method here gives back the cell of cells that it gets, and the
    ///     cell in it is pending.
    /// </summary>
    [Test]
    public async Task AHoldOfACellIsNoHoldInACompilationOfTheLibrary()
    {
        const string source =
            """
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> ReadsTheCellOfCells() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Cell<Cell<int>> all = Cell.Constant(l.Map(v => v));
                        Cell<Cell<int>> held = all.Hold(Plain);

                        return S.Hold(held.Sample().Sample());
                    });

                public static Cell<Cell<int>> Hold(this Cell<Cell<int>> cells, Cell<int> initial) => cells;
            }
            """;

        await Assert.That(await RulesIn(source: source, assemblyName: "SodaFlow.FSharp", images: []))
            .IsEquivalentTo(["SF1001"]);
    }

    // The rules that the analyzer reports for a source that is a compilation of its own.
    private static async Task<IReadOnlyList<string>> RulesIn(
        string source,
        string assemblyName,
        IEnumerable<byte[]> images) =>
    [
        .. (await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: assemblyName,
                    images: images)))
            .Select(static diagnostic => diagnostic.Id)
    ];

    // A file with one loop block that has the given statements.
    private static string OneBlock(string statements) =>
        $$"""
          using System.Collections.Generic;
          using SodaFlow;

          public static class Only
          {
              private static readonly StreamSink<int> S = Stream.CreateSink<int>();
              private static readonly List<object> Kept = [];

              public static Cell<int> Run() =>
                  Cell.Loop<int>().WithoutCaptures(l =>
                  {
                      {{statements}}

                      return S.Hold(0);
                  });
          }
          """;

    // An assertion on a collection names ten of its items and counts the others. The reason names
    // each mismatch, so that one run says all that changed.
    private static async Task NoMismatches(IReadOnlyCollection<string> mismatches) =>
        await Assert.That(mismatches.Count)
            .IsEqualTo(0)
            .Because(string.Join(separator: Environment.NewLine, values: mismatches));

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
            // A serializer puts the exception of a getter into one of its own.
            Outcome outcome = Outcome.Other;

            for (Exception? cause = thrown; cause != null && outcome == Outcome.Other; cause = cause.InnerException)
            {
                outcome = OutcomeOf(cause);
            }

            return (outcome, $"throws {thrown.GetType().Name}: {thrown.Message}");
        }
    }

    private static Outcome OutcomeOf(Exception thrown) =>
        thrown switch
        {
            DependencyCycleException => Outcome.MakesCycle,
            InvalidOperationException when Says(exception: thrown, text: "sampled before it was looped") =>
                Outcome.ReadsEarly,
            InvalidOperationException when Says(exception: thrown, text: "lazy sample forces itself") =>
                Outcome.ForcesItself,
            _ => Outcome.Other
        };

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
