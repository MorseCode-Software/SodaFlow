using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The code that runs while one loop is open, which is the body of a loop
///     block.
/// </summary>
internal sealed class LoopRegion(
    in Compilation compilation,
    in SodaFlowSymbols symbols,
    in ISymbol placeholder,
    in LoopState placeholderState,
    in SyntaxNode root,
    in Action<Diagnostic> report)
{
    // The walk reads at most this many callee bodies for one block. A block that needs more is
    // code that calls itself through many paths, and the limit is what ends the work.
    private const int MaxFrames = 512;

    private readonly Compilation compilation = compilation;

    // The fields that the code sets while the loop is open, where the walk does not know the
    // object. Such a field is a static field, or a field of an object that the block did not
    // make.
    private readonly Dictionary<ISymbol, LoopState> fields = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<SyntaxTree, SemanticModel> models = [];
    private readonly Action<Diagnostic> report = report;
    private readonly HashSet<(string Id, Location Location)> reported = [];

    // The walk and the evaluator get the symbol and the type of one node many times. The compiler
    // computes them again at each call, thus the region keeps them.
    private readonly Dictionary<ExpressionSyntax, IMethodSymbol?> conversionOf = [];
    private readonly Dictionary<SyntaxNode, ISymbol?> symbolOf = [];
    private readonly Dictionary<ExpressionSyntax, ITypeSymbol?> typeOf = [];
    private int frames;

    /// <summary>The one state of each object that the code of the region makes.</summary>
    public ObjectStore Objects { get; } = new();

    /// <summary>The body of the block.</summary>
    public SyntaxNode Root { get; } = root;

    /// <summary>The assemblies of the library, as this compilation sees them.</summary>
    public SodaFlowSymbols Symbols { get; } = symbols;

    public ISymbol Placeholder { get; } = placeholder;

    public LoopState PlaceholderState { get; } = placeholderState;

    public string PlaceholderName => this.Placeholder.Name;

    /// <summary>The assembly that the block is in.</summary>
    public IAssemblySymbol Assembly => this.compilation.Assembly;

    /// <summary>True when the code set a field to a value with a state.</summary>
    public bool HasFields => this.fields.Count > 0;

    /// <summary>
    ///     The state of a field that the code set while the loop is open. The
    ///     field is the origin of that state, in each frame that reads it.
    /// </summary>
    public LoopState FieldState(ISymbol field) =>
        this.fields.TryGetValue(key: field.OriginalDefinition, value: out LoopState? state)
            ? state.From(field.OriginalDefinition)
            : LoopState.None;

    /// <summary>
    ///     Records a value that the code gives to a field. The walk does not know
    ///     which object has the field, thus each object of the type has that state
    ///     to the end of the block.
    /// </summary>
    public void SetField(ISymbol field, LoopState state)
    {
        if (state.IsNone)
        {
            return;
        }

        ISymbol key = field.OriginalDefinition;

        this.fields[key] =
            this.fields.TryGetValue(key: key, value: out LoopState? existing) ? existing.Join(state) : state;
    }

    private SemanticModel ModelFor(SyntaxTree tree)
    {
        if (!this.models.TryGetValue(key: tree, value: out SemanticModel? model))
        {
            model = this.compilation.GetSemanticModel(tree);
            this.models[tree] = model;
        }

        return model;
    }

    public SemanticModel ModelFor(SyntaxNode node) => this.ModelFor(node.SyntaxTree);

    /// <summary>The symbol that a node names, or null when it names none.</summary>
    public ISymbol? SymbolOf(SyntaxNode node)
    {
        if (!this.symbolOf.TryGetValue(key: node, value: out ISymbol? symbol))
        {
            symbol = this.ModelFor(node).GetSymbolInfo(node).Symbol;
            this.symbolOf[node] = symbol;
        }

        return symbol;
    }

    /// <summary>
    ///     The operator that makes the conversion which the compiler adds to an
    ///     expression. Null when it adds none, or one that no operator makes.
    /// </summary>
    public IMethodSymbol? ConversionOf(ExpressionSyntax expression)
    {
        if (!this.conversionOf.TryGetValue(key: expression, value: out IMethodSymbol? method))
        {
            Conversion conversion = this.ModelFor(expression).GetConversion(expression);
            method = conversion.IsUserDefined ? conversion.MethodSymbol : null;
            this.conversionOf[expression] = method;
        }

        return method;
    }

    /// <summary>
    ///     True when the compiler converts the first type to the second one with
    ///     no cast. No operator converts an interface. Thus, each value of a first
    ///     type that is an interface is then a value of the second type.
    /// </summary>
    public bool Converts(ITypeSymbol from, ITypeSymbol to) =>
        this.compilation.ClassifyConversion(source: from, destination: to).IsImplicit;

    /// <summary>
    ///     The type of an expression. A collection expression has no type of its
    ///     own, and has the type that the compiler converts it to. A conditional
    ///     or a switch expression whose results are such expressions is the same.
    /// </summary>
    public ITypeSymbol? TypeOf(ExpressionSyntax expression)
    {
        if (!this.typeOf.TryGetValue(key: expression, value: out ITypeSymbol? type))
        {
            TypeInfo info = this.ModelFor(expression).GetTypeInfo(expression);

            type =
                info.Type == null
                && expression
                    is CollectionExpressionSyntax
                    or ConditionalExpressionSyntax
                    or SwitchExpressionSyntax
                    or ParenthesizedExpressionSyntax
                    ? info.ConvertedType
                    : info.Type;

            this.typeOf[expression] = type;
        }

        return type;
    }

    /// <summary>
    ///     Reports a diagnostic one time. The walk can read the same lambda or
    ///     local function from more than one call.
    /// </summary>
    private void Report(Diagnostic diagnostic)
    {
        if (this.reported.Add((diagnostic.Id, diagnostic.Location)))
        {
            this.report(diagnostic);
        }
    }

    /// <summary>
    ///     Counts one more callee body that the walk reads. False when the block
    ///     is at its limit, and the callee is then code that the analyzer does not
    ///     read.
    /// </summary>
    public bool TryEnter() => ++this.frames <= MaxFrames;

    /// <summary>
    ///     Walks the region for reads that run before the close, then checks each
    ///     expression that closes the loop for a link back to the placeholder.
    /// </summary>
    public void Analyze(IEnumerable<ExpressionSyntax> closings, bool checkLinks)
    {
        LoopStateEvaluator evaluator = new(this);

        new SinkWalker(region: this, evaluator: evaluator, sink: finding => this.Report(finding.Diagnostic))
            .Visit(this.Root);

        foreach (Finding unread in evaluator.Frame.TakeUnread())
        {
            this.Report(unread.Diagnostic);
        }

        if (!checkLinks)
        {
            return;
        }

        foreach (ExpressionSyntax closing in closings)
        {
            LoopState state = evaluator.Eval(closing);

            if (state.Linked)
            {
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.LinkedClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
            else if (state.Pending)
            {
                // A pending close with no link is a lazy sample of the loop in its own initial
                // value. That sample forces itself at the end of the transaction.
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.PendingClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
        }
    }
}
