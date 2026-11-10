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
///     value and the links of its inputs. A call to code of this compilation gets
///     its state from the body of that code. The evaluator walks that body as
///     the region walks the block, and gives the caller what the walk found.
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

    private static readonly ImmutableDictionary<ISymbol, LoopState> NoSymbols =
        ImmutableDictionary<ISymbol, LoopState>.Empty.WithComparers(SymbolEqualityComparer.Default);

    private readonly ImmutableDictionary<ISymbol, LoopState> bound;
    private readonly int depth;
    private readonly Dictionary<SyntaxNode, Followed?> followed = [];
    private readonly Dictionary<ISymbol, LoopState> locals;
    private readonly Dictionary<ExpressionSyntax, LoopState> memo = [];
    private readonly LoopRegion region;
    private readonly Dictionary<ISymbol, LoopState> thisMembers;

    public LoopStateEvaluator(LoopRegion region)
        : this(
            region: region,
            bound: NoSymbols,
            depth: 0,
            frame: new LoopFrame(),
            locals: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            thisMembers: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default))
    {
    }

    private LoopStateEvaluator(
        LoopRegion region,
        ImmutableDictionary<ISymbol, LoopState> bound,
        int depth,
        LoopFrame frame,
        Dictionary<ISymbol, LoopState> locals,
        Dictionary<ISymbol, LoopState> thisMembers)
    {
        this.region = region;
        this.bound = bound;
        this.depth = depth;
        this.Frame = frame;
        this.locals = locals;
        this.thisMembers = thisMembers;
    }

    /// <summary>The frame of the code that this evaluator reads.</summary>
    public LoopFrame Frame { get; }

    /// <summary>
    ///     True when the code is a member of an object that has a member with a
    ///     state. Then, a name with no owner can be a property of that object.
    /// </summary>
    public bool HasThisMembers => this.thisMembers.Count > 0;

    /// <summary>Records the state of a local at its declaration or assignment.</summary>
    public void Declare(ISymbol local, LoopState state) =>
        this.locals[local] =
            this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing.Join(state) : state;

    /// <summary>
    ///     Records the state of a member of the object that the code is in, at
    ///     its initializer or assignment.
    /// </summary>
    public void SetMember(ISymbol member, LoopState state)
    {
        ISymbol key = member.OriginalDefinition;

        this.thisMembers[key] =
            this.thisMembers.TryGetValue(key: key, value: out LoopState? existing) ? existing.Join(state) : state;
    }

    /// <summary>
    ///     Records a value that the code gives to a member of an object, as in
    ///     <c>row.Share = value</c>. An object in a local keeps the state with its
    ///     members. For each other owner, and for a static member, the region keeps
    ///     the state for the field.
    /// </summary>
    public void SetMemberOf(ExpressionSyntax? owner, ISymbol member, LoopState state)
    {
        if (owner is IdentifierNameSyntax
            && this.region.ModelFor(owner).GetSymbolInfo(owner).Symbol is { } holder
                and (ILocalSymbol or IParameterSymbol))
        {
            LoopState known =
                this.locals.TryGetValue(key: holder, value: out LoopState? existing) ? existing : LoopState.None;

            this.locals[holder] =
                known.WithMembers((known.Members ?? Construction.Empty).With(member: member, state: state));

            return;
        }

        this.region.SetField(field: member, state: state);
    }

    /// <summary>
    ///     Adds a state to what a name holds, for a value that a call puts into
    ///     the object that the name is for. <c>list.Add(value)</c> is such a call.
    /// </summary>
    public void Absorb(ExpressionSyntax target, LoopState state)
    {
        if (state.IsNone)
        {
            return;
        }

        switch (this.region.ModelFor(target).GetSymbolInfo(target).Symbol)
        {
            case { } local and (ILocalSymbol or IParameterSymbol) when target is IdentifierNameSyntax:
                this.Declare(local: local, state: state);
                break;
            case { IsStatic: false } member and (IFieldSymbol or IPropertySymbol)
                when target is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                this.SetMember(member: member, state: state);
                break;
            case { } field and (IFieldSymbol or IPropertySymbol):
                this.region.SetField(field: field, state: state);
                break;
        }
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

    /// <summary>
    ///     The result of a call, when the body of the callee is in this
    ///     compilation. The call is an invocation, the creation of an object, or
    ///     the read of a property with a getter. It is also the constructor that a
    ///     constructor calls first. Null for a callee with no body to read, and for
    ///     a callee that is too deep.
    /// </summary>
    public Followed? Follow(SyntaxNode call)
    {
        if (this.followed.TryGetValue(key: call, value: out Followed? known))
        {
            return known;
        }

        Followed? result =
            call switch
            {
                InvocationExpressionSyntax invocation => this.FollowInvocation(invocation),
                ObjectCreationExpressionSyntax creation =>
                    this.FollowCreation(creation: creation, arguments: creation.ArgumentList),
                ImplicitObjectCreationExpressionSyntax creation =>
                    this.FollowCreation(creation: creation, arguments: creation.ArgumentList),
                ConstructorInitializerSyntax chain => this.FollowChain(chain: chain, arguments: chain.ArgumentList),
                PrimaryConstructorBaseTypeSyntax chain =>
                    this.FollowChain(chain: chain, arguments: chain.ArgumentList),
                ExpressionSyntax access => this.FollowGetter(access),
                _ => null
            };

        this.followed[call] = result;

        return result;
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
            // The value of a?.b is the value of .b, whose owner is a.
            ConditionalAccessExpressionSyntax c => this.Eval(c.WhenNotNull),
            SwitchExpressionSyntax s => this.JoinAll(s.Arms.Select(static arm => arm.Expression)),
            WithExpressionSyntax w => this.WithInitializer(state: this.Eval(w.Expression), initializer: w.Initializer),
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
            // A lambda is a value. Its code runs when something calls it.
            AnonymousFunctionExpressionSyntax function =>
                LoopState.OfFunction(new FunctionValue(declaration: function, definer: this, receiver: null)),
            // The object that the code is in, with the members that the evaluator knows.
            ThisExpressionSyntax when this.thisMembers.Count > 0 =>
                LoopState.None.WithMembers(
                    new Construction(this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default))),
            InvocationExpressionSyntax inv => this.EvalInvocation(inv),
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => this.EvalCreation(expression),
            IdentifierNameSyntax
                or MemberAccessExpressionSyntax
                or MemberBindingExpressionSyntax
                or GenericNameSyntax => this.EvalName(expression),
            _ => this.TypeState(expression)
        };

    private LoopState EvalName(ExpressionSyntax expression)
    {
        SemanticModel model = this.region.ModelFor(expression);
        ISymbol? symbol = model.GetSymbolInfo(expression).Symbol;

        switch (symbol)
        {
            case ILocalSymbol or IParameterSymbol:
            {
                if (SymbolEqualityComparer.Default.Equals(x: symbol, y: this.region.Placeholder))
                {
                    return this.region.PlaceholderState;
                }

                LoopState named =
                    this.bound.TryGetValue(key: symbol, value: out LoopState? boundState) ? boundState : LoopState.None;

                if (this.locals.TryGetValue(key: symbol, value: out LoopState? localState))
                {
                    named = named.Join(localState);
                }

                // A method of an object can read a parameter of the primary constructor of the
                // object.
                return named.IsNone
                       && symbol is IParameterSymbol
                       && this.thisMembers.TryGetValue(key: symbol, value: out LoopState? captured)
                    ? captured
                    : named;
            }
            case IMethodSymbol group:
                return this.EvalMethodGroup(expression: expression, group: group);
            case IPropertySymbol or IFieldSymbol:
            {
                // A property with a getter has the state of what the getter returns.
                if (symbol is IPropertySymbol && this.Follow(expression) is { } getter)
                {
                    return getter.State.Join(this.TypeState(expression));
                }

                // A property of the library that reads the current value gives the value that its
                // owner holds.
                if (symbol is IPropertySymbol
                    && SodaFlowSymbols.ReadMembers.Contains(symbol.Name)
                    && this.region.Symbols.IsSodaFlow(symbol))
                {
                    return this.Eval(
                            expression switch
                            {
                                MemberAccessExpressionSyntax read => read.Expression,
                                MemberBindingExpressionSyntax read => ConditionalReceiver(read),
                                _ => null
                            })
                        .Content
                        .Join(this.TypeState(expression));
                }

                // A field that the code set while the loop is open, on an object that the walk does
                // not know.
                LoopState field = this.region.HasFields ? this.region.FieldState(symbol) : LoopState.None;

                // A member of this object, in a constructor the evaluator follows, has the state the
                // constructor gave it.
                if (expression is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
                {
                    return (this.thisMembers.TryGetValue(key: symbol.OriginalDefinition, value: out LoopState? own)
                            ? own
                            : LoopState.None)
                        .Join(field)
                        .Join(this.TypeState(expression));
                }

                LoopState fromOwner = field;

                ExpressionSyntax? ownerExpression =
                    expression switch
                    {
                        MemberAccessExpressionSyntax access => access.Expression,
                        MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
                        _ => null
                    };

                if (ownerExpression != null)
                {
                    LoopState owner = this.Eval(ownerExpression);

                    // An object whose construction was followed gives each member its own state,
                    // wherever the object came from: a local, a hold, or a lambda parameter.
                    if (owner.Members != null)
                    {
                        return owner.Members.StateOf(symbol)
                            .FromIfUnknown(owner.Origin)
                            .Join(this.TypeState(expression));
                    }

                    // Otherwise, a member of a pending value is pending. Such a member is the cell
                    // behind a bindable, one element of a tuple from a loop block, or the values of
                    // a dictionary.
                    ITypeSymbol memberType = symbol is IPropertySymbol p ? p.Type : ((IFieldSymbol)symbol).Type;

                    if (symbol.ContainingType.IsTupleType
                        || this.region.Symbols.CanHoldState(type: memberType, compilation: this.region.Assembly))
                    {
                        fromOwner = fromOwner.Join(owner);
                    }
                }

                return fromOwner.Join(this.TypeState(expression));
            }
            default:
                return this.TypeState(expression);
        }
    }

    /// <summary>
    ///     The name of a method, used as a value. A method or a local function of
    ///     this compilation is a function that a call of the delegate runs.
    /// </summary>
    private LoopState EvalMethodGroup(ExpressionSyntax expression, IMethodSymbol group)
    {
        // A method group such as x.Sample is a read that runs after this. It is pending, as a lazy
        // sample of x is.
        if (expression is MemberAccessExpressionSyntax read
            && SodaFlowSymbols.ReadMembers.Contains(read.Name.Identifier.ValueText)
            && this.region.Symbols.IsSodaFlow(group))
        {
            return this.Eval(read.Expression).WithLinked(false);
        }

        SyntaxNode? declaration =
            (group.ReducedFrom ?? group).OriginalDefinition.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .FirstOrDefault(static syntax => syntax is MethodDeclarationSyntax or LocalFunctionStatementSyntax);

        if (declaration == null)
        {
            return this.TypeState(expression);
        }

        return LoopState.OfFunction(
            new FunctionValue(
                declaration: declaration,
                definer: declaration is LocalFunctionStatementSyntax ? this : null,
                receiver: expression is MemberAccessExpressionSyntax { Expression: not ThisExpressionSyntax } owned
                    ? this.Eval(owned.Expression).Members
                    : null));
    }

    private LoopState EvalInvocation(InvocationExpressionSyntax invocation)
    {
        SemanticModel model = this.region.ModelFor(invocation);

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return this.TypeState(invocation);
        }

        // Code that is not the library. A body in this compilation gives the state. Code that
        // cannot know the library only moves a value, and its result has the state of its inputs.
        // Any other callee is unknown, and a delegate with no known function is one. The result
        // gets no state, and the walk reports a pending argument with SF1003. A warning is for
        // what the analyzer can see.
        if (!this.region.Symbols.IsSodaFlow(method))
        {
            LoopState result =
                this.Classify(call: invocation, method: method) == CalleeKind.Mover
                    ? this.MovedResult(invocation: invocation, method: method)
                    : LoopState.None;

            return result
                .Join(this.Follow(invocation)?.State ?? LoopState.None)
                .Join(this.region.Symbols.StateOfType(method.ReturnType));
        }

        ExpressionSyntax? receiver =
            ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        LoopState receiverState = this.Eval(receiver);
        ITypeSymbol? receiverType = receiver == null ? null : model.GetTypeInfo(receiver).Type;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        List<LoopState> argumentStates = [];
        List<ITypeSymbol?> argumentTypes = [];

        // In a static call of an extension method, the receiver is one of the arguments. It is not
        // also an argument of the operator.
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression == receiver)
            {
                continue;
            }

            argumentStates.Add(
                this.EvalArgument(
                    argument: arguments[i].Expression,
                    runsNow: OperatorTable.RunsFunction(
                        method: method,
                        parameter: ParameterFor(method: method, argument: arguments[i], position: i))));

            argumentTypes.Add(model.GetTypeInfo(arguments[i].Expression).Type);
        }

        LoopState inputs = argumentStates.Aggregate(seed: receiverState, func: static (a, b) => a.Join(b));
        LoopState firstArgument = argumentStates.Count > 0 ? argumentStates[0] : LoopState.None;
        LoopState source = receiver != null ? receiverState : firstArgument;

        LoopState state =
            OperatorTable.Rule(symbols: this.region.Symbols, method: method, receiverType: receiverType) switch
            {
                // A function that is not written at the call is a lambda in a local, a local
                // function, or a method. The evaluator follows it.
                OperatorRule.RunsNow => firstArgument.Join(this.Follow(invocation)?.State ?? LoopState.None),
                // A read gives the value that the receiver holds, with the state of that value.
                OperatorRule.Read => receiverState.Content,
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
                // A value given to a constant or a sink is content. The links of a value with members
                // of its own are in it, and not links of the cell.
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
                        symbols: this.region.Symbols,
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
                OperatorRule.Default => DefaultLibraryResult(
                    symbols: this.region.Symbols,
                    method: method,
                    inputs: inputs),
                _ => throw new ArgumentOutOfRangeException(
                    paramName: nameof(invocation),
                    message: "An operator rule has no case.")
            };

        // The result of an operator came into this code as its inputs did.
        return state.FromIfUnknown(inputs.Origin);
    }

    /// <summary>
    ///     An object made in the region. Its constructor is followed when it is
    ///     in this compilation, and the object then knows the state of each of its
    ///     members.
    /// </summary>
    private LoopState EvalCreation(ExpressionSyntax creation)
    {
        InitializerExpressionSyntax? initializer =
            creation switch
            {
                ObjectCreationExpressionSyntax o => o.Initializer,
                ImplicitObjectCreationExpressionSyntax i => i.Initializer,
                _ => null
            };

        // The object has no node of its own. Its members hold the state, and a hold or a cell of
        // such objects reads them through the content. An object of code that cannot know the
        // library, such as a list, has the state of the values that its constructor gets.
        LoopState state =
            this.TypeState(creation)
                .Join(this.Follow(creation)?.State ?? LoopState.None)
                .Join(this.MovedInto(creation));

        return initializer == null ? state : this.WithInitializer(state: state, initializer: initializer);
    }

    /// <summary>
    ///     The state of an object after an initializer. An object initializer or
    ///     a with expression sets members, and the state of each replaces the state
    ///     from the constructor. A collection initializer adds elements, and the
    ///     collection gets their states also.
    /// </summary>
    private LoopState WithInitializer(LoopState state, InitializerExpressionSyntax initializer)
    {
        SemanticModel model = this.region.ModelFor(initializer);

        foreach (ExpressionSyntax expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment
                && model.GetSymbolInfo(name).Symbol is { } member and (IPropertySymbol or IFieldSymbol))
            {
                state =
                    state.WithMembers(
                        (state.Members ?? Construction.Empty).With(member: member, state: this.Eval(assignment.Right)));
            }
            else
            {
                // An element of a collection, or the value for an index.
                state =
                    state.Join(
                        this.Eval(expression is AssignmentExpressionSyntax indexed ? indexed.Right : expression));
            }
        }

        return state;
    }

    /// <summary>
    ///     What the analyzer knows about the callee of a call to code that is not
    ///     the library.
    /// </summary>
    public CalleeKind Classify(SyntaxNode call, IMethodSymbol method)
    {
        if (this.region.Symbols.IsSodaFlow(method))
        {
            return CalleeKind.Library;
        }

        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            return this.Follow(call) != null ? CalleeKind.Body : CalleeKind.Unknown;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        bool hasBody =
            definition.MethodKind == MethodKind.Constructor
                ? definition.DeclaringSyntaxReferences.Length > 0
                  || (definition.IsImplicitlyDeclared
                      && definition.ContainingType.DeclaringSyntaxReferences.Length > 0)
                : BodyOf(definition) != null;

        if (hasBody)
        {
            // A body that is too deep is code that the analyzer does not read.
            return this.Follow(call) != null ? CalleeKind.Body : CalleeKind.Unknown;
        }

        return this.region.Symbols.CanKnowTheLibrary(definition.ContainingAssembly)
            ? CalleeKind.Unknown
            : CalleeKind.Mover;
    }

    /// <summary>
    ///     The state of the values that a call gives to code that cannot know the
    ///     library. They are the receiver and each argument that is not a function
    ///     for the callee to run. That code can keep them, give them back, or give
    ///     them to a function of the caller.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The callee.</param>
    /// <param name="withReceiver">
    ///     False for only the arguments, which are what the call can put into the
    ///     receiver.
    /// </param>
    public LoopState Moved(InvocationExpressionSyntax invocation, IMethodSymbol method, bool withReceiver)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        ExpressionSyntax? receiver = ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        return this.MovedArguments(
            definition: definition,
            arguments: invocation.ArgumentList.Arguments,
            offset: method.ReducedFrom != null ? 1 : 0,
            seed: withReceiver ? this.Eval(receiver) : LoopState.None);
    }

    private LoopState MovedArguments(
        IMethodSymbol definition,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int offset,
        LoopState seed)
    {
        LoopState moved = seed;

        // A callee that returns a delegate gives back the function that it got, as a wrapper
        // does. Then, the function is a value that it moves.
        bool givesBackFunctions = definition.ReturnType.TypeKind == TypeKind.Delegate;

        for (int i = 0; i < arguments.Count; i++)
        {
            // A function for a delegate parameter is code for the callee to run, and not a value
            // that it moves.
            if (givesBackFunctions
                || SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                    is not { Type.TypeKind: TypeKind.Delegate })
            {
                moved = moved.Join(this.Eval(arguments[i].Expression));
            }
        }

        return moved;
    }

    /// <summary>
    ///     The result of a call to code that cannot know the library, where the
    ///     type of the result can hold a state. The result has the state of the
    ///     values that the callee got, and of what a lambda that it runs returns.
    /// </summary>
    private LoopState MovedResult(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!this.region.Symbols.CanHoldState(type: method.ReturnType, compilation: this.region.Assembly))
        {
            return LoopState.None;
        }

        LoopState moved = this.Moved(invocation: invocation, method: method, withReceiver: true);
        LoopState result = moved;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression is AnonymousFunctionExpressionSyntax lambda
                && this.RunsFunction(
                    kind: CalleeKind.Mover,
                    method: method,
                    parameter: ParameterFor(method: method, argument: arguments[i], position: i)))
            {
                result =
                    result.Join(
                        this.EvalLambdaResult(
                            lambda: lambda,
                            boundParameters: ParametersOf(lambda).Select(_ => moved).ToImmutableArray()));
            }
        }

        return result;
    }

    /// <summary>
    ///     True when the callee runs, in the call, the function that it gets for
    ///     the given parameter.
    /// </summary>
    /// <remarks>
    ///     A library method says so in <see cref="OperatorTable" />. A callee
    ///     whose body the evaluator follows gets the function as a value, and the
    ///     body says what runs. For code that the analyzer cannot read, only the
    ///     call itself can tell. A constructor keeps a function for a time
    ///     after the call, as <c>Lazy</c> and a command object do. A method runs a
    ///     function given for a delegate parameter. It does not when it returns a
    ///     delegate or a <c>Lazy</c>, which then has the function. A function given
    ///     for a parameter of a different type, as to <c>Add</c> of a list, is a
    ///     value that the callee keeps.
    /// </remarks>
    public bool RunsFunction(CalleeKind kind, IMethodSymbol method, IParameterSymbol? parameter) =>
        kind switch
        {
            CalleeKind.Library => OperatorTable.RunsFunction(method: method, parameter: parameter),
            CalleeKind.Body => false,
            _ => method.MethodKind != MethodKind.Constructor
                 && parameter is { Type.TypeKind: TypeKind.Delegate }
                 && method.ReturnType.TypeKind != TypeKind.Delegate
                 && method.ReturnType.OriginalDefinition is not INamedTypeSymbol
                 {
                     Name: "Lazy",
                     ContainingNamespace.Name: "System"
                 }
                 && !RunsOnAnotherThread(method.ContainingType)
        };

    /// <summary>
    ///     The parameter that gets an argument of a call. It is a parameter of the
    ///     definition of the callee, which has the types that the callee itself
    ///     knows. For a call of a delegate, it is a parameter of that delegate.
    /// </summary>
    public static IParameterSymbol? ParameterFor(IMethodSymbol method, ArgumentSyntax argument, int position) =>
        method.MethodKind == MethodKind.DelegateInvoke
            ? SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position)
            : SodaFlowSymbols.ParameterOf(
                callee: (method.ReducedFrom ?? method).OriginalDefinition,
                argument: argument,
                position: position + (method.ReducedFrom != null ? 1 : 0));

    /// <summary>
    ///     A type whose lambdas run on a different thread or after the block, so
    ///     not in the block.
    /// </summary>
    private static bool RunsOnAnotherThread(ISymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() is "System.Threading" or "System.Threading.Tasks"
        && type.Name is "Thread" or "Task" or "TaskFactory" or "ThreadPool" or "Timer" or "SynchronizationContext";

    /// <summary>
    ///     The state of an object from a constructor of code that cannot know the
    ///     library: the state of the arguments.
    /// </summary>
    private LoopState MovedInto(ExpressionSyntax creation)
    {
        BaseArgumentListSyntax? arguments =
            creation switch
            {
                ObjectCreationExpressionSyntax written => written.ArgumentList,
                ImplicitObjectCreationExpressionSyntax implied => implied.ArgumentList,
                _ => null
            };

        return arguments != null
               && this.region.ModelFor(creation).GetSymbolInfo(creation).Symbol is IMethodSymbol constructor
               && this.Classify(call: creation, method: constructor) == CalleeKind.Mover
            ? this.MovedArguments(
                definition: constructor.OriginalDefinition,
                arguments: arguments.Arguments,
                offset: 0,
                seed: LoopState.None)
            : LoopState.None;
    }

    private Followed? FollowInvocation(InvocationExpressionSyntax invocation)
    {
        SemanticModel model = this.region.ModelFor(invocation);

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        // A call of a delegate runs the functions that the delegate can be.
        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            ExpressionSyntax target = DelegateOf(invocation: invocation, model: model);
            LoopState targetState = this.Eval(target);

            return targetState.Functions.IsEmpty
                ? null
                : this.FollowFunctions(
                    call: invocation,
                    name: NameOf(target),
                    functions: targetState.Functions,
                    target: targetState,
                    arguments: arguments);
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (this.region.Symbols.IsSodaFlow(method) || BodyOf(definition) is not { } body)
        {
            return this.FollowGivenFunctions(invocation: invocation, method: method);
        }

        ExpressionSyntax? receiver =
            ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        IReadOnlyList<Binding> bindings =
            this.Bind(
                definition: definition,
                arguments: arguments,
                extensionReceiver: method.ReducedFrom != null ? receiver : null);

        // A local function is part of the code that contains it, and reads the locals of that
        // code.
        if (definition.MethodKind == MethodKind.LocalFunction)
        {
            return this.Enter(
                call: invocation,
                name: method.Name,
                body: body,
                bindings: bindings,
                definer: this,
                members: this.thisMembers,
                receiver: null,
                target: null);
        }

        (Dictionary<ISymbol, LoopState> members, LoopState? receiverState) =
            this.MembersFor(isStatic: definition.IsStatic, owner: receiver);

        return this.Enter(
            call: invocation,
            name: method.Name,
            body: body,
            bindings: bindings,
            definer: null,
            members: members,
            receiver: receiverState,
            target: null);
    }

    /// <summary>
    ///     A call to code with no body to read that runs a function in the call.
    ///     The walk reads a lambda written at the call where it is. Each other
    ///     function, such as a local function or a lambda in a local, gets a frame
    ///     as a call of it does. Each parameter of that function has the state of
    ///     the values that the callee can give it.
    /// </summary>
    private Followed? FollowGivenFunctions(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        CalleeKind kind =
            this.region.Symbols.IsSodaFlow(method)
                ? CalleeKind.Library
                : this.region.Symbols.CanKnowTheLibrary(method.ContainingAssembly)
                    ? CalleeKind.Unknown
                    : CalleeKind.Mover;

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        Followed? all = null;

        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax written = arguments[i].Expression;

            if (written is AnonymousFunctionExpressionSyntax
                || !this.RunsFunction(
                    kind: kind,
                    method: method,
                    parameter: ParameterFor(method: method, argument: arguments[i], position: i)))
            {
                continue;
            }

            LoopState functions = this.Eval(written);

            if (functions.Functions.IsEmpty)
            {
                continue;
            }

            LoopState given =
                kind == CalleeKind.Mover
                    ? this.Moved(invocation: invocation, method: method, withReceiver: true)
                    : LoopState.None;

            foreach (FunctionValue function in functions.Functions)
            {
                all =
                    Both(
                        first: all,
                        second: this.FollowFunction(
                            call: invocation,
                            name: NameOf(written),
                            function: function,
                            target: functions,
                            arguments: default,
                            each: (written, given)));
            }
        }

        return all;
    }

    private static Followed? Both(Followed? first, Followed? second) =>
        first == null
            ? second
            : second == null
                ? first
                : new Followed(
                    state: first.State.Join(second.State),
                    findings: first.Findings.AddRange(second.Findings),
                    outs: first.Outs.SetItems(second.Outs));

    private Followed? FollowFunctions(
        SyntaxNode call,
        string name,
        ImmutableArray<FunctionValue> functions,
        LoopState target,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        Followed? all = null;

        foreach (FunctionValue function in functions)
        {
            Followed? one =
                this.FollowFunction(
                    call: call,
                    name: name,
                    function: function,
                    target: target,
                    arguments: arguments,
                    each: null);

            all = Both(first: all, second: one);
        }

        return all;
    }

    /// <param name="call">The call that runs the function.</param>
    /// <param name="name">The name of the function, for a report.</param>
    /// <param name="function">The function.</param>
    /// <param name="target">The delegate that the function is a value of.</param>
    /// <param name="arguments">The arguments, when the call is a call of the delegate.</param>
    /// <param name="each">
    ///     For a callee with no body that runs the function. It is the expression
    ///     that names the function at the call, and the state that each of its
    ///     parameters gets.
    /// </param>
    private Followed? FollowFunction(
        SyntaxNode call,
        string name,
        FunctionValue function,
        LoopState target,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        (ExpressionSyntax Written, LoopState Given)? each)
    {
        SemanticModel model = this.region.ModelFor(function.Declaration);

        IReadOnlyList<Binding> Bindings(ImmutableArray<IParameterSymbol> parameters) =>
            each is var (written, given)
                ?
                [
                    .. parameters.Select(parameter =>
                        new Binding(parameter: parameter, argument: written, state: given))
                ]
                : this.BindByPosition(parameters: parameters, arguments: arguments);

        switch (function.Declaration)
        {
            case AnonymousFunctionExpressionSyntax lambda when function.Definer != null:
                return this.Enter(
                    call: call,
                    name: name,
                    body: lambda.Body,
                    bindings: Bindings(
                        ParametersOf(lambda)
                            .Select(parameter => model.GetDeclaredSymbol(parameter))
                            .OfType<IParameterSymbol>()
                            .ToImmutableArray()),
                    definer: function.Definer,
                    members: function.Definer.thisMembers,
                    receiver: null,
                    target: target);
            case LocalFunctionStatementSyntax or MethodDeclarationSyntax
                when model.GetDeclaredSymbol(function.Declaration) is IMethodSymbol method
                     && BodyOf(method) is { } body:
            {
                IReadOnlyList<Binding> bindings = Bindings(method.Parameters);

                if (function.Definer != null)
                {
                    return this.Enter(
                        call: call,
                        name: name,
                        body: body,
                        bindings: bindings,
                        definer: function.Definer,
                        members: function.Definer.thisMembers,
                        receiver: null,
                        target: target);
                }

                LoopState owner = LoopState.None.WithMembers(function.Receiver);

                return this.Enter(
                    call: call,
                    name: name,
                    body: body,
                    bindings: bindings,
                    definer: null,
                    members: TaggedMembers(owner),
                    receiver: owner,
                    target: null);
            }
            default:
                return null;
        }
    }

    private Followed? FollowCreation(ExpressionSyntax creation, BaseArgumentListSyntax? arguments) =>
        this.region.ModelFor(creation).GetSymbolInfo(creation).Symbol is IMethodSymbol constructor
        && !this.region.Symbols.IsSodaFlow(constructor)
            ? this.FollowConstructor(
                call: creation,
                constructor: constructor,
                arguments: arguments?.Arguments ?? default)
            : null;

    /// <summary>
    ///     The constructor that a constructor calls first: the constructor of the
    ///     base, or a second constructor of the type. The members that it sets are
    ///     members of the object that this code is in.
    /// </summary>
    private Followed? FollowChain(SyntaxNode chain, ArgumentListSyntax? arguments)
    {
        if (this.region.ModelFor(chain).GetSymbolInfo(chain).Symbol is not IMethodSymbol constructor
            || this.region.Symbols.IsSodaFlow(constructor))
        {
            return null;
        }

        Followed? first =
            this.FollowConstructor(
                call: chain,
                constructor: constructor,
                arguments: arguments?.Arguments ?? default);

        if (first?.State.Members != null)
        {
            foreach (KeyValuePair<ISymbol, LoopState> pair in first.State.Members.Members)
            {
                this.SetMember(member: pair.Key, state: pair.Value);
            }
        }

        return first;
    }

    private Followed? FollowConstructor(
        SyntaxNode call,
        IMethodSymbol constructor,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        IMethodSymbol definition = constructor.OriginalDefinition;
        SyntaxNode? declaration = definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        // A primary constructor declares on its type. An explicit one declares in it. A type with
        // no constructor gets one from the compiler, which runs the initializers of the type.
        TypeDeclarationSyntax? type =
            declaration as TypeDeclarationSyntax
            ?? declaration?.Parent as TypeDeclarationSyntax
            ?? (definition.IsImplicitlyDeclared
                ? definition.ContainingType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    as TypeDeclarationSyntax
                : null);

        if (type == null)
        {
            return null;
        }

        IReadOnlyList<Binding> bindings =
            this.Bind(definition: definition, arguments: arguments, extensionReceiver: null);

        if (!this.region.HasFields && bindings.All(static binding => binding.State.IsNone))
        {
            return Followed.Nothing;
        }

        if (this.depth >= MaxInlineDepth || !this.region.TryEnter())
        {
            return null;
        }

        Dictionary<ISymbol, LoopState> members = new(SymbolEqualityComparer.Default);
        LoopStateEvaluator inner = this.Inner(bindings: bindings, definer: null, members: members);
        List<Finding> found = [];
        SinkWalker walker = new(region: this.region, evaluator: inner, sink: found.Add);
        SemanticModel model = this.region.ModelFor(type);

        if (declaration is TypeDeclarationSyntax)
        {
            foreach (IParameterSymbol parameter in definition.Parameters)
            {
                if (!inner.bound.TryGetValue(key: parameter, value: out LoopState? given) || given.IsNone)
                {
                    continue;
                }

                // A method of the object can read a parameter of a primary constructor.
                members[parameter] = given;

                // A positional parameter of a record is also the property of that name. That
                // property has no declaration of its own.
                if (declaration is RecordDeclarationSyntax
                    && definition.ContainingType.GetMembers(parameter.Name)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault(static property =>
                            property.DeclaringSyntaxReferences.Any(static reference =>
                                reference.GetSyntax() is ParameterSyntax)) is { } positional)
                {
                    members[positional] = given;
                }
            }
        }

        // The constructor that this one calls first. The walk gives its members to this object.
        SyntaxNode? chain =
            declaration switch
            {
                ConstructorDeclarationSyntax written => written.Initializer,
                TypeDeclarationSyntax =>
                    type.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault(),
                _ => null
            };

        walker.Visit(chain);

        // The initializers of the members run in each constructor, but not in one that calls a
        // second constructor of the type first.
        if (chain is not ConstructorInitializerSyntax { RawKind: (int)SyntaxKind.ThisConstructorInitializer })
        {
            foreach (MemberDeclarationSyntax member in type.Members)
            {
                switch (member)
                {
                    case PropertyDeclarationSyntax { Initializer: { } init } property
                        when !IsStatic(property.Modifiers):
                        Set(member: model.GetDeclaredSymbol(property), value: init.Value);
                        break;
                    case FieldDeclarationSyntax field when !IsStatic(field.Modifiers):
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
        }

        // The body. The walk records each member that the body sets.
        if (declaration is ConstructorDeclarationSyntax explicitConstructor)
        {
            walker.Visit(explicitConstructor.Body ?? (SyntaxNode?)explicitConstructor.ExpressionBody?.Expression);
        }

        return this.Leave(
            call: call,
            name: definition.ContainingType.Name,
            bindings: bindings,
            inner: inner,
            lexical: false,
            receiver: null,
            target: null,
            result: LoopState.None.WithMembers(
                new Construction(members.ToImmutableDictionary(SymbolEqualityComparer.Default))),
            found: found);

        void Set(ISymbol? member, ExpressionSyntax value)
        {
            if (member == null)
            {
                return;
            }

            walker.Visit(value);
            inner.SetMember(member: member, state: inner.Eval(value));
        }
    }

    /// <summary>
    ///     The read of a property whose getter has a body. The getter is a member
    ///     of the object that owns the property, and reads the members of that
    ///     object.
    /// </summary>
    private Followed? FollowGetter(ExpressionSyntax access)
    {
        if (this.region.ModelFor(access).GetSymbolInfo(access).Symbol is not IPropertySymbol property
            || this.region.Symbols.IsSodaFlow(property)
            || GetterOf(property.OriginalDefinition) is not { } body)
        {
            return null;
        }

        ExpressionSyntax? owner =
            access switch
            {
                MemberAccessExpressionSyntax member => member.Expression,
                MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
                _ => null
            };

        (Dictionary<ISymbol, LoopState> members, LoopState? receiver) =
            this.MembersFor(isStatic: property.IsStatic, owner: owner);

        return this.Enter(
            call: access,
            name: property.Name,
            body: body,
            bindings: [],
            definer: null,
            members: members,
            receiver: receiver,
            target: null);
    }

    /// <summary>
    ///     Walks the body of a callee as a call from this code, and gives the
    ///     caller what the walk found.
    /// </summary>
    /// <param name="call">The call, which is where the caller reports a finding about a member.</param>
    /// <param name="name">The name of the callee, for a report.</param>
    /// <param name="body">The block or the expression of the callee.</param>
    /// <param name="bindings">The arguments, each with the parameter that gets it.</param>
    /// <param name="definer">
    ///     For a lambda or a local function, the evaluator of the code that
    ///     contains it. The body reads the locals of that code, and is part of its
    ///     frame. Null for a callee with a frame of its own.
    /// </param>
    /// <param name="members">The members of the object that the callee is a member of.</param>
    /// <param name="receiver">
    ///     The object, when the call names it and the members have that object as
    ///     their origin.
    /// </param>
    /// <param name="target">The delegate, when the call is a call of a delegate.</param>
    private Followed? Enter(
        SyntaxNode call,
        string name,
        SyntaxNode body,
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members,
        LoopState? receiver,
        LoopState? target)
    {
        // Nothing from the loop goes in, thus nothing from the loop comes out. A lambda or a local
        // function can read the locals of the code that it is in, thus the walk always reads it.
        if (definer == null
            && members.Count == 0
            && !this.region.HasFields
            && bindings.All(static binding => binding.State.IsNone))
        {
            return Followed.Nothing;
        }

        if (this.depth >= MaxInlineDepth || !this.region.TryEnter())
        {
            return null;
        }

        LoopStateEvaluator inner = this.Inner(bindings: bindings, definer: definer, members: members);
        List<Finding> found = [];
        new SinkWalker(region: this.region, evaluator: inner, sink: found.Add).Visit(body);

        return this.Leave(
            call: call,
            name: name,
            bindings: bindings,
            inner: inner,
            lexical: definer != null,
            receiver: receiver,
            target: target,
            result: body is ExpressionSyntax expression
                ? inner.Eval(expression)
                : inner.JoinAll(ReturnExpressions(body)),
            found: found);
    }

    /// <summary>
    ///     The evaluator for the body of a callee, with each parameter bound to
    ///     the state of its argument. The parameter is the origin of that state.
    /// </summary>
    private LoopStateEvaluator Inner(
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder parameters = (definer?.bound ?? NoSymbols).ToBuilder();
        HashSet<ISymbol> given = new(SymbolEqualityComparer.Default);

        foreach (Binding binding in bindings)
        {
            LoopState state = binding.State.From(binding.Parameter);

            // A params parameter gets the states of all its arguments.
            parameters[binding.Parameter] =
                given.Add(binding.Parameter) ? state : parameters[binding.Parameter].Join(state);
        }

        return new LoopStateEvaluator(
            region: this.region,
            bound: parameters.ToImmutable(),
            depth: this.depth + 1,
            frame: definer?.Frame ?? new LoopFrame(),
            locals: definer?.locals ?? new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            thisMembers: definer?.thisMembers ?? members);
    }

    /// <summary>
    ///     Changes the result and the findings of a callee from the names of the
    ///     callee to the names of this code. A finding about a parameter becomes a
    ///     finding at the argument for that parameter. A finding about a member
    ///     becomes a finding at the call.
    /// </summary>
    private Followed Leave(
        SyntaxNode call,
        string name,
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator inner,
        bool lexical,
        LoopState? receiver,
        LoopState? target,
        LoopState result,
        IReadOnlyList<Finding> found)
    {
        Dictionary<ISymbol, Binding> byParameter = new(SymbolEqualityComparer.Default);

        foreach (Binding binding in bindings)
        {
            if (!byParameter.TryGetValue(key: binding.Parameter, value: out Binding first) || first.State.IsNone)
            {
                byParameter[binding.Parameter] = binding;
            }
        }

        // A lambda that a callee runs is part of the frame whose code contains it. That frame can
        // be a different one from this code.
        bool sameFrame = ReferenceEquals(objA: inner.Frame, objB: this.Frame);
        ImmutableArray<Finding>.Builder findings = ImmutableArray.CreateBuilder<Finding>();

        foreach (Finding finding in found)
        {
            // A finding of a different frame goes to that frame as it is.
            if (!ReferenceEquals(objA: finding.Home, objB: inner.Frame))
            {
                findings.Add(finding);

                continue;
            }

            if (finding.Origin != null && byParameter.TryGetValue(key: finding.Origin, value: out Binding binding))
            {
                // An AsCell() at the argument is the assertion of the caller that nothing reads the
                // value. The analyzer reports a read that it can see, whatever the caller asserted.
                if (finding.Escape && IsAsserted(binding.Argument))
                {
                    continue;
                }

                findings.Add(
                    new Finding(
                        home: this.Frame,
                        origin: binding.State.Origin,
                        escape: finding.Escape,
                        diagnostic: finding.Escape
                            ? Diagnostic.Create(
                                descriptor: Diagnostics.Escape,
                                location: binding.Argument.GetLocation(),
                                this.region.PlaceholderName,
                                finding.Subject)
                            : Diagnostic.Create(
                                descriptor: Diagnostics.ReadInCallee,
                                location: binding.Argument.GetLocation(),
                                name,
                                finding.Origin.Name,
                                this.region.PlaceholderName),
                        subject: finding.Subject));

                continue;
            }

            if (lexical)
            {
                findings.Add(finding);

                continue;
            }

            // The caller did not give a value that has no origin. A callee can only have it from a
            // placeholder type, and the analyzer does not know which loop that placeholder is of.
            if (finding.Origin == null)
            {
                continue;
            }

            findings.Add(
                new Finding(
                    home: this.Frame,
                    origin: Out(finding.Origin),
                    escape: finding.Escape,
                    diagnostic: finding.Escape
                        ? Diagnostic.Create(
                            descriptor: Diagnostics.Escape,
                            location: call.GetLocation(),
                            this.region.PlaceholderName,
                            finding.Subject)
                        : Diagnostic.Create(
                            descriptor: Diagnostics.MemberReadInCallee,
                            location: call.GetLocation(),
                            name,
                            finding.Origin.Name,
                            this.region.PlaceholderName),
                    subject: finding.Subject));
        }

        ISymbol? inputs =
            bindings.Select(static binding => binding.State.Origin).FirstOrDefault(static origin => origin != null)
            ?? receiver?.Origin
            ?? target?.Origin;

        ImmutableDictionary<ISymbol, LoopState>.Builder outs =
            ImmutableDictionary.CreateBuilder<ISymbol, LoopState>(SymbolEqualityComparer.Default);

        foreach (Binding binding in bindings.Where(static binding => binding.Parameter.RefKind != RefKind.None))
        {
            if (inner.locals.TryGetValue(key: binding.Parameter, value: out LoopState? assigned))
            {
                outs[binding.Parameter] = assigned.WithOrigins(Out).FromIfUnknown(inputs);
            }
        }

        return new Followed(
            state: result.WithOrigins(Out).FromIfUnknown(inputs),
            findings: findings.ToImmutable(),
            outs: outs.ToImmutable());

        // The origin in this code of a value that has the given origin in the callee.
        ISymbol? Out(ISymbol? origin)
        {
            if (origin != null && byParameter.TryGetValue(key: origin, value: out Binding given))
            {
                return given.State.Origin;
            }

            if (lexical)
            {
                return sameFrame ? origin : target?.Origin;
            }

            return origin != null && receiver?.Members?.StateOf(origin) is { IsNone: false } member
                ? member.Origin ?? receiver.Origin
                : origin;
        }
    }

    /// <summary>
    ///     The members of the object that a method or a getter is a member of. A
    ///     call with no owner, or on <c>this</c>, is on the object that this code
    ///     is in. A call on a different object gets the members of that object, and
    ///     each member is the origin of its state.
    /// </summary>
    private (Dictionary<ISymbol, LoopState> Members, LoopState? Receiver) MembersFor(
        bool isStatic,
        ExpressionSyntax? owner)
    {
        if (isStatic)
        {
            return (new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default), null);
        }

        if (owner is null or ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return (this.thisMembers, null);
        }

        LoopState state = this.Eval(owner);

        return (TaggedMembers(state), state);
    }

    private static Dictionary<ISymbol, LoopState> TaggedMembers(LoopState owner)
    {
        Dictionary<ISymbol, LoopState> members = new(SymbolEqualityComparer.Default);

        if (owner.Members != null)
        {
            foreach (KeyValuePair<ISymbol, LoopState> pair in owner.Members.Members)
            {
                members[pair.Key] = pair.Value.From(pair.Key);
            }
        }

        return members;
    }

    /// <summary>
    ///     The arguments of a call, each with the parameter of the definition
    ///     that gets it, by name or by position as the compiler binds them. The
    ///     receiver of an extension method is the argument of its first parameter.
    /// </summary>
    private IReadOnlyList<Binding> Bind(
        IMethodSymbol definition,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ExpressionSyntax? extensionReceiver)
    {
        List<Binding> bindings = [];
        int offset = 0;

        if (extensionReceiver != null && definition.Parameters.Length > 0)
        {
            bindings.Add(
                new Binding(
                    parameter: definition.Parameters[0],
                    argument: extensionReceiver,
                    state: this.Eval(extensionReceiver)));

            offset = 1;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                is { } parameter)
            {
                bindings.Add(
                    new Binding(
                        parameter: parameter,
                        argument: arguments[i].Expression,
                        state: this.Eval(arguments[i].Expression)));
            }
        }

        return bindings;
    }

    private IReadOnlyList<Binding> BindByPosition(
        ImmutableArray<IParameterSymbol> parameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        List<Binding> bindings = [];

        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            bindings.Add(
                new Binding(
                    parameter: parameters[i],
                    argument: arguments[i].Expression,
                    state: this.Eval(arguments[i].Expression)));
        }

        return bindings;
    }

    /// <summary>The body of a method or a local function of this compilation.</summary>
    private static SyntaxNode? BodyOf(IMethodSymbol definition) =>
        definition.DeclaringSyntaxReferences
            .Select(static reference =>
                reference.GetSyntax() switch
                {
                    MethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
                    LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                    _ => null
                })
            .FirstOrDefault(static body => body != null);

    /// <summary>The body of the getter of a property, when the getter has one.</summary>
    private static SyntaxNode? GetterOf(IPropertySymbol definition) =>
        definition.DeclaringSyntaxReferences
            .Select(static reference =>
                reference.GetSyntax() switch
                {
                    PropertyDeclarationSyntax { ExpressionBody: { } arrow } => arrow.Expression,
                    PropertyDeclarationSyntax { AccessorList: { } accessors } =>
                        accessors.Accessors
                            .Where(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                            .Select(static accessor =>
                                accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression)
                            .FirstOrDefault(static body => body != null),
                    _ => null
                })
            .FirstOrDefault(static body => body != null);

    private static bool IsStatic(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier =>
            modifier.IsKind(SyntaxKind.StaticKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword));

    /// <summary>
    ///     The delegate that an invocation calls: <c>f</c> in <c>f(x)</c>, in
    ///     <c>f.Invoke(x)</c>, and in <c>f?.Invoke(x)</c>.
    /// </summary>
    private static ExpressionSyntax DelegateOf(InvocationExpressionSyntax invocation, SemanticModel model) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access
                when model.GetSymbolInfo(access).Symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } =>
                access.Expression,
            MemberBindingExpressionSyntax binding when ConditionalReceiver(binding) is { } receiver => receiver,
            _ => invocation.Expression
        };

    private static string NameOf(ExpressionSyntax target) =>
        target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => target.ToString()
        };

    /// <summary>
    ///     A method with no rule here gives its result the state of its inputs,
    ///     where the result type can hold one. This is the conservative default.
    /// </summary>
    private static LoopState DefaultLibraryResult(SodaFlowSymbols symbols, IMethodSymbol method, LoopState inputs)
    {
        ITypeSymbol returnType = method.ReturnType;

        if (symbols.IsStreamType(returnType))
        {
            return new LoopState(
                pending: false,
                linked: inputs.Linked,
                contentPending: inputs.ContentPending,
                contentLinked: inputs.ContentLinked);
        }

        return symbols.IsCarrierType(returnType) ? inputs : LoopState.None;
    }

    /// <summary>
    ///     What a body returns: its one expression, or the combined state of its
    ///     return statements. The evaluator declares the locals at the first level
    ///     of a block in order. This is for a function that runs after the close,
    ///     which the walk does not read.
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
                && this.region.Symbols.IsSodaFlow(readMethod)
                && this.Eval(access.Expression).Pending)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The state of an argument of a library operator. A function that the
    ///     operator keeps for a firing after the close gives the operator no state.
    /// </summary>
    private LoopState EvalArgument(ExpressionSyntax argument, bool runsNow)
    {
        if (argument is AnonymousFunctionExpressionSyntax lambda)
        {
            return runsNow
                ? this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray<LoopState>.Empty)
                : LoopState.None;
        }

        LoopState state = this.Eval(argument);

        // The functions that a name can be are not an input of the operator.
        return state.Functions.IsEmpty ? state : LoopState.None;
    }

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
        ImmutableDictionary<ISymbol, LoopState>.Builder parameters = this.bound.ToBuilder();
        IReadOnlyList<ParameterSyntax> written = ParametersOf(lambda);

        for (int i = 0; i < written.Count && i < boundParameters.Length; i++)
        {
            if (model.GetDeclaredSymbol(written[i]) is { } symbol)
            {
                parameters[symbol] = boundParameters[i];
            }
        }

        // The lambda is part of this code: it reads the same locals and the same members.
        LoopStateEvaluator inner =
            boundParameters.IsEmpty
                ? this
                : new LoopStateEvaluator(
                    region: this.region,
                    bound: parameters.ToImmutable(),
                    depth: this.depth,
                    frame: this.Frame,
                    locals: this.locals,
                    thisMembers: this.thisMembers);

        return inner.EvalBody(expressionBody: lambda.ExpressionBody, block: lambda.Block);
    }

    // Arrays, and not a collection expression. A collection expression here takes the natural type
    // of the other arms, SeparatedSyntaxList, and that conversion gives an empty list.
    public static IReadOnlyList<ParameterSyntax> ParametersOf(AnonymousFunctionExpressionSyntax lambda) =>
        lambda switch
        {
            SimpleLambdaExpressionSyntax s => new[] { s.Parameter },
            ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.ToArray(),
            AnonymousMethodExpressionSyntax { ParameterList: { } list } => list.Parameters.ToArray(),
            _ => Array.Empty<ParameterSyntax>()
        };

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
        this.region.Symbols.StateOfType(this.region.ModelFor(expression).GetTypeInfo(expression).Type);

    private static bool LinkedStreamInput(
        SodaFlowSymbols symbols,
        LoopState receiverState,
        ITypeSymbol? receiverType,
        IReadOnlyList<LoopState> argumentStates,
        IReadOnlyList<ITypeSymbol?> argumentTypes)
    {
        bool linked =
            receiverState.Linked && (receiverType == null || !symbols.IsCellOrBehaviorType(receiverType));

        for (int i = 0; i < argumentStates.Count; i++)
        {
            linked |= argumentStates[i].Linked && !symbols.IsCellOrBehaviorType(argumentTypes[i]);
        }

        return linked;
    }

    /// <summary>
    ///     An <c>AsCell()</c> or <c>AsBehavior()</c> on the argument, or on a
    ///     value it is derived from, is the assertion that nothing reads it before the
    ///     close.
    /// </summary>
    public static bool IsAsserted(ExpressionSyntax argument)
    {
        ExpressionSyntax current = argument;

        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
        {
            if (access.Name.Identifier.ValueText is "AsCell" or "AsBehavior")
            {
                return true;
            }

            current = access.Expression;
        }

        return false;
    }

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

    /// <summary>
    ///     The expression that receives a call. The call can be
    ///     <c>receiver.M()</c> or <c>receiver?.M()</c>. For an extension method of
    ///     the library, it can also be <c>Extensions.M(receiver)</c>.
    /// </summary>
    public static ExpressionSyntax? ReceiverOf(
        SodaFlowSymbols symbols,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.ReducedFrom != null || !method.IsStatic)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax access => access.Expression,
                MemberBindingExpressionSyntax => ConditionalReceiver(invocation),
                _ => null
            };
        }

        // A static call. Only an operator of the library has a rule that asks for its receiver.
        if (!method.IsExtensionMethod || !symbols.IsSodaFlow(method))
        {
            return null;
        }

        return invocation.ArgumentList.Arguments
            .Where((argument, position) =>
                SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position) is { Ordinal: 0 })
            .Select(static argument => argument.Expression)
            .FirstOrDefault();
    }

    /// <summary>
    ///     The expression before the <c>?.</c> of a conditional access, for a
    ///     member or a call that comes after it.
    /// </summary>
    public static ExpressionSyntax? ConditionalReceiver(SyntaxNode afterTheMark) =>
        afterTheMark.Ancestors()
            .OfType<ConditionalAccessExpressionSyntax>()
            .FirstOrDefault(conditional => conditional.WhenNotNull.Span.Contains(afterTheMark.Span))
            ?.Expression;

    /// <summary>One argument of a call, with the parameter of the callee that gets it.</summary>
    private readonly struct Binding(in IParameterSymbol parameter, in ExpressionSyntax argument, in LoopState state)
    {
        public IParameterSymbol Parameter { get; } = parameter;

        public ExpressionSyntax Argument { get; } = argument;

        public LoopState State { get; } = state;
    }
}
