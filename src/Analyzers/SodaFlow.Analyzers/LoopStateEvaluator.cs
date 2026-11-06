using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Gives each expression in a loop region its <see cref="LoopState" />. The
///     rules are a model of what each library operator does with the current value
///     and the links of its inputs.
/// </summary>
internal sealed class LoopStateEvaluator
{
    private const int MaxInlineDepth = 1;

    private readonly ImmutableDictionary<ISymbol, LoopState> bound;
    private readonly int depth;
    private readonly Dictionary<ISymbol, LoopState> locals;
    private readonly Dictionary<ExpressionSyntax, LoopState> memo = [];
    private readonly LoopRegion region;

    public LoopStateEvaluator(LoopRegion region)
        : this(
            region: region,
            bound: ImmutableDictionary<ISymbol, LoopState>.Empty.WithComparers(SymbolEqualityComparer.Default),
            depth: 0)
    {
    }

    private LoopStateEvaluator(LoopRegion region, ImmutableDictionary<ISymbol, LoopState> bound, int depth)
    {
        this.region = region;
        this.bound = bound;
        this.depth = depth;
        this.locals = new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default);
    }

    /// <summary>Records the state of a local at its declaration or assignment.</summary>
    public void Declare(ISymbol local, LoopState state) =>
        this.locals[local] = this.locals.TryGetValue(local, out LoopState existing) ? existing.Join(state) : state;

    public LoopState Eval(ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return LoopState.None;
        }

        if (this.memo.TryGetValue(expression, out LoopState known))
        {
            return known;
        }

        // The evaluator can go in a cycle only through a reassignment, and none is the correct
        // answer for the first visit.
        this.memo[expression] = LoopState.None;
        LoopState state = this.EvalUncached(expression);
        this.memo[expression] = state;

        return state;
    }

    private LoopState EvalUncached(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax p:
                return this.Eval(p.Expression);
            case CastExpressionSyntax c:
                return this.Eval(c.Expression);
            case CheckedExpressionSyntax c:
                return this.Eval(c.Expression);
            case AwaitExpressionSyntax a:
                return this.Eval(a.Expression);
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } s:
                return this.Eval(s.Operand);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } b:
                return this.Eval(b.Left);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b:
                return this.Eval(b.Left).Join(this.Eval(b.Right));
            case ConditionalExpressionSyntax c:
                return this.Eval(c.WhenTrue).Join(this.Eval(c.WhenFalse));
            case SwitchExpressionSyntax s:
                return this.JoinAll(s.Arms.Select(static arm => arm.Expression));
            case TupleExpressionSyntax t:
                return this.JoinAll(t.Arguments.Select(static a => a.Expression));
            case CollectionExpressionSyntax c:
                return this.JoinAll(
                    c.Elements.Select(
                        static e =>
                            e switch
                            {
                                ExpressionElementSyntax x => x.Expression,
                                SpreadElementSyntax x => x.Expression,
                                _ => null
                            }));
            case ArrayCreationExpressionSyntax { Initializer: { } init }:
                return this.JoinAll(init.Expressions);
            case ImplicitArrayCreationExpressionSyntax a:
                return this.JoinAll(a.Initializer.Expressions);
            case InitializerExpressionSyntax i:
                return this.JoinAll(i.Expressions);
            case ElementAccessExpressionSyntax e:
                return this.Eval(e.Expression).Join(this.TypeState(expression));
            case AnonymousFunctionExpressionSyntax:
                return LoopState.None;
            case InvocationExpressionSyntax inv:
                return this.EvalInvocation(inv);
            case ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                return this.TypeState(expression);
            case IdentifierNameSyntax or MemberAccessExpressionSyntax or GenericNameSyntax:
                return this.EvalName(expression);
            default:
                return this.TypeState(expression);
        }
    }

    private LoopState EvalName(ExpressionSyntax expression)
    {
        SemanticModel model = this.region.ModelFor(expression);
        ISymbol? symbol = model.GetSymbolInfo(expression).Symbol;

        switch (symbol)
        {
            case ILocalSymbol or IParameterSymbol:
                if (SymbolEqualityComparer.Default.Equals(symbol, this.region.Placeholder))
                {
                    return this.region.PlaceholderState;
                }

                if (this.bound.TryGetValue(symbol, out LoopState boundState))
                {
                    return boundState;
                }

                return this.locals.TryGetValue(symbol, out LoopState localState) ? localState : LoopState.None;
            case IPropertySymbol or IFieldSymbol:
            {
                // A member of a pending value is pending: the cell behind a bindable, or one element
                // of a tuple from a loop block.
                LoopState fromOwner = LoopState.None;

                if (expression is MemberAccessExpressionSyntax access)
                {
                    LoopState owner = this.Eval(access.Expression);
                    ITypeSymbol? memberType = symbol is IPropertySymbol p ? p.Type : ((IFieldSymbol)symbol).Type;

                    if (SodaFlowSymbols.IsCarrierType(memberType)
                        || SodaFlowSymbols.IsStreamType(memberType)
                        || symbol.ContainingType.IsTupleType)
                    {
                        fromOwner = owner;
                    }
                }

                return fromOwner.Join(this.TypeState(expression));
            }
            default:
                return this.TypeState(expression);
        }
    }

    private LoopState EvalInvocation(InvocationExpressionSyntax invocation)
    {
        SemanticModel model = this.region.ModelFor(invocation);

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return this.TypeState(invocation);
        }

        ExpressionSyntax? receiver = ReceiverOf(invocation: invocation, method: method);
        LoopState receiverState = this.Eval(receiver);
        ITypeSymbol? receiverType = receiver == null ? null : model.GetTypeInfo(receiver).Type;
        bool runsNow = RunsNow(method);

        List<LoopState> argumentStates = [];
        List<ITypeSymbol?> argumentTypes = [];

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            argumentStates.Add(this.EvalArgument(argument.Expression, runsNow: runsNow));
            argumentTypes.Add(model.GetTypeInfo(argument.Expression).Type);
        }

        LoopState inputs = argumentStates.Aggregate(seed: receiverState, func: static (a, b) => a.Join(b));

        if (!SodaFlowSymbols.IsSodaFlow(method))
        {
            return this.EvalForeignCall(method: method, receiverState: receiverState, argumentStates: argumentStates);
        }

        LoopState firstArgument = argumentStates.Count > 0 ? argumentStates[0] : LoopState.None;
        LoopState source = receiver != null ? receiverState : firstArgument;

        switch (OperatorName(method))
        {
            case "Run" or "RunVoid" or "WithCaptures" or "WithoutCaptures":
                return firstArgument;
            case "Sample" or "Value" or "CanExecute":
                return LoopState.None;
            case "SampleLazy":
                return new LoopState(
                    Pending: receiverState.Pending,
                    Linked: false,
                    ContentPending: false,
                    ContentLinked: false);
            case "Map" when SodaFlowSymbols.IsLazySampleType(receiverType):
                return receiverState.WithLinked(false);
            case "Map" when SodaFlowSymbols.IsStreamType(receiverType):
                return new LoopState(
                    Pending: false,
                    Linked: receiverState.Linked,
                    ContentPending: false,
                    ContentLinked: false).Join(this.LambdaContent(invocation, receiverState.Content).AsContent);
            case "Map":
                return receiverState.Join(this.LambdaContent(invocation, receiverState.Content).AsContent);
            case "Lift" when SodaFlowSymbols.IsLazySampleType(receiverType):
                return inputs.WithLinked(false);
            case "Lift" or "Apply" or "Calm" or "AsCell" or "AsBehavior" or "AsStream"
                when !SodaFlowSymbols.IsStreamType(receiverType):
                return new LoopState(
                    Pending: inputs.Pending,
                    Linked: inputs.Linked,
                    ContentPending: false,
                    ContentLinked: false);
            case "SwitchC" or "SwitchB":
                return new LoopState(
                    Pending: receiverState.Pending || receiverState.ContentPending,
                    Linked: receiverState.Linked || receiverState.ContentLinked,
                    ContentPending: false,
                    ContentLinked: false);
            case "SwitchS":
                return new LoopState(
                    Pending: false,
                    Linked: receiverState.ContentLinked,
                    ContentPending: false,
                    ContentLinked: false);
            case "Hold":
                return new LoopState(
                    Pending: false,
                    Linked: receiverState.Linked,
                    ContentPending: receiverState.ContentPending || firstArgument.Pending,
                    ContentLinked: receiverState.ContentLinked || firstArgument.Linked);
            case "HoldLazy" or "AccumLazy" or "CollectLazy":
                return new LoopState(
                    Pending: firstArgument.Pending,
                    Linked: receiverState.Linked,
                    ContentPending: false,
                    ContentLinked: false);
            case "ConstantLazy":
                return firstArgument.WithLinked(false);
            case "Constant":
                return firstArgument.AsContent;
            case "Accum" or "Collect":
                return new LoopState(
                    Pending: false,
                    Linked: receiverState.Linked,
                    ContentPending: false,
                    ContentLinked: false);
            case "Updates" or "Values":
                return source.WithPending(false);
            case "MapTo":
                return new LoopState(
                    Pending: false,
                    Linked: receiverState.Linked,
                    ContentPending: firstArgument.Pending,
                    ContentLinked: firstArgument.Linked);
            case "Snapshot" or "SnapshotLatest" or "Gate" or "Filter" or "FilterSome" or "Once" or "Calm":
                // A snapshot samples its cells at each firing. Only the stream has a link.
                return new LoopState(
                    Pending: false,
                    Linked: receiverState.Linked,
                    ContentPending: receiverState.ContentPending,
                    ContentLinked: receiverState.ContentLinked);
            case "Merge" or "OrElse":
                return new LoopState(
                    Pending: false,
                    Linked: LinkedStreamInput(
                        receiverState: receiverState,
                        receiverType: receiverType,
                        argumentStates: argumentStates,
                        argumentTypes: argumentTypes),
                    ContentPending: inputs.ContentPending,
                    ContentLinked: inputs.ContentLinked);
            case "Defer" or "Split" or "At" or "Debounce" or "Post" or "Listen" or "ListenStrong" or "ListenOnce"
                or "ListenOnceStrong" or "ListenOnceAsync" or "Send" or "Unlisten" or "Loop":
                // A defer, a split, a timer, and a debounce fire in a new transaction, which breaks
                // the link.
                return LoopState.None;
            default:
                return DefaultLibraryResult(method: method, inputs: inputs);
        }
    }

    private LoopState EvalForeignCall(IMethodSymbol method, LoopState receiverState, List<LoopState> argumentStates)
    {
        LoopState? inlined = this.TryInline(method: method, receiverState: receiverState, argumentStates: argumentStates);

        if (inlined is { } known)
        {
            return known;
        }

        LoopState inputs = argumentStates.Aggregate(seed: receiverState, func: static (a, b) => a.Join(b));

        return DefaultLibraryResult(method: method, inputs: inputs).Join(SodaFlowSymbols.StateOfType(method.ReturnType));
    }

    /// <summary>
    ///     A method with no rule here gives its result the state of its inputs, where
    ///     the result type can hold one. This is the conservative default.
    /// </summary>
    private static LoopState DefaultLibraryResult(IMethodSymbol method, LoopState inputs)
    {
        ITypeSymbol returnType = method.ReturnType;

        if (SodaFlowSymbols.IsStreamType(returnType))
        {
            return new LoopState(
                Pending: false,
                Linked: inputs.Linked,
                ContentPending: inputs.ContentPending,
                ContentLinked: inputs.ContentLinked);
        }

        if (SodaFlowSymbols.IsCarrierType(returnType))
        {
            return inputs;
        }

        return LoopState.None;
    }

    /// <summary>
    ///     One level into a method in this compilation whose body is one expression:
    ///     the result is that expression with the parameters bound to the arguments.
    /// </summary>
    private LoopState? TryInline(IMethodSymbol method, LoopState receiverState, List<LoopState> argumentStates)
    {
        if (this.depth >= MaxInlineDepth)
        {
            return null;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        SyntaxNode? declaration = definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        ExpressionSyntax? body =
            declaration switch
            {
                MethodDeclarationSyntax m => m.ExpressionBody?.Expression ?? SingleReturn(m.Body),
                LocalFunctionStatementSyntax f => f.ExpressionBody?.Expression ?? SingleReturn(f.Body),
                _ => null
            };

        if (body == null)
        {
            return null;
        }

        ImmutableDictionary<ISymbol, LoopState>.Builder bound = this.bound.ToBuilder();
        int offset = 0;

        if (method.ReducedFrom != null && definition.Parameters.Length > 0)
        {
            bound[definition.Parameters[0]] = receiverState;
            offset = 1;
        }

        for (int i = 0; i < argumentStates.Count && i + offset < definition.Parameters.Length; i++)
        {
            bound[definition.Parameters[i + offset]] = argumentStates[i];
        }

        LoopStateEvaluator inner = new(region: this.region, bound: bound.ToImmutable(), depth: this.depth + 1);

        return inner.Eval(body);
    }

    private static ExpressionSyntax? SingleReturn(BlockSyntax? block) =>
        block is { Statements.Count: 1 } && block.Statements[0] is ReturnStatementSyntax { Expression: { } result }
            ? result
            : null;

    private LoopState EvalArgument(ExpressionSyntax argument, bool runsNow)
    {
        switch (argument)
        {
            case AnonymousFunctionExpressionSyntax lambda:
                return runsNow ? this.EvalLambdaResult(lambda, boundParameters: []) : LoopState.None;
            case MemberAccessExpressionSyntax access
                when SodaFlowSymbols.ReadMembers.Contains(access.Name.Identifier.ValueText)
                && this.region.ModelFor(access).GetSymbolInfo(access).Symbol is IMethodSymbol:
                // A method group such as x.Sample is a read that runs after this. It is pending, as
                // a lazy sample of x is.
                return this.Eval(access.Expression).WithLinked(false);
            default:
                return this.Eval(argument);
        }
    }

    /// <summary>
    ///     The state of what a lambda given to an operator returns, for the content
    ///     of a cell of cells or a cell of streams.
    /// </summary>
    private LoopState LambdaContent(InvocationExpressionSyntax invocation, LoopState parameterState)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        if (lambda == null)
        {
            return LoopState.None;
        }

        return this.EvalLambdaResult(lambda, boundParameters: [parameterState]);
    }

    public LoopState EvalLambdaResult(
        AnonymousFunctionExpressionSyntax lambda,
        ImmutableArray<LoopState> boundParameters)
    {
        SemanticModel model = this.region.ModelFor(lambda);
        ImmutableDictionary<ISymbol, LoopState>.Builder bound = this.bound.ToBuilder();

        IReadOnlyList<ParameterSyntax> parameters =
            lambda switch
            {
                SimpleLambdaExpressionSyntax s => [s.Parameter],
                ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters,
                AnonymousMethodExpressionSyntax { ParameterList: { } list } => list.Parameters,
                _ => []
            };

        for (int i = 0; i < parameters.Count && i < boundParameters.Length; i++)
        {
            if (model.GetDeclaredSymbol(parameters[i]) is { } symbol)
            {
                bound[symbol] = boundParameters[i];
            }
        }

        LoopStateEvaluator inner =
            boundParameters.IsEmpty ? this : new LoopStateEvaluator(region: this.region, bound: bound.ToImmutable(), depth: this.depth);

        if (lambda.ExpressionBody != null)
        {
            return inner.Eval(lambda.ExpressionBody);
        }

        if (lambda.Block == null)
        {
            return LoopState.None;
        }

        // The locals of the block, in order, so that a return of one of them has a state.
        foreach (LocalDeclarationStatementSyntax declaration in lambda.Block.Statements
                     .OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (VariableDeclaratorSyntax declarator in declaration.Declaration.Variables)
            {
                if (declarator.Initializer != null && model.GetDeclaredSymbol(declarator) is { } local)
                {
                    inner.Declare(local, inner.Eval(declarator.Initializer.Value));
                }
            }
        }

        return inner.JoinAll(ReturnExpressions(lambda.Block));
    }

    /// <summary>
    ///     The expressions that a block returns, without those of a nested function.
    /// </summary>
    public static IEnumerable<ExpressionSyntax> ReturnExpressions(SyntaxNode body) =>
        body.DescendantNodes(descendIntoChildren: static n => n is not (AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax))
            .OfType<ReturnStatementSyntax>()
            .Select(static r => r.Expression)
            .Where(static e => e != null)!;

    private LoopState JoinAll(IEnumerable<ExpressionSyntax?> expressions) =>
        expressions.Aggregate(seed: LoopState.None, func: (state, e) => state.Join(this.Eval(e)));

    private LoopState TypeState(ExpressionSyntax expression) =>
        SodaFlowSymbols.StateOfType(this.region.ModelFor(expression).GetTypeInfo(expression).Type);

    private static bool LinkedStreamInput(
        LoopState receiverState,
        ITypeSymbol? receiverType,
        List<LoopState> argumentStates,
        List<ITypeSymbol?> argumentTypes)
    {
        bool linked = receiverState.Linked && (receiverType == null || !SodaFlowSymbols.IsCellOrBehaviorType(receiverType));

        for (int i = 0; i < argumentStates.Count; i++)
        {
            linked |= argumentStates[i].Linked && !SodaFlowSymbols.IsCellOrBehaviorType(argumentTypes[i]);
        }

        return linked;
    }

    /// <summary>
    ///     The code of a lambda given to one of these runs in the block, before the
    ///     loop closes. Every other operator keeps its lambda for a firing after the close.
    /// </summary>
    public static bool RunsNow(IMethodSymbol method) =>
        SodaFlowSymbols.IsSodaFlow(method)
        && method.Name is "Run" or "RunVoid" or "WithCaptures" or "WithoutCaptures";

    /// <summary>
    ///     The name of the operator, without the Impl or Internal suffix of the
    ///     library's own internal entry points.
    /// </summary>
    public static string OperatorName(IMethodSymbol method)
    {
        string name = method.Name;

        foreach (string suffix in Suffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - suffix.Length);
            }
        }

        return name;
    }

    private static readonly ImmutableArray<string> Suffixes = ["Impl", "Internal"];

    public static ExpressionSyntax? ReceiverOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return null;
        }

        return method.ReducedFrom != null || !method.IsStatic ? access.Expression : null;
    }
}
