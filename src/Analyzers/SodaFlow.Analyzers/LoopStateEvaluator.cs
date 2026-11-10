using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Gives each expression in a loop region its <see cref="LoopState" />.
///     The rules are a model of what each library operator does with the current
///     value and the links of its inputs.
/// </summary>
internal sealed class LoopStateEvaluator
{
    /// <summary>
    ///     How many calls deep the evaluator follows a helper or a construction.
    ///     Three is sufficient for a helper that calls a helper that calls one more.
    ///     The limit is also what ends a recursive chain.
    /// </summary>
    public const int MaxInlineDepth = 3;

    private static readonly ImmutableArray<string> Suffixes = ImmutableArray.Create(item1: "Impl", item2: "Internal");

    private readonly ImmutableDictionary<ISymbol, LoopState> bound;
    private readonly int depth;
    private readonly Dictionary<InvocationExpressionSyntax, ImmutableHashSet<ISymbol>> followedByCall = [];
    private readonly Dictionary<ISymbol, LoopState> locals;
    private readonly Dictionary<ExpressionSyntax, LoopState> memo = [];
    private readonly LoopRegion region;
    private readonly Dictionary<ISymbol, LoopState> thisMembers;

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
        this.thisMembers = new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default);
    }

    /// <summary>Records the state of a local at its declaration or assignment.</summary>
    public void Declare(ISymbol local, LoopState state) =>
        this.locals[local] =
            this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing.Join(state) : state;

    /// <summary>
    ///     The members of the object that an expression names, when its
    ///     construction was followed. The state carries them through a local, a hold,
    ///     or a map.
    /// </summary>
    public Construction? ConstructionOf(ExpressionSyntax expression) => this.Eval(expression).Members;

    /// <summary>
    ///     The parameters of an inlined call with all uses in the expressions
    ///     that the evaluator read. The argument went only where the result shows it.
    /// </summary>
    public ImmutableHashSet<ISymbol>? FollowedParametersOf(InvocationExpressionSyntax invocation)
    {
        this.Eval(invocation);

        return this.followedByCall.TryGetValue(key: invocation, value: out ImmutableHashSet<ISymbol>? followed)
            ? followed
            : null;
    }

    public LoopState Eval(ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return LoopState.None;
        }

        if (this.memo.TryGetValue(key: expression, value: out LoopState? known))
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

    private LoopState EvalUncached(ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax p => this.Eval(p.Expression),
            CastExpressionSyntax c => this.Eval(c.Expression),
            CheckedExpressionSyntax c => this.Eval(c.Expression),
            AwaitExpressionSyntax a => this.Eval(a.Expression),
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } s =>
                this.Eval(s.Operand),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } b => this.Eval(b.Left),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b =>
                this.Eval(b.Left).Join(this.Eval(b.Right)),
            ConditionalExpressionSyntax c => this.Eval(c.WhenTrue).Join(this.Eval(c.WhenFalse)),
            SwitchExpressionSyntax s => this.JoinAll(s.Arms.Select(static arm => arm.Expression)),
            TupleExpressionSyntax t => this.JoinAll(t.Arguments.Select(static a => a.Expression)),
            CollectionExpressionSyntax c => this.JoinAll(
                c.Elements.Select(static e =>
                    e switch
                    {
                        ExpressionElementSyntax x => x.Expression,
                        SpreadElementSyntax x => x.Expression,
                        _ => null
                    })),
            ArrayCreationExpressionSyntax { Initializer: { } init } => this.JoinAll(init.Expressions),
            ImplicitArrayCreationExpressionSyntax a => this.JoinAll(a.Initializer.Expressions),
            InitializerExpressionSyntax i => this.JoinAll(i.Expressions),
            ElementAccessExpressionSyntax e => this.Eval(e.Expression).Join(this.TypeState(expression)),
            AnonymousFunctionExpressionSyntax => LoopState.None,
            InvocationExpressionSyntax inv => this.EvalInvocation(inv),
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => this.EvalCreation(expression),
            IdentifierNameSyntax or MemberAccessExpressionSyntax or GenericNameSyntax => this.EvalName(expression),
            _ => this.TypeState(expression)
        };

    private LoopState EvalName(ExpressionSyntax expression)
    {
        SemanticModel model = this.region.ModelFor(expression);
        ISymbol? symbol = model.GetSymbolInfo(expression).Symbol;

        switch (symbol)
        {
            case ILocalSymbol or IParameterSymbol:
                if (SymbolEqualityComparer.Default.Equals(x: symbol, y: this.region.Placeholder))
                {
                    return this.region.PlaceholderState;
                }

                if (this.bound.TryGetValue(key: symbol, value: out LoopState? boundState))
                {
                    return boundState;
                }

                return this.locals.TryGetValue(key: symbol, value: out LoopState? localState)
                    ? localState
                    : LoopState.None;
            case IPropertySymbol or IFieldSymbol:
            {
                // A member of this object, in a constructor the evaluator follows, has the state the
                // constructor gave it.
                if (expression is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
                {
                    return (this.thisMembers.TryGetValue(key: symbol.OriginalDefinition, value: out LoopState? own)
                            ? own
                            : LoopState.None)
                        .Join(this.TypeState(expression));
                }

                LoopState fromOwner = LoopState.None;

                if (expression is MemberAccessExpressionSyntax access)
                {
                    LoopState owner = this.Eval(access.Expression);

                    // An object whose construction was followed gives each member its own state,
                    // wherever the object came from: a local, a hold, or a lambda parameter.
                    if (owner.Members != null)
                    {
                        return owner.Members.StateOf(symbol).Join(this.TypeState(expression));
                    }

                    // Otherwise, a member of a pending value is pending: the cell behind a bindable,
                    // or one element of a tuple from a loop block.
                    ITypeSymbol memberType = symbol is IPropertySymbol p ? p.Type : ((IFieldSymbol)symbol).Type;

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
            argumentStates.Add(this.EvalArgument(argument: argument.Expression, runsNow: runsNow));
            argumentTypes.Add(model.GetTypeInfo(argument.Expression).Type);
        }

        LoopState inputs = argumentStates.Aggregate(seed: receiverState, func: static (a, b) => a.Join(b));

        if (!SodaFlowSymbols.IsSodaFlow(method))
        {
            return this.EvalForeignCall(
                invocation: invocation,
                method: method,
                receiverState: receiverState,
                argumentStates: argumentStates);
        }

        LoopState firstArgument = argumentStates.Count > 0 ? argumentStates[0] : LoopState.None;
        LoopState source = receiver != null ? receiverState : firstArgument;

        return OperatorTable.Rule(method: method, receiverType: receiverType) switch
        {
            OperatorRule.RunsNow => firstArgument,
            OperatorRule.Read => LoopState.None,
            OperatorRule.SampleLazy => new LoopState(
                pending: receiverState.Pending,
                linked: false,
                contentPending: false,
                contentLinked: false),
            OperatorRule.MapLazy => receiverState.WithLinked(false),
            OperatorRule.MapStream => new LoopState(
                pending: false,
                linked: receiverState.Linked,
                contentPending: false,
                contentLinked: false).WithContent(
                this.LambdaContent(invocation: invocation, parameterState: receiverState.Content)),
            // The result holds what the function returns, and not what the receiver held.
            OperatorRule.MapValue => new LoopState(
                pending: receiverState.Pending,
                linked: receiverState.Linked,
                contentPending: false,
                contentLinked: false).WithContent(
                this.LambdaContent(invocation: invocation, parameterState: receiverState.Content)),
            OperatorRule.LiftLazy => inputs.WithLinked(false),
            OperatorRule.Combine => new LoopState(
                pending: inputs.Pending,
                linked: inputs.Linked,
                contentPending: false,
                contentLinked: false),
            OperatorRule.SwitchValue => new LoopState(
                pending: receiverState.Pending || receiverState.ContentPending,
                linked: receiverState.Linked || receiverState.ContentLinked,
                contentPending: false,
                contentLinked: false),
            OperatorRule.SwitchStream => new LoopState(
                pending: false,
                linked: receiverState.ContentLinked,
                contentPending: false,
                contentLinked: false),
            OperatorRule.Hold => new LoopState(
                pending: false,
                linked: receiverState.Linked,
                contentPending: false,
                contentLinked: false).WithContent(receiverState.Content.Join(firstArgument)),
            OperatorRule.HoldLazy => new LoopState(
                pending: firstArgument.Pending,
                linked: receiverState.Linked,
                contentPending: false,
                contentLinked: false),
            OperatorRule.ConstantLazy => firstArgument.WithLinked(false),
            // A value given to a constant or a sink is content. The links of a value with members of
            // its own are in it, and not links of the cell.
            OperatorRule.Content => firstArgument.AsContent,
            OperatorRule.Fold => new LoopState(
                pending: false,
                linked: receiverState.Linked,
                contentPending: false,
                contentLinked: false),
            OperatorRule.Updates => source.WithPending(false),
            OperatorRule.MapTo => new LoopState(
                pending: false,
                linked: receiverState.Linked,
                contentPending: false,
                contentLinked: false).WithContent(firstArgument),
            // A snapshot samples its cells at each firing. Only the stream has a link.
            OperatorRule.StreamFromReceiver => new LoopState(
                pending: false,
                linked: receiverState.Linked,
                contentPending: receiverState.ContentPending,
                contentLinked: receiverState.ContentLinked),
            OperatorRule.MergeStreams => new LoopState(
                pending: false,
                linked: LinkedStreamInput(
                    receiverState: receiverState,
                    receiverType: receiverType,
                    argumentStates: argumentStates,
                    argumentTypes: argumentTypes),
                contentPending: inputs.ContentPending,
                contentLinked: inputs.ContentLinked),
            // A deferred read is pending when the function reads a pending value.
            OperatorRule.DeferLazy => new LoopState(
                pending: firstArgument.Pending || this.LambdaReadsPending(invocation),
                linked: false,
                contentPending: false,
                contentLinked: false),
            // A defer, a split, a timer, a debounce, a listener, and a send: no state.
            OperatorRule.Break => LoopState.None,
            OperatorRule.Default => DefaultLibraryResult(method: method, inputs: inputs),
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(method), message: "An operator rule has no case.")
        };
    }

    /// <summary>
    ///     An object made in the region. Its constructor is followed when it is
    ///     in this compilation, and the object then knows the state of each of its
    ///     members.
    /// </summary>
    private LoopState EvalCreation(ExpressionSyntax creation)
    {
        LoopState typeState = this.TypeState(creation);
        SemanticModel model = this.region.ModelFor(creation);

        ArgumentListSyntax? arguments =
            creation switch
            {
                ObjectCreationExpressionSyntax o => o.ArgumentList,
                ImplicitObjectCreationExpressionSyntax i => i.ArgumentList,
                _ => null
            };

        if (arguments == null
            || this.depth >= MaxInlineDepth
            || model.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor
            || SodaFlowSymbols.IsSodaFlow(constructor))
        {
            return typeState;
        }

        Construction? construction = this.FollowConstruction(constructor: constructor, arguments: arguments.Arguments);

        // The object has no node of its own. Its members hold the state, and a hold or a cell of
        // such objects reads them through the content.
        return construction == null ? typeState : typeState.WithMembers(construction);
    }

    private Construction? FollowConstruction(IMethodSymbol constructor, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        IMethodSymbol definition = constructor.OriginalDefinition;
        SyntaxNode? declaration = definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        // A primary constructor declares on its type. An explicit one declares in it.
        TypeDeclarationSyntax? type =
            declaration as TypeDeclarationSyntax ?? declaration?.Parent as TypeDeclarationSyntax;

        if (type == null)
        {
            return null;
        }

        SemanticModel model = this.region.ModelFor(type);
        ImmutableDictionary<ISymbol, LoopState>.Builder bound = this.bound.ToBuilder();
        List<IParameterSymbol> parameters = [];

        for (int i = 0; i < arguments.Count; i++)
        {
            IParameterSymbol? parameter =
                SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i);

            if (parameter != null)
            {
                bound[parameter] = this.EvalArgument(argument: arguments[i].Expression, runsNow: false);
                parameters.Add(parameter);
            }
        }

        LoopStateEvaluator inner = new(region: this.region, bound: bound.ToImmutable(), depth: this.depth + 1);

        ImmutableDictionary<ISymbol, LoopState>.Builder members =
            ImmutableDictionary.CreateBuilder<ISymbol, LoopState>(SymbolEqualityComparer.Default);

        List<SyntaxNode> followed = [];

        foreach (MemberDeclarationSyntax member in type.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax { Initializer: { } init } property:
                    Set(member: model.GetDeclaredSymbol(property), value: init.Value);
                    break;
                case FieldDeclarationSyntax field:
                    foreach (VariableDeclaratorSyntax declarator in field.Declaration.Variables)
                    {
                        if (declarator.Initializer != null)
                        {
                            Set(member: model.GetDeclaredSymbol(declarator), value: declarator.Initializer.Value);
                        }
                    }

                    break;
            }
        }

        if (declaration is ConstructorDeclarationSyntax explicitConstructor)
        {
            IEnumerable<ExpressionSyntax> assignments =
                explicitConstructor.Body != null
                    ? explicitConstructor.Body.Statements.OfType<ExpressionStatementSyntax>()
                        .Select(static s => s.Expression)
                    : explicitConstructor.ExpressionBody != null
                        ? [explicitConstructor.ExpressionBody.Expression]
                        : [];

            foreach (ExpressionSyntax expression in assignments)
            {
                if (expression is not AssignmentExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                    } assignment)
                {
                    continue;
                }

                ExpressionSyntax target =
                    assignment.Left is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access
                        ? access
                        : assignment.Left;

                if (model.GetSymbolInfo(target).Symbol is IPropertySymbol or IFieldSymbol)
                {
                    Set(member: model.GetSymbolInfo(target).Symbol, value: assignment.Right);
                }
            }
        }

        return new Construction(
            members: members.ToImmutable(),
            followedParameters: FollowedParameters(
                model: model,
                scope: type,
                parameters: parameters.ToImmutableArray(),
                evaluated: followed));

        void Set(ISymbol? member, ExpressionSyntax value)
        {
            if (member == null)
            {
                return;
            }

            LoopState state = inner.Eval(value);

            members[member] =
                members.TryGetValue(key: member, value: out LoopState? existing) ? existing.Join(state) : state;

            inner.thisMembers[member] = members[member];
            followed.Add(value);
        }
    }

    /// <summary>
    ///     A call to code that is not the library. A body in this compilation is
    ///     evaluated with the arguments bound. Any other callee, a delegate included,
    ///     is unknown: the result gets no state, and the walk reports the pending
    ///     argument with SF1003. A warning is for what the analyzer can see.
    /// </summary>
    private LoopState EvalForeignCall(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        IReadOnlyList<LoopState> argumentStates)
    {
        LoopState? inlined =
            this.TryInline(
                invocation: invocation,
                method: method,
                receiverState: receiverState,
                argumentStates: argumentStates);

        return (inlined ?? LoopState.None).Join(SodaFlowSymbols.StateOfType(method.ReturnType));
    }

    /// <summary>
    ///     A method with no rule here gives its result the state of its inputs,
    ///     where the result type can hold one. This is the conservative default.
    /// </summary>
    private static LoopState DefaultLibraryResult(IMethodSymbol method, LoopState inputs)
    {
        ITypeSymbol returnType = method.ReturnType;

        if (SodaFlowSymbols.IsStreamType(returnType))
        {
            return new LoopState(
                pending: false,
                linked: inputs.Linked,
                contentPending: inputs.ContentPending,
                contentLinked: inputs.ContentLinked);
        }

        return SodaFlowSymbols.IsCarrierType(returnType) ? inputs : LoopState.None;
    }

    /// <summary>
    ///     One level into a method in this compilation. The result is what the
    ///     body returns, with the parameters bound to the arguments. The locals of the
    ///     body are declared in order.
    /// </summary>
    private LoopState? TryInline(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        IReadOnlyList<LoopState> argumentStates)
    {
        if (this.depth >= MaxInlineDepth)
        {
            return null;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        SyntaxNode? declaration = definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        (ExpressionSyntax? expressionBody, BlockSyntax? block) =
            declaration switch
            {
                MethodDeclarationSyntax m => (m.ExpressionBody?.Expression, m.Body),
                LocalFunctionStatementSyntax f => (f.ExpressionBody?.Expression, f.Body),
                _ => (null, null)
            };

        if (declaration == null || (expressionBody == null && block == null))
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

        // By name or by position, as the compiler binds them. Named arguments can be in a sequence
        // that is not the sequence of the parameters. A params parameter gets the states of all its
        // arguments.
        Dictionary<ISymbol, LoopState> byParameter = new(SymbolEqualityComparer.Default);
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        for (int i = 0; i < argumentStates.Count; i++)
        {
            if (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                is not { } parameter)
            {
                continue;
            }

            byParameter[parameter] =
                byParameter.TryGetValue(key: parameter, value: out LoopState? first)
                    ? first.Join(argumentStates[i])
                    : argumentStates[i];
        }

        foreach (KeyValuePair<ISymbol, LoopState> pair in byParameter)
        {
            bound[pair.Key] = pair.Value;
        }

        LoopStateEvaluator inner = new(region: this.region, bound: bound.ToImmutable(), depth: this.depth + 1);
        LoopState result = inner.EvalBody(expressionBody: expressionBody, block: block);

        this.followedByCall[invocation] =
            FollowedParameters(
                model: this.region.ModelFor(declaration),
                scope: declaration,
                parameters: definition.Parameters,
                evaluated: Evaluated(expressionBody: expressionBody, block: block));

        return result;
    }

    /// <summary>
    ///     The expressions that <see cref="EvalBody" /> evaluates: the local
    ///     initializers and the returns.
    /// </summary>
    private static IEnumerable<SyntaxNode> Evaluated(SyntaxNode? expressionBody, BlockSyntax? block)
    {
        if (expressionBody != null)
        {
            return [expressionBody];
        }

        if (block == null)
        {
            return [];
        }

        return block.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(static d => d.Declaration.Variables)
            .Select(static v => v.Initializer?.Value)
            .Where(static v => v != null)
            .Cast<SyntaxNode>()
            .Concat(ReturnExpressions(block));
    }

    /// <summary>
    ///     The parameters with all uses in the scope in one of the expressions
    ///     that the evaluator read. The value went only where the result shows it.
    /// </summary>
    private static ImmutableHashSet<ISymbol> FollowedParameters(
        SemanticModel model,
        SyntaxNode scope,
        ImmutableArray<IParameterSymbol> parameters,
        IEnumerable<SyntaxNode> evaluated)
    {
        // To get the symbol of an identifier is slow, and the scope of a constructor is its full
        // type. Thus, this code reads each identifier one time. It gets the symbol only when the
        // text of the identifier is the name of a parameter. It gets no more symbols for a parameter
        // after it finds one use that is not in a covered expression.
        Dictionary<string, IParameterSymbol> byName = [];

        foreach (IParameterSymbol parameter in parameters)
        {
            byName[parameter.Name] = parameter;
        }

        HashSet<SyntaxNode> covered = [.. evaluated];
        HashSet<ISymbol> used = new(SymbolEqualityComparer.Default);

        foreach (IdentifierNameSyntax use in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (byName.TryGetValue(key: use.Identifier.ValueText, value: out IParameterSymbol? candidate)
                && !used.Contains(candidate)
                && SymbolEqualityComparer.Default.Equals(x: model.GetSymbolInfo(use).Symbol, y: candidate)
                && !use.AncestorsAndSelf().Any(covered.Contains))
            {
                used.Add(candidate);
            }
        }

        return parameters
            .Where(parameter => !used.Contains(parameter))
            .ToImmutableHashSet<ISymbol>(SymbolEqualityComparer.Default);
    }

    /// <summary>
    ///     What a body returns: its one expression, or the combined state of its
    ///     return statements.
    /// </summary>
    private LoopState EvalBody(ExpressionSyntax? expressionBody, BlockSyntax? block)
    {
        if (expressionBody != null)
        {
            return this.Eval(expressionBody);
        }

        if (block == null)
        {
            return LoopState.None;
        }

        SemanticModel model = this.region.ModelFor(block);

        // The locals of the block, in order, so that a return of one of them has a state. A local
        // with no initializer gets no state.
        foreach (VariableDeclaratorSyntax declarator in block.Statements
                     .OfType<LocalDeclarationStatementSyntax>()
                     .SelectMany(static declaration => declaration.Declaration.Variables))
        {
            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.Declare(local: local, state: this.Eval(declarator.Initializer?.Value));
            }
        }

        return this.JoinAll(ReturnExpressions(block));
    }

    /// <summary>
    ///     True when the function given to an operator reads a pending value,
    ///     which a deferred sample then reads when it is forced.
    /// </summary>
    private bool LambdaReadsPending(InvocationExpressionSyntax invocation)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        if (lambda == null)
        {
            return false;
        }

        SemanticModel model = this.region.ModelFor(lambda);

        foreach (InvocationExpressionSyntax read in lambda.Body.DescendantNodesAndSelf()
                     .OfType<InvocationExpressionSyntax>())
        {
            if (read.Expression is MemberAccessExpressionSyntax access
                && SodaFlowSymbols.ReadMembers.Contains(access.Name.Identifier.ValueText)
                && model.GetSymbolInfo(read).Symbol is IMethodSymbol readMethod
                && SodaFlowSymbols.IsSodaFlow(readMethod)
                && this.Eval(access.Expression).Pending)
            {
                return true;
            }
        }

        return false;
    }

    private LoopState EvalArgument(ExpressionSyntax argument, bool runsNow) =>
        argument switch
        {
            AnonymousFunctionExpressionSyntax lambda => runsNow
                ? this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray<LoopState>.Empty)
                : LoopState.None,
            // A method group such as x.Sample is a read that runs after this. It is pending, as a lazy
            // sample of x is.
            MemberAccessExpressionSyntax access
                when SodaFlowSymbols.ReadMembers.Contains(access.Name.Identifier.ValueText)
                     && this.region.ModelFor(access).GetSymbolInfo(access).Symbol is IMethodSymbol =>
                this.Eval(access.Expression).WithLinked(false),
            _ => this.Eval(argument)
        };

    /// <summary>
    ///     The state of what a lambda given to an operator returns, for the
    ///     content of a cell of cells or a cell of streams.
    /// </summary>
    private LoopState LambdaContent(InvocationExpressionSyntax invocation, LoopState parameterState)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        return lambda == null
            ? LoopState.None
            : this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray.Create(parameterState));
    }

    private LoopState EvalLambdaResult(
        AnonymousFunctionExpressionSyntax lambda,
        ImmutableArray<LoopState> boundParameters)
    {
        SemanticModel model = this.region.ModelFor(lambda);
        ImmutableDictionary<ISymbol, LoopState>.Builder bound = this.bound.ToBuilder();

        // Arrays, and not a collection expression. A collection expression here takes the natural
        // type of the other arms, SeparatedSyntaxList, and that conversion gives an empty list.
        IReadOnlyList<ParameterSyntax> parameters =
            lambda switch
            {
                SimpleLambdaExpressionSyntax s => new[] { s.Parameter },
                ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.ToArray(),
                AnonymousMethodExpressionSyntax { ParameterList: { } list } => list.Parameters.ToArray(),
                _ => Array.Empty<ParameterSyntax>()
            };

        for (int i = 0; i < parameters.Count && i < boundParameters.Length; i++)
        {
            if (model.GetDeclaredSymbol(parameters[i]) is { } symbol)
            {
                bound[symbol] = boundParameters[i];
            }
        }

        LoopStateEvaluator inner =
            boundParameters.IsEmpty
                ? this
                : new LoopStateEvaluator(region: this.region, bound: bound.ToImmutable(), depth: this.depth);

        return inner.EvalBody(expressionBody: lambda.ExpressionBody, block: lambda.Block);
    }

    /// <summary>
    ///     The expressions that a block returns, without those of a nested
    ///     function.
    /// </summary>
    public static IEnumerable<ExpressionSyntax> ReturnExpressions(SyntaxNode body) =>
        body.DescendantNodes(
                descendIntoChildren: static n =>
                    n is not (AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax))
            .OfType<ReturnStatementSyntax>()
            .Select(static r => r.Expression)
            .OfType<ExpressionSyntax>();

    private LoopState JoinAll(IEnumerable<ExpressionSyntax?> expressions) =>
        expressions.Aggregate(seed: LoopState.None, func: (state, e) => state.Join(this.Eval(e)));

    private LoopState TypeState(ExpressionSyntax expression) =>
        SodaFlowSymbols.StateOfType(this.region.ModelFor(expression).GetTypeInfo(expression).Type);

    private static bool LinkedStreamInput(
        LoopState receiverState,
        ITypeSymbol? receiverType,
        IReadOnlyList<LoopState> argumentStates,
        IReadOnlyList<ITypeSymbol?> argumentTypes)
    {
        bool linked =
            receiverState.Linked && (receiverType == null || !SodaFlowSymbols.IsCellOrBehaviorType(receiverType));

        for (int i = 0; i < argumentStates.Count; i++)
        {
            linked |= argumentStates[i].Linked && !SodaFlowSymbols.IsCellOrBehaviorType(argumentTypes[i]);
        }

        return linked;
    }

    /// <summary>
    ///     The code of a lambda given to one of these runs in the block, before
    ///     the loop closes. Every other operator keeps its lambda for a firing after
    ///     the close.
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

        string? suffix =
            Suffixes.FirstOrDefault(candidate =>
                name.Length > candidate.Length
                && name.EndsWith(value: candidate, comparisonType: StringComparison.Ordinal));

        return suffix == null ? name : name.Substring(startIndex: 0, length: name.Length - suffix.Length);
    }

    public static ExpressionSyntax? ReceiverOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return null;
        }

        return method.ReducedFrom != null || !method.IsStatic ? access.Expression : null;
    }
}
