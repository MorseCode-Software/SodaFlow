using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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
    private const int MaxInlineDepth = 3;

    private static readonly ImmutableArray<string> Suffixes = ImmutableArray.Create(item1: "Impl", item2: "Internal");

    private static readonly ImmutableDictionary<ISymbol, LoopState> NoSymbols =
        ImmutableDictionary<ISymbol, LoopState>.Empty.WithComparers(SymbolEqualityComparer.Default);

    // The locals and parameters that an assignment gave a new state, which replaced the first one.
    private readonly HashSet<ISymbol> assigned;
    private readonly ImmutableDictionary<ISymbol, LoopState> bound;
    private readonly int depth;
    private readonly Dictionary<SyntaxNode, Followed?> followed = [];

    // The calls that the code writes with no call syntax. The key is the node and the member that
    // it calls. One node can make more than one such call, as an expression with an operator and
    // a conversion does.
    private readonly Dictionary<(SyntaxNode Node, ISymbol Member), Followed?> implied = new(ImpliedCalls.Instance);
    private readonly Dictionary<SyntaxNode, CalleeKind> kinds = [];
    private readonly Dictionary<ISymbol, LoopState> locals;
    private readonly Dictionary<ExpressionSyntax, LoopState> memo = [];

    // The queries that this code makes.
    private readonly Dictionary<SyntaxNode, QueryValue> queries = [];

    // True for an evaluator that reads code that does not run now, to learn what that code reads.
    // It has copies of the locals and the members, and it does not set a field of the region.
    private readonly bool quiet;
    private readonly LoopRegion region;
    private readonly Dictionary<ISymbol, LoopState> thisMembers;

    public LoopStateEvaluator(LoopRegion region)
        : this(
            region: region,
            bound: NoSymbols,
            depth: 0,
            frame: new LoopFrame(),
            locals: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            assigned: new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            thisMembers: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            quiet: false)
    {
    }

    private LoopStateEvaluator(
        LoopRegion region,
        ImmutableDictionary<ISymbol, LoopState> bound,
        int depth,
        LoopFrame frame,
        Dictionary<ISymbol, LoopState> locals,
        HashSet<ISymbol> assigned,
        Dictionary<ISymbol, LoopState> thisMembers,
        bool quiet)
    {
        this.region = region;
        this.bound = bound;
        this.depth = depth;
        this.Frame = frame;
        this.locals = locals;
        this.assigned = assigned;
        this.thisMembers = thisMembers;
        this.quiet = quiet;
    }

    /// <summary>
    ///     An evaluator for the same code that changes nothing that the walk
    ///     keeps. It reads a function that does not run now, to learn if the
    ///     function reads a pending value.
    /// </summary>
    private LoopStateEvaluator Quiet() =>
        this.quiet
            ? this
            : new LoopStateEvaluator(
                region: this.region,
                bound: this.bound,
                depth: this.depth,
                frame: this.Frame,
                locals: new Dictionary<ISymbol, LoopState>(
                    dictionary: this.locals,
                    comparer: SymbolEqualityComparer.Default),
                assigned: new HashSet<ISymbol>(collection: this.assigned, comparer: SymbolEqualityComparer.Default),
                thisMembers: new Dictionary<ISymbol, LoopState>(
                    dictionary: this.thisMembers,
                    comparer: SymbolEqualityComparer.Default),
                quiet: true);

    /// <summary>The frame of the code that this evaluator reads.</summary>
    public LoopFrame Frame { get; }

    /// <summary>
    ///     Records a query for the frame of this code. The call or the query
    ///     expression makes that query. An evaluator that reads code which does
    ///     not run now records none. That query is in no state, thus a walk of
    ///     it changes no result.
    /// </summary>
    public void Wait(SyntaxNode call)
    {
        if (!this.quiet)
        {
            this.Frame.Wait(this.QueryAt(call));
        }
    }

    /// <summary>The query that a call or a query expression of this code makes.</summary>
    private QueryValue QueryAt(SyntaxNode call)
    {
        if (!this.queries.TryGetValue(key: call, value: out QueryValue? query))
        {
            query = new QueryValue(call: call, definer: this);
            this.queries[call] = query;
        }

        return query;
    }

    /// <summary>
    ///     True for a call that makes a sequence and runs nothing: a query method
    ///     of the platform, or an iterator of this compilation. The code of the
    ///     function, of the key, or of the iterator runs when something enumerates
    ///     the result.
    /// </summary>
    public bool Waits(InvocationExpressionSyntax call, IMethodSymbol method) =>
        this.Classify(call: call, method: method) switch
        {
            CalleeKind.Mover => SodaFlowSymbols.MakesQuery(method),
            CalleeKind.Body => IsIterator(this.DefinitionOf(invocation: call, method: method)),
            _ => false
        };

    private static bool IsIterator(IMethodSymbol definition) =>
        SodaFlowSymbols.IsEnumerated(definition.ReturnType)
        && BodyOf(definition) is BlockSyntax block
        && block.DescendantNodes(
                descendIntoChildren: static node =>
                    node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(static node => node is YieldStatementSyntax);

    /// <summary>
    ///     True when the code is a member of an object that has a member with a
    ///     state. Then, a name with no owner can be a property of that object.
    /// </summary>
    public bool HasThisMembers => this.thisMembers.Count > 0;

    /// <summary>
    ///     Records the state of a local at its declaration, or at an assignment
    ///     that does not always run. The local then has the two states together.
    /// </summary>
    public void Declare(ISymbol local, LoopState state) =>
        this.locals[local] =
            this.locals.TryGetValue(key: local, value: out LoopState? existing) ? existing.Join(state) : state;

    /// <summary>
    ///     Records an assignment that always runs. The new state replaces the
    ///     state that the local or the parameter had.
    /// </summary>
    public void Assign(ISymbol local, LoopState state)
    {
        this.locals[local] = state;
        this.assigned.Add(local);
    }

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
            && this.region.SymbolOf(owner) is { } holder
                and (ILocalSymbol or IParameterSymbol))
        {
            LoopState known =
                this.locals.TryGetValue(key: holder, value: out LoopState? existing) ? existing : LoopState.None;

            this.locals[holder] =
                known.WithMembers((known.Members ?? Construction.Empty).With(member: member, state: state));

            return;
        }

        this.SetField(field: member, state: state);
    }

    private void SetField(ISymbol field, LoopState state)
    {
        if (!this.quiet)
        {
            this.region.SetField(field: field, state: state);
        }
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

        // The object contains the value. It is not the value.
        state = state.AsContainer;

        switch (this.region.SymbolOf(target))
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
                this.SetField(field: field, state: state);
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
    /// <summary>
    ///     The state of an expression as one object. A collection has the members
    ///     of its elements in its state, and the collection is not one of those
    ///     objects. Thus, a member that the code calls on the collection is not a
    ///     member of an element.
    /// </summary>
    /// <remarks>
    ///     The type of the expression tells the two apart, when an element cannot
    ///     have that type. A type such as <c>object</c> tells nothing. Then, the
    ///     state says if the value is a container.
    /// </remarks>
    public LoopState Self(ExpressionSyntax? written)
    {
        LoopState state = this.Eval(written);

        if (written == null || state.Members == null)
        {
            return state;
        }

        ITypeSymbol? type = this.TypeBeforeObject(written);

        return state.Members.CanBe(type) && !(state.IsContainer && Construction.SaysNothing(type))
            ? state
            : state.WithMembers(null);
    }

    /// <summary>
    ///     The type of an expression, or the type that it has before a cast to
    ///     <c>object</c>. That cast keeps the value and hides what it is.
    /// </summary>
    private ITypeSymbol? TypeBeforeObject(ExpressionSyntax written)
    {
        ITypeSymbol? type = this.region.TypeOf(written);

        while (type?.SpecialType == SpecialType.System_Object)
        {
            ExpressionSyntax? inner =
                written switch
                {
                    ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                    CastExpressionSyntax cast => cast.Expression,
                    BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } conversion => conversion.Left,
                    _ => null
                };

            if (inner == null)
            {
                break;
            }

            written = inner;
            type = this.region.TypeOf(written);
        }

        return type;
    }

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
            // A cast that an operator of this compilation makes gives what the operator returns.
            CastExpressionSyntax c => this.FollowExpression(c) is { } converted
                ? converted.State.Join(this.TypeState(expression))
                : this.Eval(c.Expression),
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
            TupleExpressionSyntax t => this.EvalTuple(t),
            AnonymousObjectCreationExpressionSyntax a => this.EvalAnonymous(a),
            // A collection contains its elements, and is not one of them.
            CollectionExpressionSyntax c => this.JoinAll(
                    c.Elements.Select(static e =>
                        e switch
                        {
                            ExpressionElementSyntax x => x.Expression,
                            SpreadElementSyntax x => x.Expression,
                            _ => null
                        }))
                .AsContainer,
            ArrayCreationExpressionSyntax { Initializer: { } init } => this.JoinAll(init.Expressions).AsContainer,
            ImplicitArrayCreationExpressionSyntax a => this.JoinAll(a.Initializer.Expressions).AsContainer,
            InitializerExpressionSyntax i => this.JoinAll(i.Expressions).AsContainer,
            // A query expression has the queries of its first source, and is one more.
            QueryExpressionSyntax q => this.Eval(q.FromClause.Expression).OnlyQueries.WithQuery(this.QueryAt(q)),
            // An indexer of this compilation gives what its getter returns. An element of a
            // collection has the state of the collection.
            ElementAccessExpressionSyntax e => this.FollowExpression(e) is { } indexed
                ? indexed.State.Join(this.TypeState(expression))
                : this.Eval(e.Expression).AsElement.Join(this.TypeState(expression)),
            // A lambda is a value. Its code runs when something calls it.
            AnonymousFunctionExpressionSyntax function =>
                LoopState.OfFunction(new FunctionValue(declaration: function, definer: this, receiver: null)),
            // The object that the code is in, with the members that the evaluator knows.
            ThisExpressionSyntax when this.thisMembers.Count > 0 =>
                LoopState.None.WithMembers(
                    new Construction(
                        members: this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default),
                        type: null)),
            InvocationExpressionSyntax inv => this.EvalInvocation(inv),
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => this.EvalCreation(expression),
            IdentifierNameSyntax
                or MemberAccessExpressionSyntax
                or MemberBindingExpressionSyntax
                or GenericNameSyntax => this.EvalName(expression),
            // An operator of this compilation gives what it returns.
            _ => this.FollowExpression(expression) is { } result
                ? result.State.Join(this.TypeState(expression))
                : this.TypeState(expression)
        };

    /// <summary>
    ///     A tuple has the state of all its elements together, for code that uses
    ///     it as one value. It also has the state of each element as a member.
    ///     Thus, a read of a plain element is not a read of a pending one.
    /// </summary>
    private LoopState EvalTuple(TupleExpressionSyntax tuple)
    {
        LoopState all = this.JoinAll(tuple.Arguments.Select(static argument => argument.Expression));
        TypeInfo info = this.region.ModelFor(tuple).GetTypeInfo(tuple);

        if ((info.Type ?? info.ConvertedType) is not INamedTypeSymbol { IsTupleType: true } type
            || type.TupleElements.Length != tuple.Arguments.Count)
        {
            return all;
        }

        Construction elements = Construction.Empty;

        for (int i = 0; i < tuple.Arguments.Count; i++)
        {
            elements = elements.With(member: type.TupleElements[i], state: this.Eval(tuple.Arguments[i].Expression));
        }

        return all.WithMembers(elements);
    }

    /// <summary>
    ///     An anonymous object has the state of each value that it gets as the
    ///     state of that property.
    /// </summary>
    private LoopState EvalAnonymous(AnonymousObjectCreationExpressionSyntax creation)
    {
        SemanticModel model = this.region.ModelFor(creation);
        Construction properties = Construction.Empty;

        foreach (AnonymousObjectMemberDeclaratorSyntax declarator in creation.Initializers)
        {
            if (model.GetDeclaredSymbol(declarator) is { } property)
            {
                properties = properties.With(member: property, state: this.Eval(declarator.Expression));
            }
        }

        return LoopState.None.WithMembers(properties);
    }

    private LoopState EvalName(ExpressionSyntax expression)
    {
        ISymbol? symbol = this.region.SymbolOf(expression);

        switch (symbol)
        {
            case ILocalSymbol or IParameterSymbol:
            {
                if (SymbolEqualityComparer.Default.Equals(x: symbol, y: this.region.Placeholder))
                {
                    return this.region.PlaceholderState;
                }

                LoopState named =
                    !this.assigned.Contains(symbol)
                    && this.bound.TryGetValue(key: symbol, value: out LoopState? boundState)
                        ? boundState
                        : LoopState.None;

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
                if (symbol is IPropertySymbol property)
                {
                    // A property with a getter has the state of what the getter returns. Only a
                    // property of this compilation has a getter that the evaluator can read. The
                    // value of a Lazy is what its function returns.
                    if ((property.DeclaringSyntaxReferences.Length > 0 || SodaFlowSymbols.IsLazyValue(property))
                        && this.Follow(expression) is { } getter)
                    {
                        return getter.State.Join(this.TypeState(expression));
                    }

                    // A property of the library that reads the current value gives the value that
                    // its owner holds.
                    if (SodaFlowSymbols.ReadMembers.Contains(property.Name)
                        && this.region.Symbols.IsSodaFlow(property))
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
                    // wherever the object came from: a local, a hold, or a lambda parameter. The
                    // code can read the member through an interface of the object. A tuple with
                    // no state for the element is a tuple that the evaluator did not see made.
                    // A pair, a task, a nullable, or an enumerator of the platform has the members
                    // of the object that it contains. Its own property is not one of them.
                    if (owner.Members != null
                        && owner.Members.CanOwn(symbol)
                        && (!symbol.ContainingType.IsTupleType || owner.Members.Has(symbol)))
                    {
                        return owner.Members.StateOf(owner.Members.Resolve(symbol))
                            .FromIfUnknown(owner.Origin)
                            .Join(this.TypeState(expression));
                    }

                    // Otherwise, a member of a pending value is pending. Such a member is the cell
                    // behind a bindable, one element of a tuple from a loop block, or the values of
                    // a dictionary. A member that is not a sequence is one of the values that the
                    // owner contains, as the value of a pair is.
                    ITypeSymbol memberType = symbol is IPropertySymbol p ? p.Type : ((IFieldSymbol)symbol).Type;

                    // SyncRoot of a collection is an object to lock, and not a value in it.
                    if (symbol.ContainingType.IsTupleType
                        || (symbol.Name != "SyncRoot"
                            && this.region.Symbols.CanHoldState(
                                type: memberType,
                                compilation: this.region.Assembly)))
                    {
                        fromOwner = fromOwner.Join(SodaFlowSymbols.IsSequence(memberType) ? owner : owner.AsElement);
                    }
                    else if (SodaFlowSymbols.CanHoldQuery(memberType))
                    {
                        fromOwner = fromOwner.Join(owner.OnlyQueries);
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
        if (this.region.SymbolOf(invocation) is not IMethodSymbol method)
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

            result =
                result
                    .Join(this.Follow(invocation)?.State ?? LoopState.None)
                    .Join(this.region.Symbols.StateOfType(method.ReturnType));

            // A query and the result of an iterator keep their code for the time when
            // something enumerates them.
            return this.Waits(call: invocation, method: method)
                ? result.WithQuery(this.QueryAt(invocation))
                : result;
        }

        ExpressionSyntax? receiver =
            ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);

        LoopState receiverState = this.Eval(receiver);
        ITypeSymbol? receiverType = receiver == null ? null : this.region.TypeOf(receiver);
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        List<LoopState> argumentStates = [];
        List<ITypeSymbol?> argumentTypes = [];
        bool sortsByPendingOrder = false;

        // In a static call of an extension method, the receiver is one of the arguments. It is not
        // also an argument of the operator.
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression == receiver)
            {
                continue;
            }

            IParameterSymbol? parameter = ParameterFor(method: method, argument: arguments[i], position: i);

            // A comparer is a function of the operator, and not a value that the result holds. The
            // members of a comparer object are not members of the result.
            LoopState argumentState =
                SodaFlowSymbols.IsComparer(parameter?.Type)
                    ? LoopState.None
                    : this.EvalArgument(
                        argument: arguments[i].Expression,
                        runsNow: OperatorTable.RunsFunction(method: method, parameter: parameter));

            argumentStates.Add(argumentState);
            argumentTypes.Add(this.region.TypeOf(arguments[i].Expression));

            sortsByPendingOrder |=
                argumentState.ContentPending
                && OperatorTable.IsOrderCell(symbols: this.region.Symbols, parameter: parameter);
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
                // A read before the close gives the initial value. The value of a firing comes
                // after the close.
                OperatorRule.Hold => new LoopState(
                    pending: false,
                    linked: receiverState.Linked,
                    contentPending: false,
                    contentLinked: false).WithContent(receiverState.Content.AfterTheClose.Join(firstArgument)),
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
                OperatorRule.DeferLazy => new LoopState(
                    pending: firstArgument.Pending,
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

        // The function of a map, a lift, or a view computes the value of the result at the first
        // read of it. A function that reads a pending value makes that value pending. A view that
        // sorts by the order in a cell runs the function of that order.
        if (!state.Pending
            && (sortsByPendingOrder
                || (OperatorTable.ComputesValueWithFunction(
                        symbols: this.region.Symbols,
                        method: method,
                        receiverType: receiverType)
                    && this.CanUseState(invocation: invocation, given: inputs.Content)
                    && this.Quiet()
                        .FunctionReadsPending(invocation: invocation, method: method, given: inputs.Content))))
        {
            state = state.WithPending(true);
        }

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

        if (initializer != null)
        {
            state = this.WithInitializer(state: state, initializer: initializer);
        }

        // An object of code that cannot know the library is not an object of this compilation.
        // Thus, it contains the objects that have the members, and is not one of them.
        return this.region.SymbolOf(creation) is IMethodSymbol constructor
               && this.Classify(call: creation, method: constructor) == CalleeKind.Mover
            ? state.AsContainer
            : state;
    }

    /// <summary>
    ///     The state of an object after an initializer. An object initializer or
    ///     a with expression sets members, and the state of each replaces the state
    ///     from the constructor. A collection initializer adds elements, and the
    ///     collection gets their states also.
    /// </summary>
    private LoopState WithInitializer(LoopState state, InitializerExpressionSyntax initializer)
    {
        foreach (ExpressionSyntax expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment
                && this.region.SymbolOf(name) is { } member and (IPropertySymbol or IFieldSymbol))
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
        if (!this.kinds.TryGetValue(key: call, value: out CalleeKind kind))
        {
            kind = this.ClassifyUncached(call: call, method: method);
            this.kinds[call] = kind;
        }

        return kind;
    }

    private CalleeKind ClassifyUncached(SyntaxNode call, IMethodSymbol method)
    {
        if (this.region.Symbols.IsSodaFlow(method))
        {
            return CalleeKind.Library;
        }

        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            return this.Follow(call) != null ? CalleeKind.Body : CalleeKind.Unknown;
        }

        IMethodSymbol definition =
            call is InvocationExpressionSyntax invocation
                ? this.DefinitionOf(invocation: invocation, method: method)
                : method.OriginalDefinition;

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

        // The receiver of an extension method is the argument of its first parameter. A callee
        // that enumerates a sequence of this compilation gets its elements also.
        LoopState seed =
            !withReceiver
                ? LoopState.None
                : method.ReducedFrom == null
                    ? this.Eval(receiver)
                    : this.Eval(receiver)
                        .Join(this.Enumerated(written: receiver, parameter: definition.Parameters[0].Type));

        return this.MovedArguments(
            definition: definition,
            arguments: invocation.ArgumentList.Arguments,
            offset: method.ReducedFrom != null ? 1 : 0,
            seed: seed);
    }

    private LoopState MovedArguments(
        IMethodSymbol definition,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int offset,
        LoopState seed)
    {
        // A callee that returns a delegate gives back the function that it got, as a wrapper
        // does. Then, the function is a value that it moves. A Lazy keeps the function that its
        // constructor gets. A method that returns a Lazy gives its function to that Lazy.
        // A comparer from a function, as from Comparer.Create, has that function.
        bool keepsFunctions =
            definition.ReturnType.TypeKind == TypeKind.Delegate
            || SodaFlowSymbols.IsLazy(definition.ReturnType)
            || SodaFlowSymbols.IsComparer(definition.ReturnType)
            || (definition.MethodKind == MethodKind.Constructor && SodaFlowSymbols.IsLazy(definition.ContainingType));

        // A function for a delegate parameter is code for the callee to run, and not a value that
        // it moves.
        return arguments
            .Select((argument, position) =>
                (Written: argument.Expression,
                    Parameter: SodaFlowSymbols.ParameterOf(
                        callee: definition,
                        argument: argument,
                        position: position + offset)))
            .Where(given => keepsFunctions || given.Parameter is not { Type.TypeKind: TypeKind.Delegate })
            .Aggregate(
                seed: seed,
                func: (moved, given) =>
                    moved.Join(this.Eval(given.Written))
                        .Join(this.Enumerated(written: given.Written, parameter: given.Parameter?.Type)));
    }

    /// <summary>
    ///     The result of a call to code that cannot know the library, where the
    ///     type of the result can hold a state. The result has the state of the
    ///     values that the callee got, and of what a lambda that it runs returns.
    /// </summary>
    private LoopState MovedResult(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        // A sequence of numbers has no state, but it can be or contain a query. That query is
        // one that the callee got, or one that a function of the callee returns.
        bool holdsState =
            this.region.Symbols.CanHoldState(type: method.ReturnType, compilation: this.region.Assembly);

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        // Reflection makes an object or calls a method that the analyzer cannot follow. Its
        // result is not one of the values that it got.
        if ((!holdsState && !SodaFlowSymbols.CanHoldQuery(method.ReturnType))
            || SodaFlowSymbols.IsReflection(definition))
        {
            return LoopState.None;
        }

        LoopState moved = this.Moved(invocation: invocation, method: method, withReceiver: true);
        LoopState result = moved;
        LoopState element = moved.AsElement;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression is AnonymousFunctionExpressionSyntax lambda
                && RunsFunction(
                    kind: CalleeKind.Mover,
                    method: method,
                    parameter: ParameterFor(method: method, argument: arguments[i], position: i)))
            {
                result =
                    result.Join(
                        this.EvalLambdaResult(
                            lambda: lambda,
                            boundParameters: ParametersOf(lambda).Select(_ => element).ToImmutableArray()));
            }
        }

        if (!holdsState)
        {
            return result.OnlyQueries;
        }

        // A callee that has a sequence gives one of its values, as First does. A callee with no
        // sequence gives back what it got.
        bool takesOneOut =
            this.region.Symbols.TakesASequence(definition)
            || (!definition.IsStatic && SodaFlowSymbols.IsEnumerated(definition.ContainingType));

        return Returned(state: result, type: method.ReturnType, takesOneOut: takesOneOut);
    }

    /// <summary>
    ///     The state of what a method of code that cannot know the library
    ///     returns, from the state of the values that it got. A result that is a
    ///     sequence contains those values. Each other result can be one of them, as
    ///     the result of <c>First</c> is.
    /// </summary>
    /// <param name="state">The state of the values that the method got.</param>
    /// <param name="type">The type of the result.</param>
    /// <param name="takesOneOut">
    ///     True when the receiver or a parameter of the method is a sequence. A
    ///     result whose type says nothing is then one value of that sequence. False
    ///     for a method that can only give back what it got. A list that it got as
    ///     <c>object</c> stays a list.
    /// </param>
    private static LoopState Returned(LoopState state, ITypeSymbol type, bool takesOneOut)
    {
        if (!SodaFlowSymbols.IsSequence(type))
        {
            return takesOneOut || !Construction.SaysNothing(type) ? state.AsElement : state;
        }

        // A method can give back a sequence that it got, as AsEnumerable does. That sequence can
        // be an object of this compilation, which has its own members.
        return state.Members is { } members && (!members.HasType || members.Is(type)) ? state : state.AsContainer;
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
    ///     value that the callee keeps. A read of <c>Value</c> runs the function
    ///     that a <c>Lazy</c> has, which <see cref="FollowLazy" /> follows. A
    ///     comparer is a function with a different type. A method runs one that it
    ///     gets, and a method that returns one keeps the function that makes it.
    /// </remarks>
    public static bool RunsFunction(CalleeKind kind, IMethodSymbol method, IParameterSymbol? parameter) =>
        kind switch
        {
            CalleeKind.Library => OperatorTable.RunsFunction(method: method, parameter: parameter),
            CalleeKind.Body => false,
            _ => method.MethodKind != MethodKind.Constructor
                 && parameter != null
                 && (parameter.Type.TypeKind == TypeKind.Delegate || SodaFlowSymbols.IsComparer(parameter.Type))
                 && method.ReturnType.TypeKind != TypeKind.Delegate
                 && !SodaFlowSymbols.IsLazy(method.ReturnType)
                 && !SodaFlowSymbols.IsComparer(method.ReturnType)
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
    private LoopState MovedInto(SyntaxNode creation)
    {
        BaseArgumentListSyntax? arguments =
            creation switch
            {
                ObjectCreationExpressionSyntax written => written.ArgumentList,
                ImplicitObjectCreationExpressionSyntax implied => implied.ArgumentList,
                _ => null
            };

        return arguments != null
               && this.region.SymbolOf(creation) is IMethodSymbol constructor
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
        if (this.region.SymbolOf(invocation) is not IMethodSymbol method)
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        // A call of a delegate runs the functions that the delegate can be.
        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            ExpressionSyntax target = this.DelegateOf(invocation);
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

        if (this.region.Symbols.IsSodaFlow(method))
        {
            return OperatorTable.FunctionsInTheCall.Contains(OperatorName(method))
                ? this.FollowGivenFunctions(invocation: invocation, method: method)
                : null;
        }

        IMethodSymbol definition = this.DefinitionOf(invocation: invocation, method: method);

        if (BodyOf(definition) is not { } body)
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
    ///     The definition of the method that a call runs. The call can be through
    ///     an interface or a base type, on an object whose constructor the evaluator
    ///     followed. Then, it runs the method of the type of that object.
    /// </summary>
    private IMethodSymbol DefinitionOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return method is { IsStatic: false, ReducedFrom: null }
               && ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method) is { } receiver
               && this.Self(receiver).Members is { } members
               && members.Resolve(definition) is IMethodSymbol actual
            ? actual.OriginalDefinition
            : definition;
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

            IParameterSymbol? parameter = ParameterFor(method: method, argument: arguments[i], position: i);

            if (written is AnonymousFunctionExpressionSyntax
                || !RunsFunction(kind: kind, method: method, parameter: parameter))
            {
                continue;
            }

            LoopState functions = this.Eval(written);
            ImmutableArray<FunctionValue> callables = Callables(state: functions, parameter: parameter);

            if (callables.IsEmpty)
            {
                continue;
            }

            LoopState given =
                kind == CalleeKind.Mover
                    ? this.Moved(invocation: invocation, method: method, withReceiver: true).AsElement
                    : LoopState.None;

            all =
                callables.Aggregate(
                    seed: all,
                    func: (followed, function) =>
                        Both(
                            first: followed,
                            second: this.FollowFunction(
                                call: invocation,
                                name: NameOf(written),
                                function: function,
                                target: functions,
                                arguments: default,
                                each: (written, given))));
        }

        return all;
    }

    /// <summary>
    ///     The functions that a callee can run through an argument. They are the
    ///     functions that the argument can be. For a comparer that is an object of
    ///     this compilation, they are also the methods of the comparer, which the
    ///     callee calls through the interface.
    /// </summary>
    private static ImmutableArray<FunctionValue> Callables(LoopState state, IParameterSymbol? parameter) =>
        state.Members == null || !SodaFlowSymbols.IsComparer(parameter?.Type)
            ? state.Functions
            : state.Functions.AddRange(
                state.Members.Implementations(SodaFlowSymbols.IsComparerInterface)
                    .Select(static method => method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax())
                    .OfType<MethodDeclarationSyntax>()
                    .Select(declaration =>
                        new FunctionValue(declaration: declaration, definer: null, receiver: state.Members)));

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
        SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        functions.Aggregate(
            seed: (Followed?)null,
            func: (all, function) =>
                Both(
                    first: all,
                    second: this.FollowFunction(
                        call: call,
                        name: name,
                        function: function,
                        target: target,
                        arguments: arguments,
                        each: null)));

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
            each is (var written, var given)
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

    private Followed? FollowCreation(SyntaxNode creation, BaseArgumentListSyntax? arguments) =>
        this.region.SymbolOf(creation) is IMethodSymbol constructor
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
    private Followed? FollowChain(SyntaxNode chain, BaseArgumentListSyntax? arguments)
    {
        if (this.region.SymbolOf(chain) is not IMethodSymbol constructor
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

        IReadOnlyList<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving = walker.RunWaitingApart();

        return this.Leave(
            call: call,
            name: definition.ContainingType.Name,
            bindings: bindings,
            inner: inner,
            lexical: false,
            receiver: null,
            target: null,
            result: LoopState.None.WithMembers(
                new Construction(
                    members: members.ToImmutableDictionary(SymbolEqualityComparer.Default),
                    type: definition.ContainingType)),
            found: found,
            leaving: leaving);

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
    private Followed? FollowGetter(SyntaxNode access)
    {
        if (this.region.SymbolOf(access) is not IPropertySymbol written || this.region.Symbols.IsSodaFlow(written))
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

        if (SodaFlowSymbols.IsLazyValue(written))
        {
            return owner == null
                ? null
                : this.FollowLazy(access: access, written: owner, lazy: this.Eval(owner), value: written.Type);
        }

        // A property that the code reads through an interface is the property of the object.
        IPropertySymbol property =
            this.Self(owner).Members?.Resolve(written) as IPropertySymbol ?? written;

        if (GetterOf(property.OriginalDefinition) is not { } body)
        {
            return null;
        }

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
    ///     A call that the code writes with no call syntax. The compiler binds an
    ///     operator, a conversion, an accessor, a <c>Deconstruct</c>, and some
    ///     other forms to a member. The evaluator reads that member as a call of
    ///     it. The result is null for a member with no body to read.
    /// </summary>
    /// <param name="call">The node that makes the call. A report about a member is at this node.</param>
    /// <param name="member">The method or the accessor that the node calls.</param>
    /// <param name="owner">
    ///     The state of the object, for an instance member. Null for the object
    ///     that this code is in.
    /// </param>
    /// <param name="arguments">
    ///     The expressions that the member gets, in the order of its parameters,
    ///     each with its state.
    /// </param>
    public Followed? FollowImplied(
        SyntaxNode call,
        IMethodSymbol member,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        if (this.implied.TryGetValue(key: (call, member), value: out Followed? known))
        {
            return known;
        }

        // The member of the object, when the code names one of an interface or a base type.
        IMethodSymbol definition =
            (owner?.Members?.Resolve(member.OriginalDefinition) as IMethodSymbol ?? member).OriginalDefinition;

        Followed? result = null;

        if (!this.region.Symbols.IsSodaFlow(definition) && BodyOf(definition) is { } body)
        {
            IReadOnlyList<Binding> bindings =
            [
                .. arguments
                    .Take(definition.Parameters.Length)
                    .Select((argument, position) =>
                        new Binding(
                            parameter: definition.Parameters[position],
                            argument: argument.Written,
                            state: argument.State))
            ];

            result =
                definition.IsStatic
                    ? this.Enter(
                        call: call,
                        name: ImpliedNameOf(definition),
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                        receiver: null,
                        target: null)
                    : this.Enter(
                        call: call,
                        name: ImpliedNameOf(definition),
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: owner == null ? this.thisMembers : TaggedMembers(owner),
                        receiver: owner,
                        target: null);
        }

        this.implied[(call, member)] = result;

        return result;
    }

    /// <summary>
    ///     The call that an expression makes with no call syntax. It is the getter
    ///     of an indexer, an operator, or a conversion that the code writes as a
    ///     cast. A concatenation also calls <c>ToString</c>. The result is null for
    ///     an expression with no such call.
    /// </summary>
    public Followed? FollowExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ElementAccessExpressionSyntax access when this.region.Symbols.DeclaresIndexers =>
                this.FollowIndexer(access: access, owner: access.Expression, indexes: access.ArgumentList),
            ElementBindingExpressionSyntax binding
                when this.region.Symbols.DeclaresIndexers && ConditionalReceiver(binding) is { } owner =>
                this.FollowIndexer(access: binding, owner: owner, indexes: binding.ArgumentList),
            BinaryExpressionSyntax binary
                when this.region.Symbols.DeclaresOperators || this.region.Symbols.DeclaresToString =>
                this.region.SymbolOf(binary) switch
                {
                    IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } operation =>
                        this.FollowOperator(call: binary, operation: operation, operands: [binary.Left, binary.Right]),
                    // A concatenation calls ToString on an operand that is not a string.
                    IMethodSymbol
                    {
                        MethodKind: MethodKind.BuiltinOperator,
                        ContainingType.SpecialType: SpecialType.System_String
                    } => Both(first: this.FollowToString(binary.Left), second: this.FollowToString(binary.Right)),
                    _ => null
                },
            PrefixUnaryExpressionSyntax prefix when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(call: prefix, operation: this.region.SymbolOf(prefix), operands: [prefix.Operand]),
            PostfixUnaryExpressionSyntax postfix when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(
                    call: postfix,
                    operation: this.region.SymbolOf(postfix),
                    operands: [postfix.Operand]),
            CastExpressionSyntax cast when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(call: cast, operation: this.region.SymbolOf(cast), operands: [cast.Expression]),
            AssignmentExpressionSyntax compound
                when this.region.Symbols.DeclaresOperators
                     && !compound.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                this.FollowOperator(
                    call: compound,
                    operation: this.region.SymbolOf(compound),
                    operands: [compound.Left, compound.Right]),
            _ => null
        };

    /// <summary>
    ///     The conversion that the compiler adds to an expression, when an
    ///     operator of this compilation makes it.
    /// </summary>
    public Followed? FollowConversion(ExpressionSyntax expression) =>
        this.region.Symbols.DeclaresOperators && this.region.ConversionOf(expression) is { } conversion
            ? this.FollowOperator(call: expression, operation: conversion, operands: [expression])
            : null;

    /// <summary>
    ///     The call of <c>operator true</c> or <c>operator false</c> that a
    ///     condition makes on its value. No node names that operator, thus the
    ///     evaluator asks for the operation of the condition. It asks only in a
    ///     compilation that declares such an operator.
    /// </summary>
    public Followed? FollowTruth(ExpressionSyntax condition) =>
        this.region.Symbols.DeclaresTruth
        && this.region.ModelFor(condition).GetOperation(condition)?.Parent
            is IUnaryOperation { OperatorMethod: { } truth }
            ? this.FollowOperator(call: condition, operation: truth, operands: [condition])
            : null;

    /// <summary>
    ///     The call of <c>ToString</c> that an interpolation or a concatenation
    ///     makes on an object of this compilation.
    /// </summary>
    public Followed? FollowToString(ExpressionSyntax written) =>
        this.region.Symbols is { DeclaresToString: true, ObjectToString: { } toString }
        && this.Self(written) is { Members: not null } owner
            ? this.FollowImplied(call: written, member: toString, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The call of <c>Dispose</c> that a <c>using</c> makes on an object of
    ///     this compilation, when the code leaves the statement or the block.
    /// </summary>
    public Followed? FollowDispose(ExpressionSyntax resource) =>
        this.region.Symbols is { DeclaresDispose: true, DisposableDispose: { } dispose }
        && this.Self(resource) is { Members: not null } owner
            ? this.FollowImplied(call: resource, member: dispose, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The members of objects of this compilation that a call to code of the
    ///     platform runs, with no call of them in this code.
    ///     <see cref="PlatformCallers" /> says which members a method calls. A
    ///     callee that gets a sequence of this compilation also enumerates it.
    /// </summary>
    /// <remarks>
    ///     A report about such a member is at the value that has it, and not at
    ///     the call. One call can give two values, and each has its own members.
    /// </remarks>
    public Followed? FollowPlatformCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!this.region.Symbols.DeclaresPlatformMembers)
        {
            return null;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        ExpressionSyntax? receiver = ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        List<Operand> operands = [];

        // In a static call of an extension method, the receiver is one of the arguments. In a call
        // on the receiver, it is the argument of the first parameter.
        if (receiver != null && arguments.All(argument => argument.Expression != receiver))
        {
            operands.Add(
                method.ReducedFrom != null
                    ? new Operand(written: receiver, parameter: definition.Parameters[0].Type, isReceiver: false)
                    : new Operand(written: receiver, parameter: null, isReceiver: true));
        }

        operands.AddRange(
            arguments.Select((argument, position) =>
                new Operand(
                    written: argument.Expression,
                    parameter: ParameterFor(method: method, argument: argument, position: position)?.Type,
                    isReceiver: false)));

        return this.FollowPlatform(
            method: method,
            operands: operands,
            also: LoopState.None,
            given: () => this.Moved(invocation: invocation, method: method, withReceiver: true).AsElement,
            returned: () => this.Follow(invocation)?.State ?? LoopState.None);
    }

    /// <summary>The same as <see cref="FollowPlatformCall" />, for a constructor of the platform.</summary>
    public Followed? FollowPlatformCreation(IMethodSymbol constructor, SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        this.region.Symbols.DeclaresPlatformMembers
            ? this.FollowPlatform(
                method: constructor,
                operands:
                [
                    .. arguments.Select((argument, position) =>
                        new Operand(
                            written: argument.Expression,
                            parameter: SodaFlowSymbols.ParameterOf(
                                    callee: constructor.OriginalDefinition,
                                    argument: argument,
                                    position: position)
                                ?.Type,
                            isReceiver: false))
                ],
                also: LoopState.None,
                given: null,
                returned: null)
            : null;

    /// <summary>
    ///     The same as <see cref="FollowPlatformCall" />, for a member of the
    ///     platform that the code calls with no call syntax. It is the
    ///     <c>Add</c> of a collection initializer or of a collection expression, or
    ///     an accessor of an indexer.
    /// </summary>
    /// <param name="member">The method or the accessor.</param>
    /// <param name="collection">The collection, which has the values that it got before.</param>
    /// <param name="values">The values that the member gets, in the order of its parameters.</param>
    public Followed? FollowPlatformMember(
        IMethodSymbol member,
        ExpressionSyntax? collection,
        IEnumerable<ExpressionSyntax> values)
    {
        if (!this.region.Symbols.DeclaresKeyMembers
            || PlatformCallers.Of(member) == PlatformCallbacks.None)
        {
            return null;
        }

        ImmutableArray<IParameterSymbol> parameters = member.OriginalDefinition.Parameters;

        return this.FollowPlatform(
            method: member,
            operands:
            [
                .. values.Select((value, position) =>
                    new Operand(
                        written: value,
                        parameter: position < parameters.Length ? parameters[position].Type : null,
                        isReceiver: false))
            ],
            also: this.Eval(collection),
            given: null,
            returned: null);
    }

    /// <summary>
    ///     The calls that code of the platform makes when it enumerates a sequence
    ///     of this compilation. The first is <c>GetEnumerator</c>. The others are
    ///     <c>MoveNext</c> and <c>Current</c> of an enumerator of this compilation.
    ///     The result has the state of the elements. It is null for a value that is
    ///     not such a sequence.
    /// </summary>
    /// <remarks>
    ///     A query of the platform enumerates its source when something
    ///     enumerates the query, which can be after the block. The analyzer reads
    ///     the call as the time, as it does for a function that a query gets.
    /// </remarks>
    public Followed? FollowEnumeration(ExpressionSyntax written)
    {
        if (!this.region.Symbols.DeclaresEnumerators
            || this.Self(written) is not { Members: { HasType: true } members } sequence)
        {
            return null;
        }

        IMethodSymbol[] starts = [.. members.Implementations(SodaFlowSymbols.IsEnumerableInterface)];

        // The method for the generic interface. The other one calls it.
        IMethodSymbol? start =
            starts.FirstOrDefault(static method => method.ReturnType is INamedTypeSymbol { IsGenericType: true })
            ?? starts.FirstOrDefault();

        if (start == null
            || this.FollowImplied(call: written, member: start, owner: sequence, arguments: []) is not { } enumerator)
        {
            return null;
        }

        // An iterator has no enumerator object of this compilation, and a method can give the
        // enumerator of a collection of the platform. The result then has the state of the
        // elements.
        if (enumerator.State.Members is not { } made || !made.Is(start.ReturnType))
        {
            return enumerator;
        }

        Followed? all = enumerator;
        LoopState elements = LoopState.None;

        foreach (IMethodSymbol member in made.Implementations(SodaFlowSymbols.IsEnumeratorInterface))
        {
            if (member.Name is "Reset" || !member.Parameters.IsEmpty)
            {
                continue;
            }

            Followed? step = this.FollowImplied(call: written, member: member, owner: enumerator.State, arguments: []);
            all = Both(first: all, second: step);

            if (member.MethodKind == MethodKind.PropertyGet)
            {
                elements = elements.Join(step?.State ?? LoopState.None);
            }
        }

        return new Followed(
            state: elements,
            findings: all?.Findings ?? ImmutableArray<Finding>.Empty,
            outs: Followed.Nothing.Outs);
    }

    /// <summary>
    ///     The call that the getter of an indexer of the platform makes on its
    ///     key, as the indexer of a dictionary does.
    /// </summary>
    public Followed? FollowPlatformIndexer(ElementAccessExpressionSyntax access) =>
        this.region.Symbols.DeclaresKeyMembers
        && this.region.SymbolOf(access)
            is IPropertySymbol { GetMethod: { DeclaringSyntaxReferences.IsEmpty: true } getter }
            ? this.FollowPlatformMember(
                member: getter,
                collection: access.Expression,
                values: access.ArgumentList.Arguments.Select(static index => index.Expression))
            : null;

    private Followed? FollowPlatform(
        IMethodSymbol method,
        IReadOnlyCollection<Operand> operands,
        LoopState also,
        Func<LoopState>? given,
        Func<LoopState>? returned)
    {
        // A callee that gets a sequence enumerates it.
        Followed? all =
            operands
                .Where(static operand => SodaFlowSymbols.IsSequence(operand.Parameter))
                .Aggregate(
                    seed: (Followed?)null,
                    func: (followed, operand) =>
                        Both(first: followed, second: this.FollowEnumeration(operand.Written)));

        PlatformCallbacks callbacks = PlatformCallers.Of(method) & this.region.Symbols.DeclaredCallbacks;

        // A callee that gets a comparer calls the comparer. It does not call CompareTo, Equals, or
        // GetHashCode of an object.
        if (operands.Any(static operand => IsComparison(operand.Parameter)))
        {
            callbacks &= PlatformCallbacks.Text;
        }

        if (callbacks == PlatformCallbacks.None)
        {
            return all;
        }

        ImmutableArray<ITypeSymbol> compared = PlatformCallers.ComparedTypes(method);

        // A callee that gets a function compares what the function returns, as a sort by a key
        // does. It does not compare the values that it gives to the function.
        bool byFunction = operands.Any(static operand => operand.Parameter?.TypeKind == TypeKind.Delegate);
        List<(ExpressionSyntax Written, LoopState State, Construction Members)> owners = [];

        foreach (Operand operand in operands)
        {
            if (byFunction)
            {
                if (given != null && operand.Written is AnonymousFunctionExpressionSyntax lambda)
                {
                    LoopState parameters = given();

                    LoopState result =
                        this.EvalLambdaResult(
                            lambda: lambda,
                            boundParameters: ParametersOf(lambda).Select(_ => parameters).ToImmutableArray());

                    owners.AddRange(
                        CallbackOwners(state: result, written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members)));
                }
                else if (returned != null && operand.Parameter?.TypeKind == TypeKind.Delegate)
                {
                    // A method, a local function, or a lambda in a local. The evaluator follows it
                    // as the call of it, and that result has the state of what it returns.
                    owners.AddRange(
                        CallbackOwners(state: returned(), written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members)));
                }

                continue;
            }

            // A method that makes text from its arguments does not read its receiver, which has
            // the text so far.
            if (callbacks == PlatformCallbacks.Text && operand.IsReceiver && operands.Count > 1)
            {
                continue;
            }

            owners.AddRange(
                CallbackOwners(
                        state: this.Eval(operand.Written),
                        written: this.TypeBeforeObject(operand.Written),
                        asSequence: operand.IsReceiver || SodaFlowSymbols.IsSequence(operand.Parameter),
                        compared: compared)
                    .Select(owner => (operand.Written, owner.State, owner.Members)));
        }

        if (owners.Count == 0)
        {
            return all;
        }

        // The platform gives one object to a member of a second one, as a sort does to CompareTo.
        // The analyzer does not know which of them gets the call. Thus, each object can be the
        // parameter.
        LoopState others = owners.Aggregate(seed: also, func: static (joined, owner) => joined.Join(owner.State));

        return owners.Aggregate(
            seed: all,
            func: (followed, owner) =>
            {
                // A member with a parameter gets one more of the objects.
                (ExpressionSyntax Written, LoopState State)[] one = [(owner.Written, others)];

                return this.CallbackMembers(members: owner.Members, callbacks: callbacks)
                    .Aggregate(
                        seed: followed,
                        func: (found, member) =>
                            Both(
                                first: found,
                                second: this.FollowImplied(
                                    call: owner.Written,
                                    member: member,
                                    owner: owner.State,
                                    arguments: member.Parameters.Length == 1 ? one : [])));
            });
    }

    /// <summary>
    ///     The objects of this compilation in a value that a method of the
    ///     platform gets, when the method can call a member of them. The value is
    ///     such an object, a sequence of them, or a tuple that has one.
    /// </summary>
    /// <param name="state">The state of the value.</param>
    /// <param name="written">The type of the expression for the value. Null when it is not known.</param>
    /// <param name="asSequence">True when the method reads the elements of a sequence that it gets here.</param>
    /// <param name="compared">The types of the values that the method compares. Empty for each type.</param>
    private static IEnumerable<(LoopState State, Construction Members)> CallbackOwners(
        LoopState state,
        ITypeSymbol? written,
        bool asSequence,
        ImmutableArray<ITypeSymbol> compared)
    {
        if (state.Members is not { } members)
        {
            return [];
        }

        if (!members.HasType)
        {
            return members.TupleElements.SelectMany(element =>
                CallbackOwners(state: element, written: null, asSequence: false, compared: compared));
        }

        // A list has the members of its elements in its state. The list is one of those objects
        // only for a method that reads elements. A type such as object does not tell a list from
        // an element, and the state then says which the value is.
        bool isOwner =
            ((written == null || members.Is(written)) && !(state.IsContainer && Construction.SaysNothing(written)))
            || (asSequence && members.Is(SodaFlowSymbols.ElementTypeOf(written)));

        return isOwner && (compared.IsEmpty || compared.Any(members.Is)) ? [(state, members)] : [];
    }

    private IEnumerable<IMethodSymbol> CallbackMembers(Construction members, PlatformCallbacks callbacks)
    {
        SodaFlowSymbols symbols = this.region.Symbols;

        if ((callbacks & PlatformCallbacks.Compare) != PlatformCallbacks.None)
        {
            foreach (IMethodSymbol compare in members.Implementations(SodaFlowSymbols.IsComparableInterface))
            {
                yield return compare;
            }
        }

        if ((callbacks & PlatformCallbacks.Equals) != PlatformCallbacks.None)
        {
            foreach (IMethodSymbol typed in members.Implementations(SodaFlowSymbols.IsEquatableInterface))
            {
                yield return typed;
            }

            if (symbols.ObjectEquals is { } general)
            {
                yield return general;
            }
        }

        if ((callbacks & PlatformCallbacks.Hash) != PlatformCallbacks.None && symbols.ObjectGetHashCode is { } hash)
        {
            yield return hash;
        }

        if ((callbacks & PlatformCallbacks.Text) != PlatformCallbacks.None && symbols.ObjectToString is { } text)
        {
            yield return text;
        }
    }

    // A comparer, or a delegate of the type Comparison, which a sort can get.
    private static bool IsComparison(ITypeSymbol? type) =>
        SodaFlowSymbols.IsComparer(type)
        || type is INamedTypeSymbol { TypeKind: TypeKind.Delegate, Name: "Comparison" };

    // The elements that a callee gets from a sequence of this compilation, when the callee
    // enumerates what it gets for the parameter.
    private LoopState Enumerated(ExpressionSyntax? written, ITypeSymbol? parameter) =>
        written != null && SodaFlowSymbols.IsSequence(parameter)
            ? this.FollowEnumeration(written)?.State ?? LoopState.None
            : LoopState.None;

    /// <summary>
    ///     The call of a <c>Deconstruct</c> method. Each parameter gets an
    ///     argument, thus the result has the state that the method gives to each
    ///     <c>out</c> parameter.
    /// </summary>
    /// <param name="call">The deconstruction, or the part of a pattern.</param>
    /// <param name="method">The method that the compiler found.</param>
    /// <param name="value">The expression for the value, for a report.</param>
    /// <param name="owner">The state of the value.</param>
    public Followed? FollowDeconstruct(SyntaxNode call, IMethodSymbol method, ExpressionSyntax value, LoopState owner)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        // An extension method gets the value as its first argument.
        return this.FollowImplied(
            call: call,
            member: definition,
            owner: definition.IsStatic ? null : owner,
            arguments:
            [
                .. definition.Parameters.Select((_, position) =>
                    (Written: value, State: definition.IsStatic && position == 0 ? owner : LoopState.None))
            ]);
    }

    private Followed? FollowIndexer(SyntaxNode access, ExpressionSyntax owner, BaseArgumentListSyntax indexes) =>
        this.region.SymbolOf(access) is IPropertySymbol { GetMethod: { } getter }
            ? this.FollowImplied(
                call: access,
                member: getter,
                owner: this.Self(owner),
                arguments: [.. indexes.Arguments.Select(index => (index.Expression, this.Eval(index.Expression)))])
            : null;

    private Followed? FollowOperator(SyntaxNode call, ISymbol? operation, IEnumerable<ExpressionSyntax> operands) =>
        operation is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } method
            ? this.FollowImplied(
                call: call,
                member: method,
                owner: null,
                arguments: [.. operands.Select(operand => (operand, this.Eval(operand)))])
            : null;

    // The name of a member in a report: the property for an accessor, and the operator as the
    // code declares it.
    private static string ImpliedNameOf(IMethodSymbol definition) =>
        definition.AssociatedSymbol?.Name
        ?? (definition.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion
            ? definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : definition.Name);

    /// <summary>
    ///     The read of a property that a property pattern names, on a value with
    ///     the given state. A pattern has no expression for the owner of the
    ///     property, thus the walk gives the state of the owner.
    /// </summary>
    /// <param name="name">The name of the property in the pattern.</param>
    /// <param name="owner">The state of the value that the pattern is for.</param>
    public Followed? FollowPatternMember(ExpressionSyntax name, LoopState owner)
    {
        if (this.followed.TryGetValue(key: name, value: out Followed? known))
        {
            return known;
        }

        Followed? result =
            this.region.SymbolOf(name) is IPropertySymbol written && !this.region.Symbols.IsSodaFlow(written)
                ? this.FollowGetterOn(access: name, written: written, owner: owner)
                : null;

        this.followed[name] = result;

        return result;
    }

    private Followed? FollowGetterOn(ExpressionSyntax access, IPropertySymbol written, LoopState owner)
    {
        if (SodaFlowSymbols.IsLazyValue(written))
        {
            return this.FollowLazy(access: access, written: access, lazy: owner, value: written.Type);
        }

        // A property that the pattern reads through an interface is the property of the object.
        IPropertySymbol property = owner.Members?.Resolve(written) as IPropertySymbol ?? written;

        return property.IsStatic || GetterOf(property.OriginalDefinition) is not { } body
            ? null
            : this.Enter(
                call: access,
                name: property.Name,
                body: body,
                bindings: [],
                definer: null,
                members: TaggedMembers(owner),
                receiver: owner,
                target: null);
    }

    /// <summary>
    ///     The read of <c>Value</c> of a <c>Lazy</c>, which runs the functions
    ///     that the <c>Lazy</c> has. The result is null when it has no function
    ///     that the evaluator can read.
    /// </summary>
    /// <remarks>
    ///     A <c>Lazy</c> from a <c>Map</c> has two functions, and the second one
    ///     gets the value of the first one as its parameter. Thus, a second walk
    ///     gives each parameter the result of the first walk also.
    /// </remarks>
    /// <param name="access">The read of the property.</param>
    /// <param name="written">The expression that names the <c>Lazy</c>, for a report.</param>
    /// <param name="lazy">The state of the <c>Lazy</c>.</param>
    /// <param name="value">The type of the value.</param>
    private Followed? FollowLazy(SyntaxNode access, ExpressionSyntax written, LoopState lazy, ITypeSymbol value)
    {
        // A Lazy of a delegate can hold a function as its value. The read gives that function,
        // and does not run it. A function that makes such a value returns a delegate.
        ImmutableArray<FunctionValue> makers =
            value.TypeKind == TypeKind.Delegate
                ? lazy.Functions.Where(this.ReturnsDelegate).ToImmutableArray()
                : lazy.Functions;

        if (makers.IsEmpty)
        {
            return null;
        }

        Followed? first = Run(lazy);

        Followed? followed =
            first == null || makers.Length == 1 || first.State.IsNone ? first : Run(lazy.Join(first.State));

        if (followed == null || makers.Length == lazy.Functions.Length)
        {
            return followed;
        }

        return new Followed(
            state: lazy.Functions
                .Where(function => !makers.Contains(function))
                .Aggregate(
                    seed: followed.State,
                    func: static (state, function) => state.Join(LoopState.OfFunction(function))),
            findings: followed.Findings,
            outs: followed.Outs);

        Followed? Run(LoopState given) =>
            makers.Aggregate(
                seed: (Followed?)null,
                func: (all, function) =>
                    Both(
                        first: all,
                        second: this.FollowFunction(
                            call: access,
                            name: NameOf(written),
                            function: function,
                            target: lazy,
                            arguments: default,
                            each: (written, given))));
    }

    private bool ReturnsDelegate(FunctionValue function) =>
        (function.Declaration is AnonymousFunctionExpressionSyntax
            ? this.region.SymbolOf(function.Declaration)
            : this.region.ModelFor(function.Declaration).GetDeclaredSymbol(function.Declaration))
        is IMethodSymbol { ReturnType.TypeKind: TypeKind.Delegate };

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

        // A function of the walk, read by an evaluator that changes nothing: it gets a copy of
        // the locals of its code also.
        if (this.quiet && definer != null)
        {
            definer = definer.Quiet();
        }

        LoopStateEvaluator inner = this.Inner(bindings: bindings, definer: definer, members: members);
        List<Finding> found = [];
        SinkWalker walker = new(region: this.region, evaluator: inner, sink: found.Add);
        walker.Visit(body);

        // A lambda and a local function are part of a frame that can run more code. A query of
        // such a frame stays for the code that enumerates it. A callee that returns runs no more
        // code, and a function that runs after its frame returned is the same. The walk reads
        // the queries of such a frame now, and each query keeps what the walk found.
        IReadOnlyList<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving =
            definer == null || inner.Frame.HasReturned ? walker.RunWaitingApart() : [];

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
            found: found,
            leaving: leaving);
    }

    /// <summary>
    ///     The evaluator for the body of a callee, with each parameter bound to
    ///     the state of its argument. The parameter is the origin of that state.
    /// </summary>
    private LoopStateEvaluator Inner(
        IEnumerable<Binding> bindings,
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
            assigned: definer?.assigned ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            thisMembers: definer?.thisMembers ?? members,
            quiet: this.quiet);
    }

    /// <summary>
    ///     Changes the result and the findings of a callee from the names of the
    ///     callee to the names of this code. A finding about a parameter becomes a
    ///     finding at the argument for that parameter. A finding about a member
    ///     becomes a finding at the call.
    /// </summary>
    /// <remarks>
    ///     A query that the callee made and did not enumerate keeps its findings,
    ///     changed as each other finding is. This code reports them where it
    ///     enumerates the query. The query is then one of the queries of the frame
    ///     of this code, which can give it to its own caller.
    /// </remarks>
    private Followed Leave(
        SyntaxNode call,
        string name,
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator inner,
        bool lexical,
        LoopState? receiver,
        LoopState? target,
        LoopState result,
        IEnumerable<Finding> found,
        IEnumerable<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving)
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

        // A function can run after the code that made it returned. A lambda that a constructor
        // kept in a field is such a function. No walk of that code gets the finding. Thus, this
        // code reports it at the call, as it does for a callee with a frame of its own.
        bool returned = lexical && !sameFrame && inner.Frame.HasReturned;

        if (!lexical)
        {
            inner.Frame.Return();
        }

        foreach ((QueryValue query, IReadOnlyList<Finding> foundInIt) in leaving)
        {
            query.Hold(Changed(foundInIt));

            if (!this.quiet)
            {
                this.Frame.Wait(query);
            }
        }

        ImmutableArray<Finding> findings = Changed(found);

        ISymbol? inputs =
            bindings.Select(static binding => binding.State.AnyOrigin).FirstOrDefault(static origin => origin != null)
            ?? receiver?.AnyOrigin
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
            findings: findings,
            outs: outs.ToImmutable());

        // The findings of the callee, in the names of this code.
        ImmutableArray<Finding> Changed(IEnumerable<Finding> from)
        {
            ImmutableArray<Finding>.Builder changed = ImmutableArray.CreateBuilder<Finding>();

            foreach (Finding finding in from)
            {
                // A finding of a different frame goes to that frame as it is.
                if (!ReferenceEquals(objA: finding.Home, objB: inner.Frame))
                {
                    changed.Add(finding);

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

                    // The read can be of a part of the argument, as of a member of an object. Then,
                    // the origin is the origin of a part.
                    changed.Add(
                        new Finding(
                            home: this.Frame,
                            origin: binding.State.AnyOrigin,
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

                if (lexical && !returned)
                {
                    changed.Add(finding);

                    continue;
                }

                // The caller did not give a value that has no origin. A callee can only have it from a
                // placeholder type, and the analyzer does not know which loop that placeholder is of.
                if (finding.Origin == null)
                {
                    continue;
                }

                changed.Add(
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

            return changed.ToImmutable();
        }

        // The origin in this code of a value that has the given origin in the callee.
        ISymbol? Out(ISymbol? origin)
        {
            if (origin != null && byParameter.TryGetValue(key: origin, value: out Binding given))
            {
                return given.State.AnyOrigin;
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
    private static SyntaxNode? BodyOf(ISymbol definition) =>
        definition.DeclaringSyntaxReferences
            .Select(static reference =>
                reference.GetSyntax() switch
                {
                    MethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
                    LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                    OperatorDeclarationSyntax operation =>
                        operation.Body ?? (SyntaxNode?)operation.ExpressionBody?.Expression,
                    ConversionOperatorDeclarationSyntax conversion =>
                        conversion.Body ?? (SyntaxNode?)conversion.ExpressionBody?.Expression,
                    AccessorDeclarationSyntax accessor =>
                        accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression,
                    // The getter of a property or an indexer that is only an expression.
                    ArrowExpressionClauseSyntax arrow => arrow.Expression,
                    _ => null
                })
            .FirstOrDefault(static body => body != null);

    /// <summary>The body of the getter of a property, when the getter has one.</summary>
    private static SyntaxNode? GetterOf(ISymbol definition) =>
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
    private ExpressionSyntax DelegateOf(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access
                when this.region.SymbolOf(access) is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } =>
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
    ///     True when a function given to an operator reads a pending value. The
    ///     evaluator reads the function as a call of it, with each parameter bound
    ///     to the given state. This evaluator must be a quiet one, because the
    ///     function does not run now.
    /// </summary>
    private bool FunctionReadsPending(InvocationExpressionSyntax invocation, IMethodSymbol method, LoopState given)
    {
        return invocation.ArgumentList.Arguments
            .Select((argument, position) =>
                (Written: argument.Expression,
                    Parameter: ParameterFor(method: method, argument: argument, position: position)))
            // A lambda with no call and no property that reads cannot read a value.
            .Where(static argument =>
                argument.Written is not AnonymousFunctionExpressionSyntax lambda || CanRead(lambda.Body))
            .Any(Reads);

        bool Reads((ExpressionSyntax Written, IParameterSymbol? Parameter) argument)
        {
            LoopState functions = this.Eval(argument.Written);

            return Callables(state: functions, parameter: argument.Parameter)
                .Any(function =>
                    this.FollowFunction(
                            call: invocation,
                            name: NameOf(argument.Written),
                            function: function,
                            target: functions,
                            arguments: default,
                            each: (argument.Written, given))
                        is { } followed
                    && followed.Findings.Any(static finding => !finding.Escape));
        }
    }

    /// <summary>
    ///     False when no function in the arguments can use a value with a state.
    ///     This is a check of the syntax only. For most functions, the evaluator
    ///     then does not read the function. A function gets such a value through
    ///     its parameters, or through a name that has a state. It can also get one
    ///     through a call with no owner, which can be a local function or a member
    ///     of this object.
    /// </summary>
    private bool CanUseState(InvocationExpressionSyntax invocation, LoopState given)
    {
        HashSet<string>? names = null;

        foreach (ExpressionSyntax written in invocation.ArgumentList.Arguments
                     .Select(static argument => argument.Expression))
        {
            if (written is not AnonymousFunctionExpressionSyntax lambda)
            {
                // A name can be a function of this compilation. A comparer with a state can
                // have a function, or can be an object whose methods the operator calls.
                if (this.region.SymbolOf(written) is IMethodSymbol
                    || (this.region.TypeOf(written) is { } type
                        && (type.TypeKind == TypeKind.Delegate
                            || (SodaFlowSymbols.IsComparer(type) && !this.Eval(written).IsNone))))
                {
                    return true;
                }

                continue;
            }

            if (!CanRead(lambda.Body))
            {
                continue;
            }

            if (!given.IsNone || this.region.HasFields)
            {
                return true;
            }

            names ??= this.NamesWithState();

            if (lambda.Body.DescendantNodesAndSelf()
                .Any(node =>
                    node switch
                    {
                        ThisExpressionSyntax => this.thisMembers.Count > 0,
                        InvocationExpressionSyntax { Expression: IdentifierNameSyntax or GenericNameSyntax } => true,
                        IdentifierNameSyntax name => names.Contains(name.Identifier.ValueText),
                        _ => false
                    }))
            {
                return true;
            }
        }

        return false;
    }

    private HashSet<string> NamesWithState()
    {
        HashSet<string> names = [this.region.PlaceholderName];

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.locals.Where(static pair => !pair.Value.IsNone))
        {
            names.Add(pair.Key.Name);
        }

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.bound.Where(static pair => !pair.Value.IsNone))
        {
            names.Add(pair.Key.Name);
        }

        foreach (ISymbol member in this.thisMembers.Keys)
        {
            names.Add(member.Name);
        }

        return names;
    }

    private static bool CanRead(SyntaxNode body) =>
        body.DescendantNodesAndSelf()
            .Any(static node =>
                node is InvocationExpressionSyntax
                    or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" or "CanExecute" }
                    or MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" or "CanExecute" });

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
                    assigned: this.assigned,
                    thisMembers: this.thisMembers,
                    quiet: this.quiet);

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
    ///     function. An iterator returns a sequence of the values that it yields.
    ///     A sequence has the state of its elements, thus those values are here
    ///     also.
    /// </summary>
    public static IEnumerable<ExpressionSyntax> ReturnExpressions(SyntaxNode body) =>
        body.DescendantNodes(
                descendIntoChildren: static n =>
                    n is not (AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax))
            .Select(static statement =>
                statement switch
                {
                    ReturnStatementSyntax returned => returned.Expression,
                    YieldStatementSyntax yielded => yielded.Expression,
                    _ => null
                })
            .OfType<ExpressionSyntax>();

    private LoopState JoinAll(IEnumerable<ExpressionSyntax?> expressions) =>
        expressions.Aggregate(seed: LoopState.None, func: (state, e) => state.Join(this.Eval(e)));

    private LoopState TypeState(ExpressionSyntax expression) =>
        this.region.Symbols.StateOfType(this.region.TypeOf(expression));

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

    // Two implied calls are the same when the node and the member are the same.
    private sealed class ImpliedCalls : IEqualityComparer<(SyntaxNode Node, ISymbol Member)>
    {
        public static readonly ImpliedCalls Instance = new();

        public bool Equals((SyntaxNode Node, ISymbol Member) x, (SyntaxNode Node, ISymbol Member) y) =>
            ReferenceEquals(objA: x.Node, objB: y.Node)
            && SymbolEqualityComparer.Default.Equals(x: x.Member, y: y.Member);

        public int GetHashCode((SyntaxNode Node, ISymbol Member) call) =>
            (call.Node.GetHashCode() * 397) ^ SymbolEqualityComparer.Default.GetHashCode(call.Member);
    }

    /// <summary>One argument of a call, with the parameter of the callee that gets it.</summary>
    // One value that a call gives to code of the platform. The parameter is the type that the
    // definition of the callee has for it. It is null for the receiver of a method of the callee.
    private readonly struct Operand(in ExpressionSyntax written, in ITypeSymbol? parameter, in bool isReceiver)
    {
        public ExpressionSyntax Written { get; } = written;

        public ITypeSymbol? Parameter { get; } = parameter;

        public bool IsReceiver { get; } = isReceiver;
    }

    private readonly struct Binding(in IParameterSymbol parameter, in ExpressionSyntax argument, in LoopState state)
    {
        public IParameterSymbol Parameter { get; } = parameter;

        public ExpressionSyntax Argument { get; } = argument;

        public LoopState State { get; } = state;
    }
}
