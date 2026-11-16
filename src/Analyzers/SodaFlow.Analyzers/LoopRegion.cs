using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
    in Action<Diagnostic> report,
    in CancellationToken cancellation)
{
    // The walk reads at most this many callee bodies for one block. A block that needs more is
    // code that calls itself through many paths, and the limit is what ends the work.
    private const int MaxFrames = 512;

    private readonly CancellationToken cancellation = cancellation;
    private readonly Compilation compilation = compilation;

    // The fields that the code sets while the loop is open, where the walk does not know the
    // object. Such a field is a static field, or a field of an object that the block did not
    // make.
    private readonly Dictionary<ISymbol, LoopState> fields = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<SyntaxTree, SemanticModel> models = [];
    private readonly Dictionary<ISymbol, ExpressionSyntax?> initializerOf = new(SymbolEqualityComparer.Default);
    private readonly Action<Diagnostic> report = report;
    private readonly HashSet<(string Id, Location Location)> reported = [];

    // The walk and the evaluator get the symbol and the type of one node many times. The compiler
    // computes them again at each call, thus the region keeps them.
    private readonly Dictionary<ExpressionSyntax, IMethodSymbol?> conversionOf = [];
    private readonly Dictionary<SyntaxNode, ISymbol?> symbolOf = [];
    private readonly Dictionary<ExpressionSyntax, ITypeSymbol?> typeOf = [];

    // The compiler makes a new array of the declarations of a local each time. The walk takes a loop
    // shape before and after each function that runs for each element, and each shape tests each local.
    private readonly Dictionary<ISymbol, ImmutableArray<SyntaxReference>> declarationsOf =
        new(SymbolEqualityComparer.Default);

    private int frames;
    private int objectFrames;

    // The members of the object of a forward reference that the code samples at the end of the
    // transaction, before a switch links to them.
    private readonly HashSet<ISymbol> forced = new(SymbolEqualityComparer.Default);

    // The lambdas and the local functions that the walk reads for a call now.
    private readonly HashSet<SyntaxNode> activeFunctions = [];

    // The key is a lambda or a local function that the walk read for a call. The value has the
    // locals and the parameters that a function in it reads or writes.
    private readonly Dictionary<SyntaxNode, ImmutableHashSet<ISymbol>?> captured = [];

    /// <summary>The one state of each object that the code of the region makes.</summary>
    public ObjectStore Objects { get; } = new();

    /// <summary>The body of the block.</summary>
    public SyntaxNode Root { get; } = root;

    /// <summary>The assemblies of the library, as this compilation sees them.</summary>
    public SodaFlowSymbols Symbols { get; } = symbols;

    public ISymbol Placeholder { get; } = placeholder;

    public LoopState PlaceholderState { get; } = placeholderState;

    /// <summary>
    ///     True for the block of a forward reference, whose placeholder holds the
    ///     object that the block returns.
    /// </summary>
    public bool ReadsBack => !this.PlaceholderState.Content.ReadBack.IsEmpty;

    /// <summary>
    ///     Records the members of the object of a forward reference that the code
    ///     samples at the end of the transaction.
    /// </summary>
    public void Forces(ImmutableArray<ISymbol> members) => this.forced.UnionWith(members);

    public string PlaceholderName => this.Placeholder.Name;

    /// <summary>The assembly that the block is in.</summary>
    public IAssemblySymbol Assembly => this.compilation.Assembly;

    /// <summary>True when the code set a field to a value with a state.</summary>
    public bool HasFields => this.fields.Count > 0;

    /// <summary>A number for what the fields say, from <see cref="LoopState.Shape" />.</summary>
    public int ShapeOfFields =>
        this.fields.Aggregate(
            seed: 0,
            func: static (sum, one) => unchecked(
                sum + ShapeNumber.Of(location: ShapeNumber.Of(one.Key.Name), state: one.Value.Shape)));

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
        // A state that only holds objects of the store says what the owner of the field holds.
        if (state is { IsNone: true, HoldsAnObject: false })
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

    /// <summary>
    ///     The initializer of a field or of a property, when no code can give the
    ///     member a different value after it. Null for each other member.
    /// </summary>
    /// <remarks>
    ///     The member is a field with readonly, or a property with only a getter
    ///     that has no body. Only a constructor of the type can give such a
    ///     member a value. Thus, a constructor that has the name of the member in
    ///     its code can give it a different value.
    /// </remarks>
    public ExpressionSyntax? InitializerOf(ISymbol member)
    {
        if (!this.initializerOf.TryGetValue(key: member, value: out ExpressionSyntax? found))
        {
            found = FindTheInitializerOf(member);
            this.initializerOf[member] = found;
        }

        return found;
    }

    private static ExpressionSyntax? FindTheInitializerOf(ISymbol member)
    {
        ExpressionSyntax? value =
            member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                VariableDeclaratorSyntax { Initializer.Value: { } ofAField } => ofAField,
                PropertyDeclarationSyntax { Initializer.Value: { } ofAProperty, AccessorList: { } accessors }
                    when accessors.Accessors.All(static accessor =>
                        accessor is { Body: null, ExpressionBody: null }) => ofAProperty,
                _ => null
            };

        if (value == null)
        {
            return null;
        }

        bool aConstructorNamesIt =
            member.ContainingType.DeclaringSyntaxReferences
                .SelectMany(static declared => declared.GetSyntax().ChildNodes())
                .OfType<ConstructorDeclarationSyntax>()
                .Where(constructor => constructor.Modifiers.Any(SyntaxKind.StaticKeyword) == member.IsStatic)
                .Any(constructor => constructor.DescendantTokens()
                    .Any(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == member.Name));

        return aConstructorNamesIt ? null : value;
    }

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
    ///     True for a local that the given code declares. The region keeps the
    ///     declarations of each local.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="code">The body of a loop, or a function.</param>
    public bool IsDeclaredIn(ISymbol local, SyntaxNode code)
    {
        if (!this.declarationsOf.TryGetValue(key: local, value: out ImmutableArray<SyntaxReference> declarations))
        {
            declarations = local.DeclaringSyntaxReferences;
            this.declarationsOf[local] = declarations;
        }

        return LoopStateEvaluator.IsDeclaredIn(declarations: declarations, code: code);
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
    ///     The operator of the code that converts a value of the type to bool
    ///     with no cast. Null when no such operator makes that conversion.
    /// </summary>
    public IMethodSymbol? ConversionToBoolean(ITypeSymbol from) =>
        this.compilation.ClassifyConversion(
                source: from,
                destination: this.compilation.GetSpecialType(SpecialType.System_Boolean))
            is { IsImplicit: true, IsUserDefined: true, MethodSymbol: { } method }
            ? method
            : null;

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
    ///     Ends the analysis of the block when the compiler canceled it. An editor
    ///     cancels an analysis at each change to the file. The compiler stops
    ///     between two blocks, and only this call stops the walk of one block.
    /// </summary>
    public void StopWhenCanceled() => this.cancellation.ThrowIfCancellationRequested();

    /// <summary>
    ///     The number of writes to a member of an object that the walk read in
    ///     this region. A member can have a new value after each of them.
    /// </summary>
    public int MemberWrites { get; private set; }

    /// <summary>Records a write to a member of an object.</summary>
    public void AddAMemberWrite() => this.MemberWrites++;

    /// <summary>
    ///     Counts one more callee body that the walk reads only because the callee
    ///     gets an object of the store. No state from the loop goes into such a
    ///     callee. These bodies have their own limit, thus they do not use the
    ///     frames that a pending value needs.
    /// </summary>
    public bool TryEnterForObjects() => ++this.objectFrames <= MaxFrames;

    /// <summary>
    ///     Records the start of the read of a lambda or a local function for a
    ///     call. False when the walk is in a read of that function now, which is
    ///     a call of the function in its own code.
    /// </summary>
    public bool StartsACallOf(SyntaxNode function) => this.activeFunctions.Add(function);

    /// <summary>Records the end of the read that <see cref="StartsACallOf" /> started.</summary>
    public void EndsACallOf(SyntaxNode function) => this.activeFunctions.Remove(function);

    /// <summary>
    ///     The locals and the parameters that a function in the given function
    ///     reads or writes. Null when the compiler cannot tell.
    /// </summary>
    public ImmutableHashSet<ISymbol>? CapturedIn(SyntaxNode function)
    {
        if (!this.captured.TryGetValue(key: function, value: out ImmutableHashSet<ISymbol>? found))
        {
            SyntaxNode? body =
                function switch
                {
                    AnonymousFunctionExpressionSyntax lambda => lambda.Body,
                    LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                    _ => null
                };

            DataFlowAnalysis? flow =
                body switch
                {
                    StatementSyntax statement => this.ModelFor(function).AnalyzeDataFlow(statement),
                    ExpressionSyntax expression => this.ModelFor(function).AnalyzeDataFlow(expression),
                    _ => null
                };

            found = flow is { Succeeded: true }
                ? flow.Captured.ToImmutableHashSet(SymbolEqualityComparer.Default)
                : null;

            this.captured[function] = found;
        }

        return found;
    }

    /// <summary>
    ///     Walks the region for reads that run before the close, then checks each
    ///     expression that closes the loop for a link back to the placeholder.
    /// </summary>
    public void Analyze(IEnumerable<ExpressionSyntax> closings)
    {
        LoopStateEvaluator evaluator = new(this);

        evaluator.ReadBy(
                new SinkWalker(region: this, evaluator: evaluator, sink: finding => this.Report(finding.Diagnostic)))
            .Visit(this.Root);

        foreach (Finding unread in evaluator.Frame.TakeUnread())
        {
            this.Report(unread.Diagnostic);
        }

        foreach (ExpressionSyntax closing in closings)
        {
            LoopState state = evaluator.Eval(closing);

            if (this.ReadsBack)
            {
                this.CheckReadBack(closing: closing, state: state);
            }
            else if (state.Linked)
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

    /// <summary>
    ///     Checks the value that closes a forward reference, and each of its
    ///     members. The close gives each of them the value that the code reads
    ///     back through the reference. A value that reads back itself gets its
    ///     links or its initial value from itself.
    /// </summary>
    /// <remarks>
    ///     A cycle through two members or more needs a switch for each. The first
    ///     switch links to a value that has no link back yet, and then samples it.
    ///     A sample that is pending forces itself before the second link. Thus,
    ///     such a cycle is a link only when its values are not pending.
    /// </remarks>
    private void CheckReadBack(ExpressionSyntax closing, LoopState state)
    {
        Dictionary<ISymbol, LoopState> parts =
            new(SymbolEqualityComparer.Default) { [this.PlaceholderState.Content.ReadBack[0]] = state };

        foreach (KeyValuePair<ISymbol, LoopState> member in state.Members?.Members ?? [])
        {
            parts[member.Key] = member.Value;
        }

        foreach (KeyValuePair<ISymbol, LoopState> part in parts)
        {
            LoopState value = part.Value;

            if (value is { Linked: false, Pending: false } || !ReachesItself(part.Key))
            {
                continue;
            }

            // A sample at the end of the transaction forces the value before the switch links to it.
            bool linked =
                value.ReadBack.Contains(value: part.Key, comparer: SymbolEqualityComparer.Default)
                    ? value.Linked && !(value.Pending && this.forced.Contains(part.Key))
                    : !value.Pending;

            this.Report(
                Diagnostic.Create(
                    descriptor: linked ? Diagnostics.LinkedClose : Diagnostics.PendingClose,
                    location: closing.GetLocation(),
                    this.PlaceholderName));
        }

        bool ReachesItself(ISymbol start)
        {
            HashSet<ISymbol> seen = new(SymbolEqualityComparer.Default);
            Stack<ISymbol> next = new([start]);

            while (next.Count > 0)
            {
                if (!parts.TryGetValue(key: next.Pop(), value: out LoopState? from)
                    || from is { Linked: false, Pending: false })
                {
                    continue;
                }

                foreach (ISymbol member in from.ReadBack)
                {
                    if (SymbolEqualityComparer.Default.Equals(x: member, y: start))
                    {
                        return true;
                    }

                    if (seen.Add(member))
                    {
                        next.Push(member);
                    }
                }
            }

            return false;
        }
    }
}
