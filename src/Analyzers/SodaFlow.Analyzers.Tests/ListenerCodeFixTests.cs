using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SodaFlow.Analyzers.CodeFixes;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Applies each fix of <see cref="DroppedListenerCodeFixProvider" /> to a
///     sample, and checks the text that it gives. Each fixed sample must compile
///     with no new warning, and SF2001 must not report it.
/// </summary>
internal sealed class ListenerCodeFixTests
{
    private static readonly Assembly[] ShapeLibraries =
    [
        typeof(Cell<>).Assembly,
        typeof(CellExtensionMethods).Assembly
    ];

    [Test]
    public async Task TheProviderFixesTheRuleOfTheListenerAnalyzer()
    {
        await Assert.That(DroppedListenerCodeFixProvider.DiagnosticId).IsEqualTo(Diagnostics.DroppedListener.Id);

        await Assert.That(new DroppedListenerCodeFixProvider().FixableDiagnosticIds)
            .IsEquivalentTo([Diagnostics.DroppedListener.Id]);
    }

    [Test]
    public async Task AStatementGetsAField()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        clicks.Listen(static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? clicksListener;

                    public void Start(StreamSink<int> clicks)
                    {
                        clicksListener = clicks.Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task AStatementGetsAUsingDeclaration()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        // Listens.
                        clicks.Listen(static _ => { });
                        clicks.Send(1);
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.UsingKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        // Listens.
                        using var clicksListener = clicks.Listen(static _ => { });
                        clicks.Send(1);
                    }
                }
                """);
    }

    [Test]
    public async Task ADiscardGetsAFieldOrAUsingDeclaration()
    {
        const string source =
            """
            using SodaFlow;

            public sealed class Sample
            {
                public void Start(StreamSink<int> clicks)
                {
                    _ = clicks.ListenOnce(static _ => { });
                }
            }
            """;

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.FieldKey))
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? clicksListener;

                    public void Start(StreamSink<int> clicks)
                    {
                        clicksListener = clicks.ListenOnce(static _ => { });
                    }
                }
                """);

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.UsingKey))
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        using var clicksListener = clicks.ListenOnce(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task ADeclarationKeepsItsNameInTheFieldOrGetsTheUsingKeyword()
    {
        const string source =
            """
            using SodaFlow;

            public sealed class Sample
            {
                public void Start(StreamSink<int> clicks)
                {
                    var subscription = clicks.Listen(static _ => { });
                }
            }
            """;

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.FieldKey))
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? subscription;

                    public void Start(StreamSink<int> clicks)
                    {
                        subscription = clicks.Listen(static _ => { });
                    }
                }
                """);

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.UsingKey))
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        using var subscription = clicks.Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task ATypedDeclarationGivesTheFieldItsType()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        IDisposable held = clicks.Listen(static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    private IDisposable? held;

                    public void Start(StreamSink<int> clicks)
                    {
                        held = clicks.Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task AnAssignmentToALocalGetsAFieldAndLosesTheUnusedLocal()
    {
        const string source =
            """
            using SodaFlow;

            public sealed class Sample
            {
                public void Start(StreamSink<int> clicks)
                {
                    IListener listener;
                    listener = clicks.Listen(static _ => { });
                }
            }
            """;

        await Assert.That(await Offered(source)).IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey]);

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.FieldKey))
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? listener;

                    public void Start(StreamSink<int> clicks)
                    {
                        listener = clicks.Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task AnAssignmentToOneOfTwoLocalsRemovesThatLocalOnly()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        IListener first, second;
                        first = clicks.Listen(static _ => { });
                        second = clicks.Listen(static _ => { });
                        GC.KeepAlive(second);
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? first;

                    public void Start(StreamSink<int> clicks)
                    {
                        IListener second;
                        first = clicks.Listen(static _ => { });
                        second = clicks.Listen(static _ => { });
                        GC.KeepAlive(second);
                    }
                }
                """);
    }

    [Test]
    public async Task AnExpressionLambdaGetsAFieldOnly()
    {
        const string source =
            """
            using System;
            using SodaFlow;

            public sealed class Sample
            {
                public Action Start(StreamSink<int> clicks) => () => clicks.Listen(static _ => { });
            }
            """;

        await Assert.That(await Offered(source)).IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey]);

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.FieldKey))
            .IsEqualTo(
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? clicksListener;

                    public Action Start(StreamSink<int> clicks) => () => clicksListener = clicks.Listen(static _ => { });
                }
                """);
    }

    [Test]
    public async Task AConditionalAccessGetsAField()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int>? clicks)
                    {
                        clicks?.Listen(static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? clicksListener;

                    public void Start(StreamSink<int>? clicks)
                    {
                        clicksListener = clicks?.Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task TheFieldGoesAfterTheLastFieldWithAFreeName()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private readonly StreamSink<int> clicks;

                    public Sample(StreamSink<int> clicks) => this.clicks = clicks;

                    public int clicksListener => 0;

                    public void Start()
                    {
                        this.clicks.Map(static x => x).Listen(static _ => { });
                        StreamExtensionMethods.Listen(this.clicks, static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey,
                at: 1);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private readonly StreamSink<int> clicks;
                    private IListener? clicksListener2;

                    public Sample(StreamSink<int> clicks) => this.clicks = clicks;

                    public int clicksListener => 0;

                    public void Start()
                    {
                        this.clicks.Map(static x => x).Listen(static _ => { });
                        clicksListener2 = StreamExtensionMethods.Listen(this.clicks, static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task AChainGetsTheNameListener()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        clicks.Map(static x => x).Listen(static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.UsingKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        using var listener = clicks.Map(static x => x).Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task AFieldWithNoNullableAnnotationsHasNoQuestionMark()
    {
        string fixedText =
            await Fixed(
                source:
                """
                #nullable disable
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        clicks.Listen(_ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                #nullable disable
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener clicksListener;

                    public void Start(StreamSink<int> clicks)
                    {
                        clicksListener = clicks.Listen(_ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task AStaticMemberGetsNoField() =>
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public static class Sample
                    {
                        public static void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });
                        }
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.UsingKey]);

    [Test]
    public async Task AStaticLambdaGetsNoField() =>
        await Assert.That(
                await Offered(
                    """
                    using System;
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public Action<StreamSink<int>> Start() =>
                            static clicks =>
                            {
                                clicks.Listen(static _ => { });
                            };
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.UsingKey]);

    [Test]
    public async Task AStructGetsNoField() =>
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public struct Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });
                        }
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.UsingKey]);

    [Test]
    public async Task AStatementOutsideABlockGetsNoUsingDeclaration() =>
        await Assert.That(
                await Offered(
                    source:
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks, int mode)
                        {
                            if (mode == 0)
                                clicks.Listen(static _ => { });

                            switch (mode)
                            {
                                case 1:
                                    clicks.Listen(static _ => { });
                                    break;
                            }
                        }
                    }
                    """,
                    at: 0))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey]);

    [Test]
    public async Task ACaseOfASwitchGetsNoUsingDeclaration() =>
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks, int mode)
                        {
                            switch (mode)
                            {
                                case 1:
                                    clicks.Listen(static _ => { });
                                    break;
                            }
                        }
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey]);

    [Test]
    public async Task CSharp7GetsNoUsingDeclaration() =>
        await Assert.That(
                await Offered(
                    source:
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(_ => { });
                        }
                    }
                    """,
                    version: LanguageVersion.CSharp7_3))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey]);

    [Test]
    public async Task ARecordClassGetsAFieldAndARecordStructDoesNot()
    {
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public sealed record Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });
                        }
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey, DroppedListenerCodeFixProvider.UsingKey]);

        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public record struct Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });
                        }
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.UsingKey]);
    }

    [Test]
    public async Task AStaticLocalFunctionGetsNoField() =>
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            static void Go(StreamSink<int> s)
                            {
                                s.Listen(static _ => { });
                            }

                            Go(clicks);
                        }
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.UsingKey]);

    [Test]
    public async Task TheInitializerOfAFieldGetsNoField() =>
        await Assert.That(
                await Offered(
                    """
                    using System;
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public readonly Action<StreamSink<int>> Start = clicks =>
                        {
                            clicks.Listen(static _ => { });
                        };
                    }
                    """))
            .IsEquivalentTo([DroppedListenerCodeFixProvider.UsingKey]);

    [Test]
    public async Task TheNameOfTheFieldDropsThePrefixOfTheReceiver()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private readonly StreamSink<int> _clicks = Stream.CreateSink<int>();
                    private readonly StreamSink<int> m_taps = Stream.CreateSink<int>();

                    public void Start()
                    {
                        _clicks.Listen(static _ => { });
                        m_taps.Listen(static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using SodaFlow;

                public sealed class Sample
                {
                    private readonly StreamSink<int> _clicks = Stream.CreateSink<int>();
                    private readonly StreamSink<int> m_taps = Stream.CreateSink<int>();
                    private IListener? clicksListener;

                    public void Start()
                    {
                        clicksListener = _clicks.Listen(static _ => { });
                        m_taps.Listen(static _ => { });
                    }
                }
                """);

        await Assert.That(
                await Fixed(
                    source:
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        private readonly StreamSink<int> m_taps = Stream.CreateSink<int>();

                        public void Start()
                        {
                            m_taps.Listen(static _ => { });
                        }
                    }
                    """,
                    key: DroppedListenerCodeFixProvider.UsingKey))
            .Contains("using var tapsListener = m_taps.Listen(");
    }

    [Test]
    public async Task ALocalNamedUnderscoreGivesTheFieldTheNameOfTheReceiver() =>
        await Assert.That(
                await Fixed(
                    source:
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            var _ = clicks.Listen(static _ => { });
                        }
                    }
                    """,
                    key: DroppedListenerCodeFixProvider.FieldKey))
            .Contains("clicksListener = clicks.Listen(");

    [Test]
    public async Task TheNamesOfABaseClassAndOfAFieldAreTaken()
    {
        await Assert.That(
                await Fixed(
                    source:
                    """
                    using SodaFlow;

                    public class Base
                    {
                        protected int clicksListener => 0;
                    }

                    public sealed class Sample : Base
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });
                        }
                    }
                    """,
                    key: DroppedListenerCodeFixProvider.FieldKey))
            .Contains("clicksListener2 = clicks.Listen(");

        await Assert.That(
                await Fixed(
                    source:
                    """
                    using SodaFlow;

                    public class Base
                    {
                        protected int clicksListener => 0;
                    }

                    public sealed class Sample : Base
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });
                        }
                    }
                    """,
                    key: DroppedListenerCodeFixProvider.UsingKey))
            .Contains("using var clicksListener2 = clicks.Listen(");
    }

    [Test]
    public async Task ALocalOfATypeThatIsNotDisposableGetsNoUsingDeclaration()
    {
        const string source =
            """
            using SodaFlow;

            public sealed class Sample
            {
                public void Start(StreamSink<int> clicks)
                {
                    object held = clicks.Listen(static _ => { });
                }
            }
            """;

        await Assert.That(await Offered(source)).IsEquivalentTo([DroppedListenerCodeFixProvider.FieldKey]);

        await Assert.That(await Fixed(source: source, key: DroppedListenerCodeFixProvider.FieldKey))
            .Contains("private object? held;");
    }

    [Test]
    public async Task ALocalThatTheCodeUsesBeforeKeepsItsDeclaration()
    {
        string fixedText =
            await Fixed(
                source:
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    public void Start(StreamSink<int> clicks)
                    {
                        IListener? listener = null;
                        GC.KeepAlive(listener);
                        listener = clicks.Listen(static _ => { });
                    }
                }
                """,
                key: DroppedListenerCodeFixProvider.FieldKey);

        await Assert.That(fixedText)
            .IsEqualTo(
                """
                using System;
                using SodaFlow;

                public sealed class Sample
                {
                    private IListener? listener2;

                    public void Start(StreamSink<int> clicks)
                    {
                        IListener? listener = null;
                        GC.KeepAlive(listener);
                        listener2 = clicks.Listen(static _ => { });
                    }
                }
                """);
    }

    [Test]
    public async Task ADeclarationOfTwoLocalsGetsNeitherFix() =>
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            IListener first = clicks.Listen(static _ => { }), second = clicks.Listen(static _ => { });
                        }
                    }
                    """))
            .IsEmpty();

    [Test]
    public async Task AUsingLocalDoesNotTakeTheNameOfALaterLocal() =>
        await Assert.That(
                await Fixed(
                    source:
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            clicks.Listen(static _ => { });

                            if (clicks != null)
                            {
                                int clicksListener = 1;
                                clicks.Send(clicksListener);
                            }
                        }
                    }
                    """,
                    key: DroppedListenerCodeFixProvider.UsingKey))
            .Contains("using var clicksListener2 = clicks.Listen(");

    [Test]
    public async Task ADeclarationWhoseLocalIsSetAgainGetsNeitherFix() =>
        await Assert.That(
                await Offered(
                    """
                    using SodaFlow;

                    public sealed class Sample
                    {
                        public void Start(StreamSink<int> clicks)
                        {
                            IListener listener = clicks.Listen(static _ => { });
                            listener = clicks.Listen(static _ => { });
                            listener.Dispose();
                        }
                    }
                    """))
            .IsEmpty();

    /// <summary>
    ///     Each fix that the provider offers for a report in the listener shapes
    ///     compiles, and the rule does not report the fixed call.
    /// </summary>
    [Test]
    public async Task EachFixOfTheShapesCompilesAndClearsItsReport()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "ListenerShapes.cs"));
        Document document = Sample(source: source, version: LanguageVersion.Preview);
        IReadOnlyList<Diagnostic> reports = await Reports(document);
        int fixes = 0;
        List<string> failures = [];

        foreach (Diagnostic report in reports)
        {
            foreach (CodeAction action in await Actions(document: document, diagnostic: report))
            {
                try
                {
                    await Apply(document: document, action: action, reportsBefore: reports.Count);
                }
                catch (InvalidOperationException failure)
                {
                    failures.Add($"line {report.Location.GetLineSpan().StartLinePosition.Line + 1}: {failure.Message.Split('\n')[0]}");
                }

                fixes++;
            }
        }

        await Assert.That(failures).IsEmpty();

        await Assert.That(fixes).IsGreaterThan(0);
    }

    private static async Task<IReadOnlyList<string>> Offered(
        string source,
        LanguageVersion version = LanguageVersion.Preview,
        int at = 0)
    {
        Document document = Sample(source: source, version: version);
        IReadOnlyList<Diagnostic> reports = await Reports(document);

        return [.. (await Actions(document: document, diagnostic: reports[at])).Select(static action => action.EquivalenceKey ?? string.Empty)];
    }

    private static async Task<string> Fixed(string source, string key, int at = 0)
    {
        Document document = Sample(source: source, version: LanguageVersion.Preview);
        IReadOnlyList<Diagnostic> reports = await Reports(document);

        CodeAction action =
            (await Actions(document: document, diagnostic: reports[at])).FirstOrDefault(action => action.EquivalenceKey == key)
            ?? throw new InvalidOperationException($"The provider offers no fix {key}.");

        Document changed = await Apply(document: document, action: action, reportsBefore: reports.Count);

        return (await changed.GetTextAsync()).ToString().Replace(oldValue: "\r\n", newValue: "\n");
    }

    // A document of a workspace, with the references of the harness.
    private static Document Sample(string source, LanguageVersion version)
    {
        CSharpCompilation references =
            AnalyzerHarness.Compile(source: string.Empty, libraries: ShapeLibraries, assemblyName: "Sample");

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
                        version >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable),
                    parseOptions: new CSharpParseOptions(languageVersion: version),
                    metadataReferences: references.References));

        return workspace.AddDocument(projectId: project.Id, name: "Sample.cs", text: SourceText.From(source));
    }

    private static async Task<IReadOnlyList<Diagnostic>> Reports(TextDocument document)
    {
        Compilation compilation =
            await document.Project.GetCompilationAsync()
            ?? throw new InvalidOperationException("The sample has no compilation.");

        Diagnostic[] errors = [.. compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        return errors.Length > 0
            ? throw new InvalidOperationException($"The sample does not compile: {errors[0]}")
            : await AnalyzerHarness.Analyze(analyzer: new ListenerAnalyzer(), compilation: compilation);
    }

    private static async Task<IReadOnlyList<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        List<CodeAction> actions = [];

        await new DroppedListenerCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(
                document: document,
                diagnostic: diagnostic,
                registerCodeFix: (action, _) => actions.Add(action),
                cancellationToken: CancellationToken.None));

        return actions;
    }

    // Applies the fix, and checks the result. It compiles, with no warning that the sample did not
    // have, and the rule reports one call less.
    private static async Task<Document> Apply(TextDocument document, CodeAction action, int reportsBefore)
    {
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None);

        Document changed =
            operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The fix removes the document.");

        Compilation before =
            await document.Project.GetCompilationAsync()
            ?? throw new InvalidOperationException("The sample has no compilation.");

        Compilation after =
            await changed.Project.GetCompilationAsync()
            ?? throw new InvalidOperationException("The fixed sample has no compilation.");

        string[] warningsBefore = [.. Warnings(before)];
        string[] added = [.. Warnings(after).Where(warning => !warningsBefore.Contains(warning))];

        Diagnostic[] errors = [.. after.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        if (errors.Length > 0 || added.Length > 0)
        {
            throw new InvalidOperationException(
                $"The fix {action.EquivalenceKey} gives {errors.FirstOrDefault()?.ToString() ?? added[0]}:\n{await changed.GetTextAsync()}");
        }

        IReadOnlyList<Diagnostic> reportsAfter = await AnalyzerHarness.Analyze(analyzer: new ListenerAnalyzer(), compilation: after);

        return reportsAfter.Count != reportsBefore - 1
            ? throw new InvalidOperationException(
                $"The fix {action.EquivalenceKey} leaves {reportsAfter.Count} reports of {reportsBefore}:\n{await changed.GetTextAsync()}")
            : changed;
    }

    private static IEnumerable<string> Warnings(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.Id + " " + diagnostic.GetMessage());
}
