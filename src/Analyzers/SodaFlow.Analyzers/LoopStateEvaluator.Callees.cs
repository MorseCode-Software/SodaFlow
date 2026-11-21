using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Locations =
    System.Collections.Immutable.ImmutableDictionary<
        Microsoft.CodeAnalysis.ISymbol,
        (SodaFlow.Analyzers.LoopStateEvaluator Reader, Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Location)>;

namespace SodaFlow.Analyzers;

// The part of the evaluator that walks the body of a callee. It makes the evaluator for that body
// from the arguments of the call, and gives the caller what the walk found.
internal sealed partial class LoopStateEvaluator
{
    // The state of a read of a member, with what the initializer of the member says. A creation
    // there says the class of the object. A lambda or the name of a static method there is the
    // function of the delegate. The walk reads the initializer for those things only.
    private LoopState WithItsInitializer(SyntaxNode expression, LoopState state)
    {
        if (this.region.SymbolOf(expression) is not { } member
            || member is not (IFieldSymbol { IsReadOnly: true } or IPropertySymbol { IsReadOnly: true })
            || this.region.InitializerOf(member.OriginalDefinition) is not { } made)
        {
            return state;
        }

        switch (made)
        {
            case AnonymousFunctionExpressionSyntax lambda when state.Functions.IsEmpty:
                return state.Join(
                    LoopState.OfFunction(new FunctionValue(declaration: lambda, definer: null, receiver: null)));
            case BaseObjectCreationExpressionSyntax creation
                when state.Members == null
                     && this.region.SymbolOf(creation)
                         is IMethodSymbol
                         {
                             ContainingType: { TypeKind: TypeKind.Class, DeclaringSyntaxReferences.IsEmpty: false }
                             itsClass
                         }:
            {
                Construction ofItsClass = Construction.Of(itsClass);

                // The object in a member of an object of the store.
                if (!state.Objects.IsEmpty)
                {
                    return state.WithMembers(ofItsClass);
                }

                // A static member has one object for each read of it. The store keeps that object,
                // which is older than the block. An evaluator that reads code which does not run
                // now makes no object.
                if (!member.IsStatic)
                {
                    return state;
                }

                MadeObject? one = this.region.Objects.InAStaticMember(member.OriginalDefinition);

                if (one == null)
                {
                    if (this.quiet)
                    {
                        return state;
                    }

                    one =
                        this.region.Objects.MakeInAStaticMember(
                            member: member.OriginalDefinition,
                            frame: this.Frame.Root,
                            type: itsClass,
                            state: LoopState.None.WithMembers(ofItsClass));
                }

                // The member is the origin of what its object has, as a field that the code set is.
                // Thus, a callee that reads the object gives its caller a finding.
                return state.WithMembers(ofItsClass).WithObjects(ImmutableArray.Create(one)).FromIfUnknown(member);
            }
            // The name of a static method of this compilation, as a value.
            case IdentifierNameSyntax or MemberAccessExpressionSyntax
                when state.Functions.IsEmpty
                     && this.region.SymbolOf(made) is IMethodSymbol { IsStatic: true } group
                     && group.OriginalDefinition.DeclaringSyntaxReferences
                             .Select(static reference => reference.GetSyntax())
                             .OfType<MethodDeclarationSyntax>()
                             .FirstOrDefault()
                         is { } declaration:
                return state.Join(
                    LoopState.OfFunction(
                        new FunctionValue(declaration: declaration, definer: null, receiver: null)));
            default:
                return state;
        }
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
    /// <param name="returnsAFunction">True when the callee returns a delegate.</param>
    /// <param name="returnsAnObject">
    ///     True when the result of the callee can be an object, and the type of
    ///     the result does not say the class of that object.
    /// </param>
    /// <param name="manyTimes">
    ///     True when a callee with no body to read runs the function. It can run
    ///     the function two or more times for one call.
    /// </param>
    /// <param name="owner">The expression of the object, when the call has one for a different object.</param>
    private Followed? Enter(
        SyntaxNode call,
        string name,
        SyntaxNode body,
        IReadOnlyList<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members,
        LoopState? receiver,
        LoopState? target,
        bool returnsAFunction = false,
        bool returnsAnObject = false,
        bool manyTimes = false,
        ExpressionSyntax? owner = null)
    {
        // Take of no item gives an empty sequence and does not start the iterator.
        if (this.TakesNoItem(call) && IsAnIteratorBody(body))
        {
            return Followed.Nothing;
        }

        // Nothing from the loop goes in, thus nothing from the loop comes out. A lambda or a local
        // function can read the locals of the code that it is in, thus the walk always reads it.
        // A callee that gets an object of the store can give the object a second name. A callee
        // that returns a delegate can return a lambda, which the caller then runs with values from
        // the loop. A callee that returns an object can make it. The class of that creation says
        // which method a call through an interface runs, when the type of the result does not.
        // The walk reads such callees while ReadsForObjects permits.
        bool noState =
            definer == null
            && members.Count == 0
            && this.region is { HasFields: false, Objects.ChangedAStaticObject: false }
            && bindings.All(static binding => binding.State.IsNone);

        // A lambda or a local function reads the locals of its code, which each call can change.
        // Thus, only a callee with a frame of its own stops at a call in its own code.
        int? recursive =
            definer == null && !noState
                ? this.ShapeOfACallAgain(body: body, bindings: bindings, members: members)
                : null;

        switch (noState)
        {
            case true when !this.ReadsForObjects(
                bindings: bindings,
                returnsAFunction: returnsAFunction || returnsAnObject):
                return Followed.Nothing;
            case false when this.depth >= MaxInlineDepth
                            || (recursive is { } shape && this.region.WalksAgainWith(body: body, shape: shape))
                            || !this.region.TryEnter():
                this.DoesNotRead(call: call, name: name, members: members);

                return null;
        }

        this.region.StartsAWalkOf(body: body, shape: recursive);

        MadeObject? self = definer == null && owner != null ? this.TheOneCollectionOf(owner) : null;

        // A function of the walk, read by an evaluator that changes nothing: it gets a copy of
        // the locals of its code also.
        if (this.quiet && definer != null)
        {
            definer = definer.Quiet();
        }

        // The body of a lambda or of a local function is a part of that function.
        SyntaxNode? function =
            definer == null
                ? null
                : body.FirstAncestorOrSelf<SyntaxNode>(static node =>
                    node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

        LoopStateEvaluator inner =
            this.Inner(
                call: call,
                bindings: bindings,
                definer: definer,
                members: members,
                function: function,
                eachRun: manyTimes ? body : null,
                self: self,
                itsObject: receiver?.Members);

        // A local of the function is new in each call. A call of the function in its own code
        // keeps the locals as they are, because the first call reads them after it.
        SyntaxNode? started = function != null && this.region.StartsACallOf(function) ? function : null;

        if (started != null)
        {
            inner.ForgetTheLocalsOf(started);
        }

        List<Finding> found = [];
        SinkWalker walker = inner.ReadBy(new SinkWalker(region: this.region, evaluator: inner, sink: found.Add));

        // An iterator that the code stops after its first item runs to its first yield return.
        if (this.TakesOneItem(call))
        {
            walker.TakesOneItem();
        }

        int before = manyTimes ? inner.ShapeOfWhatALoopLeaves(body) : 0;
        walker.Visit(body);
        found.AddRange(inner.Frame.TakeUnread());

        // A lambda and a local function are part of a frame that can run more code. A query of
        // such a frame stays for the code that enumerates it. A callee that returns runs no more
        // code, and a function that runs after its frame returned is the same. The walk reads
        // the queries of such a frame now, and each query keeps what the walk found.
        IReadOnlyList<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving =
            definer == null || inner.Frame.HasReturned ? walker.RunWaitingApart() : [];

        LoopState result =
            body is ExpressionSyntax expression ? inner.Eval(expression) : inner.JoinAll(ReturnExpressions(body));

        // The objects of the run become the objects of the runs before. A run reads what the run
        // before left. Thus, the walk reads the function again while a read gives a location a new
        // state, as it reads the body of a loop. Each read has an evaluator of its own, which reads
        // each expression again.
        if (manyTimes)
        {
            inner.EndTurns(body);
            HashSet<int> left = [before];

            for (int read = 2;
                 read <= SinkWalker.MaxReadsOfALoop && left.Add(inner.ShapeOfWhatALoopLeaves(body));
                 read++)
            {
                inner =
                    this.Inner(
                        call: call,
                        bindings: bindings,
                        definer: definer,
                        members: members,
                        function: function,
                        eachRun: body,
                        self: self,
                        itsObject: receiver?.Members);

                if (started != null)
                {
                    inner.ForgetTheLocalsOf(started);
                }

                inner.ReadBy(new SinkWalker(region: this.region, evaluator: inner, sink: found.Add)).Visit(body);
                found.AddRange(inner.Frame.TakeUnread());

                result =
                    result.Join(
                        body is ExpressionSyntax again ? inner.Eval(again) : inner.JoinAll(ReturnExpressions(body)));

                inner.EndTurns(body);
            }
        }

        this.region.EndsAWalk();

        if (started != null)
        {
            this.region.EndsACallOf(started);
        }

        Followed after =
            this.Leave(
                call: call,
                name: name,
                bindings: bindings,
                inner: inner,
                lexical: definer != null,
                receiver: receiver,
                target: target,
                result: result,
                found: found,
                leaving: leaving,
                thrown: walker.ThrownTo(call));

        if (receiver != null)
        {
            this.GivesItsOwner(call: call, owner: receiver, written: owner, sets: after.Sets);
        }

        return after;
    }

    // True when the code enumerates the result of a call for one item and then stops. First,
    // FirstOrDefault, and Any of Enumerable with no other argument do that. Take of one item and
    // the element at the first position do that also. A Select gives one item for each item that
    // the code takes from it. A foreach whose body ends the loop on each path takes one item also.
    // FirstOrDefault with a default value does as First does. AsEnumerable, and a query with one
    // select clause, do as Select does. The type of a from clause adds a Cast, which does the same.
    private bool TakesOneItem(SyntaxNode call) =>
        call.Parent switch
        {
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "First" or "FirstOrDefault" or "Any",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } next
            } => this.IsOfEnumerable(next),
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "FirstOrDefault",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } next
            } =>
                this.IsOfEnumerable(next)
                && this.region.SymbolOf(next) is IMethodSymbol { Parameters: { Length: > 0 } parameters }
                && parameters[parameters.Length - 1].Type.TypeKind != TypeKind.Delegate,
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "AsEnumerable",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } next
            } => this.IsOfEnumerable(next) && this.TakesOneItem(next),
            FromClauseSyntax
            {
                Parent: QueryExpressionSyntax
                {
                    Body: { Clauses.Count: 0, Continuation: null, SelectOrGroup: SelectClauseSyntax select }
                } query
            } => this.IsOfEnumerable(select) && this.TakesOneItem(OutsideItsParentheses(query)),
            MemberAccessExpressionSyntax
            {
                Name: { Identifier.ValueText: "Take" or "ElementAt" or "ElementAtOrDefault" } name,
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } next
            } =>
                this.region.ModelFor(next).GetConstantValue(arguments[0].Expression)
                    is { HasValue: true, Value: int value }
                && value == (name.Identifier.ValueText == "Take" ? 1 : 0)
                && this.IsOfEnumerable(next),
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Select",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } next
            } => this.IsOfEnumerable(next) && this.TakesOneItem(next),
            CommonForEachStatementSyntax loop =>
                SinkWalker.EndsTheLoopOnEachPath(loop: loop, body: loop.Statement),
            _ => false
        };

    // True when the code takes no item from the result of a call: Take of zero or fewer items.
    private bool TakesNoItem(SyntaxNode call) =>
        call.Parent is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "Take",
            Parent: InvocationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } next
        }
        && this.region.ModelFor(next).GetConstantValue(arguments[0].Expression)
            is { HasValue: true, Value: <= 0 }
        && this.IsOfEnumerable(next);

    private static bool IsAnIteratorBody(SyntaxNode body) =>
        body is BlockSyntax block
        && block.DescendantNodes(
                descendIntoChildren: static node =>
                    node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(static node => node is YieldStatementSyntax);

    private static SyntaxNode OutsideItsParentheses(SyntaxNode node)
    {
        while (node.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            node = parentheses;
        }

        return node;
    }

    private bool IsOfEnumerable(SyntaxNode call) =>
        this.region.SymbolOf(call) is IMethodSymbol method
        && SodaFlowSymbols.IsLinqEnumerable((method.ReducedFrom ?? method).OriginalDefinition.ContainingType);

    // Takes away the state of each local and each parameter that the given function declares,
    // before a call of that function. A local that a function in it reads keeps its state. That
    // function can run after the next call, with the local of its own call.
    private void ForgetTheLocalsOf(SyntaxNode function)
    {
        // The compiler does not keep its data flow analysis, and that analysis walks the full
        // member. When the function declares no local with a state, the walk has nothing to forget.
        if (!this.locals.Keys.Any(local => this.region.IsDeclaredIn(local: local, code: function))
            || this.region.CapturedIn(function) is not { } captured)
        {
            return;
        }

        foreach (ISymbol local in this.locals.Keys
                     .Where(local =>
                         this.region.IsDeclaredIn(local: local, code: function) && !captured.Contains(local))
                     .ToArray())
        {
            this.locals.Remove(local);
            this.assigned.Remove(local);
        }
    }

    /// <summary>
    ///     Records a member that this code calls and that the walk does not read.
    ///     It does that only when a member of the same object has a state, because
    ///     the code can read that member. A pending argument of such a call gets
    ///     the same report from the walk.
    /// </summary>
    private void DoesNotRead(SyntaxNode call, string name, Dictionary<ISymbol, LoopState> members)
    {
        if (this.quiet || members.Values.FirstOrDefault(static member => !member.IsNone) is not { } pending)
        {
            return;
        }

        this.Frame.DoesNotRead(
            new Finding(
                home: this.Frame,
                origin: pending.AnyOrigin,
                escape: true,
                diagnostic: Diagnostic.Create(
                    descriptor: Diagnostics.Escape,
                    location: call.GetLocation(),
                    this.region.PlaceholderName,
                    name),
                subject: name));
    }

    // True when the walk reads a callee that gets no state from the loop. It does that only for an
    // object of the store and for a callee that returns a delegate or an object. The depth limit
    // applies, and the region must have frames for such callees.
    private bool ReadsForObjects(IEnumerable<Binding> bindings, bool returnsAFunction = false) =>
        (returnsAFunction || bindings.Any(static binding => !binding.State.Objects.IsEmpty))
        && this.depth < MaxInlineDepth
        && this.region.TryEnterForObjects();

    /// <summary>
    ///     True for a call of a method that gets a task that can be not complete.
    ///     The method is of a package with no graph, and returns a task. Such a
    ///     method awaits the task before it runs a function that it gets. Thus,
    ///     the function runs after the call.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="method">The callee.</param>
    public bool WaitsBeforeItsFunctions(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (definition.ContainingAssembly is not { } assembly
            || !SodaFlowSymbols.PackagesWithNoGraph.Contains(assembly.Name)
            || definition.ReturnType is not INamedTypeSymbol returned
            || !SodaFlowSymbols.IsTask(returned))
        {
            return false;
        }

        // The receiver of an extension method is the argument of its first parameter.
        ExpressionSyntax? receiver =
            method.ReducedFrom != null
                ? ReceiverOf(symbols: this.region.Symbols, invocation: call, method: method)
                : null;

        IEnumerable<ExpressionSyntax> tasks =
            call.ArgumentList.Arguments
                .Where((argument, position) =>
                    ParameterFor(method: method, argument: argument, position: position)?.Type
                        is INamedTypeSymbol type
                    && SodaFlowSymbols.IsTask(type))
                .Select(static argument => argument.Expression);

        if (receiver != null
            && definition.Parameters[0].Type is INamedTypeSymbol owner
            && SodaFlowSymbols.IsTask(owner))
        {
            tasks = tasks.Append(receiver);
        }

        return tasks.Any(task => !CompleteTasks.IsComplete(region: this.region, task: task));
    }

    /// <summary>
    ///     The evaluator for the body of a callee, with each parameter bound to
    ///     the state of its argument. The parameter is the origin of that state.
    ///     The object of a member that the call names on a different object is
    ///     its receiver, as the caller read it.
    /// </summary>
    private LoopStateEvaluator Inner(
        SyntaxNode call,
        IEnumerable<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members,
        SyntaxNode? function,
        SyntaxNode? eachRun,
        MadeObject? self,
        Construction? itsObject = null)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder parameters = (definer?.bound ?? NoSymbols).ToBuilder();

        LoopFrame frame =
            definer?.Frame
            ?? new LoopFrame(
                parent: this.Frame,
                repeats: eachRun != null || !this.RunsOneTime(call),
                isOfAConstructor: call is BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax);

        ImmutableHashSet<ISymbol>.Builder noValue = (definer?.noValues ?? NoParameters).ToBuilder();
        HashSet<ISymbol> given = new(SymbolEqualityComparer.Default);
        HashSet<ISymbol> withAValue = new(SymbolEqualityComparer.Default);
        Dictionary<ISymbol, MadeObject> collections = new(SymbolEqualityComparer.Default);
        HashSet<ISymbol> unseen = new(SymbolEqualityComparer.Default);
        List<Binding> ofParamsArrays = [];
        Locations locations = NoLocations;

        foreach (Binding binding in bindings)
        {
            LoopState state = binding.State.From(binding.Parameter);
            locations = this.WithTheLocationFor(all: locations, binding: binding);

            // A parameter is the object of its argument. A params parameter is an array of them,
            // when the call does not give the array itself. An argument that is no name of a
            // local can make one new object, as a local does at its declaration.
            if (this.IsOneOfAParamsArray(parameter: binding.Parameter, argument: binding.Argument))
            {
                ofParamsArrays.Add(binding);
            }
            else if (KeepsItsObject(binding.Parameter))
            {
                if (this.CanBeUnseen(binding.Argument))
                {
                    unseen.Add(binding.Parameter);
                }
                else if ((this.TheOneCollectionOf(binding.Argument) ?? TheOneNewObject(binding.State)) is { } one)
                {
                    collections[binding.Parameter] = one;
                }
            }

            // A params parameter gets the states of all its arguments.
            parameters[binding.Parameter] =
                given.Add(binding.Parameter) ? state : parameters[binding.Parameter].Join(state);

            // A parameter has no value when each argument for it is null or default.
            if (this.IsNoValue(binding.Argument) && !withAValue.Contains(binding.Parameter))
            {
                noValue.Add(binding.Parameter);
            }
            else
            {
                withAValue.Add(binding.Parameter);
                noValue.Remove(binding.Parameter);
            }
        }

        // The arguments for a params parameter are in a new array, which holds only them. The
        // walk makes that object when the call runs one time, as it does for an array that the
        // code writes.
        if (definer == null && !this.quiet && !frame.Repeats)
        {
            foreach (IGrouping<ISymbol, Binding> group in ofParamsArrays.GroupBy<Binding, ISymbol>(
                         keySelector: static binding => binding.Parameter,
                         comparer: SymbolEqualityComparer.Default))
            {
                LoopState holding = HoldingItsObjects(parameters[group.Key].AsContainer);

                MadeObject array =
                    this.region.Objects.Make(
                        frame: frame,
                        type: ((IParameterSymbol)group.Key).Type,
                        exact: true,
                        state: holding);

                if (group.All(binding =>
                        this.IsKnownWhenItGoesIn(binding.Argument) && !this.CanBeUnseen(binding.Argument)))
                {
                    this.region.Objects.IsComplete(array);
                }

                parameters[group.Key] = holding.WithObjects(ImmutableArray.Create(array));
                collections[group.Key] = array;
            }
        }

        (bool Always, SyntaxNode? Turn) where = this.WhereItRuns(call);

        LoopStateEvaluator inner = new(
            region: this.region,
            bound: parameters.ToImmutable(),
            noValues: noValue.ToImmutable(),
            depth: this.depth + 1,
            frame: frame,
            locals: definer?.locals ?? new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            assigned: definer?.assigned ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            thisMembers: definer?.thisMembers ?? members,
            setOnThis: definer?.setOnThis
                       ?? (ReferenceEquals(objA: members, objB: this.thisMembers)
                           ? this.setOnThis
                           : new Dictionary<ISymbol, MadeObject?>(SymbolEqualityComparer.Default)),
            quiet: this.quiet,
            function: function,
            // A callee with no body to read can run a function two or more times in one call. A
            // run for one element is then a turn, and the body of the function is its body.
            call: new CallOfThisCode(
                runsOneTime: eachRun == null
                             && (this.RunsOneTime(call)
                                 || (this.call is { RunsOneTime: true } && this.RunsOneTimeInThisCode(call))),
                turn: eachRun ?? this.TurnOfACall(call),
                runsAFunctionOfThisRun: function != null && this.RunsAFunctionOfThisRun(call: call, function: function),
                loopsAroundTheTurn: eachRun != null
                    ? this.LoopsOf(call)
                    : this.TurnOfACall(call) is { } ofTheCall
                        ? this.LoopsAround(ofTheCall)
                        : ImmutableArray<SyntaxNode>.Empty,
                alwaysRuns: eachRun == null && where.Always,
                alwaysTurn: eachRun != null ? null : where.Turn,
                frameOfTheAlwaysTurn: this.FrameOfTheTurnOf(call),
                code: this.region.Objects.CodeOf(parent: this.CodeOfThisCall, call: call),
                self: self,
                hasItsOwnMembers: definer == null && !ReferenceEquals(objA: members, objB: this.thisMembers),
                typeArguments: this.TypeArgumentsOf(call: call, definer: definer),
                locations: locations,
                caller: this,
                site: call));

        inner.Frame.CanBeUnseen.UnionWith(unseen);
        inner.definer = definer;

        // A function of this code, and a member of this object, run on the object that this code
        // is in.
        inner.builtType =
            definer?.builtType ?? (ReferenceEquals(objA: members, objB: this.thisMembers) ? this.builtType : null);

        // A type that the walk knows says which override a call on this object runs.
        inner.thisObject =
            definer != null ? definer.thisObject
            : ReferenceEquals(objA: members, objB: this.thisMembers) ? this.thisObject
            : itsObject is { HasType: true } ? itsObject
            : null;

        // The code of a function does not read this record: see TheOneCollectionOf.
        foreach (KeyValuePair<ISymbol, MadeObject> one in collections)
        {
            inner.Frame.TheOneCollectionOf[one.Key] = one.Value;
        }

        return inner;
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
        IEnumerable<(QueryValue Query, IReadOnlyList<Finding> Found)> leaving,
        IEnumerable<(ITypeSymbol? Type, LoopState State)> thrown)
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

        ImmutableArray<(IParameterSymbol Parameter, ISymbol Member, LoopState State)>.Builder through =
            ImmutableArray.CreateBuilder<(IParameterSymbol Parameter, ISymbol Member, LoopState State)>();

        ImmutableHashSet<ISymbol>.Builder alwaysGives =
            ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);

        // A parameter has a state here only when the callee gave it one. For a parameter that is
        // not ref or out, that is a value that the callee put into the object, as Add does. The
        // caller has the same object. An assignment to the parameter changes only the local of
        // the callee, and a struct is a copy.
        foreach (Binding binding in bindings.Where(binding =>
                     binding.Parameter.RefKind != RefKind.None
                     || (!binding.Parameter.Type.IsValueType && !inner.assigned.Contains(binding.Parameter))))
        {
            if (inner.locals.TryGetValue(key: binding.Parameter, value: out LoopState? assigned))
            {
                outs[binding.Parameter] = assigned.WithOrigins(Out).FromIfUnknown(inputs);

                // An assignment that always runs gives the location of the argument this value. A
                // put or a change of a position after the last such assignment can go to that
                // location.
                if (inner.Frame.PositionsAtASureAssignment.TryGetValue(key: binding.Parameter, value: out int at)
                    && at == this.region.Objects.PositionChanges
                    && inner.Frame.PutsBeforeAnAssignment[binding.Parameter] == inner.Frame.PutsIntoLocations.Count)
                {
                    alwaysGives.Add(binding.Parameter);
                }

                // The members that the callee set through the parameter.
                if (assigned.Members is { } has
                    && inner.Frame.SetThrough.TryGetValue(key: binding.Parameter, value: out HashSet<ISymbol>? members))
                {
                    through.AddRange(
                        members.Select(member =>
                            (binding.Parameter, member, has.StateOf(member).WithOrigins(Out).FromIfUnknown(inputs))));
                }
            }
        }

        if (!lexical && inner.Frame.PutsThroughParameters.Count > 0)
        {
            this.GetsThePutsOf(callee: inner.Frame, argumentOf: byParameter);
        }

        // A callee on a different object got a copy of the members of that object. Each member
        // that it set goes back to the object. A function reads the members of its own code. A
        // constructor gives the creation the collection of each member.
        ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)> sets =
            lexical || (receiver == null && !inner.Frame.IsOfAConstructor)
                ? ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)>.Empty
                : inner.setOnThis
                    .Select(set =>
                        (set.Key, inner.thisMembers[set.Key].WithOrigins(Out).FromIfUnknown(inputs), set.Value))
                    .ToImmutableArray();

        return new Followed(
            state: result.WithOrigins(Out).FromIfUnknown(inputs),
            findings: findings,
            outs: outs.ToImmutable(),
            sets: sets,
            through: through.ToImmutable(),
            alwaysGives: alwaysGives.ToImmutable(),
            thrown: thrown
                .Select(one => (call, one.Type, one.State.WithOrigins(Out).FromIfUnknown(inputs)))
                .ToImmutableArray());

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
                    if (finding.Escape && this.IsAsserted(binding.Argument))
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
                            subject: finding.Subject,
                            readBack: finding.ReadBack));

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
                        subject: finding.Subject,
                        readBack: finding.ReadBack));
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

    // The members of the object of this code that a member of an interface names. A default method
    // of an interface names the member of the interface, but the object has the member of its
    // class. The members of the object give its classes.
    private IEnumerable<ISymbol> ImplementationsOnThis(ISymbol member) =>
        member.ContainingType is { TypeKind: TypeKind.Interface }
            ? this.thisMembers.Keys
                .Select(static key => key.ContainingType)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .Select(type => type.FindImplementationForInterfaceMember(member))
                .OfType<ISymbol>()
                .Distinct(SymbolEqualityComparer.Default)
            : [];

    // The state of a member of an interface on the object of this code: see ImplementationsOnThis.
    private LoopState OfTheImplementationsOnThis(ISymbol member) =>
        this.ImplementationsOnThis(member)
            .Aggregate(
                seed: LoopState.None,
                func: (joined, found) =>
                    this.thisMembers.TryGetValue(key: found.OriginalDefinition, value: out LoopState? own)
                        ? joined.Join(own)
                        : joined);

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

    /// <summary>
    ///     The state of what the function of a map returns, for the content of a
    ///     cell of cells or a cell of streams. The function is a lambda at the
    ///     call, or a function that the code gives by its name.
    /// </summary>
    private LoopState MappedContent(InvocationExpressionSyntax invocation, LoopState parameterState) =>
        invocation.ArgumentList.Arguments.Any(static argument =>
            argument.Expression is AnonymousFunctionExpressionSyntax)
            ? this.LambdaContent(invocation: invocation, parameterState: parameterState)
            : this.ContentOfANamedFunction(invocation: invocation, given: ImmutableArray.Create(parameterState));

    /// <summary>
    ///     True when a sort with no comparer compares sort values whose
    ///     <c>CompareTo</c> reads a pending value. The default comparer calls
    ///     <c>CompareTo</c> of each value that the selector returns, at the first
    ///     read of the view. This evaluator must be a quiet one.
    /// </summary>
    /// <remarks>
    ///     The walk follows a <c>CompareTo</c> that the type of the sort value
    ///     declares in this compilation. A sort value of a type of the platform
    ///     reads no value of the graph.
    /// </remarks>
    private bool SortValuesReadPending(InvocationExpressionSyntax invocation, IMethodSymbol method, LoopState given)
    {
        // The parameter of the definition has the type parameter of the sort value. The method of the
        // call has the type that the selector returns.
        if (method.Parameters.FirstOrDefault(static each => each.Name == "selector")?.Type is not
            INamedTypeSymbol { DelegateInvokeMethod.ReturnType: INamedTypeSymbol sortValue })
        {
            return false;
        }

        IMethodSymbol[] compareTo =
        [
            .. sortValue.GetMembers("CompareTo")
                .OfType<IMethodSymbol>()
                .Where(static member => member.Parameters.Length == 1 && !member.DeclaringSyntaxReferences.IsEmpty)
        ];

        return compareTo.Length > 0
               && invocation.ArgumentList.Arguments
                   .Where((argument, position) =>
                       ParameterFor(method: method, argument: argument, position: position) is { Name: "selector" })
                   .Select(static argument => argument.Expression)
                   .Any(ComparesPending);

        bool ComparesPending(ExpressionSyntax written)
        {
            LoopState functions = this.Eval(written);

            return Callables(state: functions, parameter: null)
                .Select(function =>
                    this.FollowFunction(
                            call: invocation,
                            name: NameOf(written),
                            function: function,
                            target: functions,
                            arguments: default,
                            each: (written, given, ManyTimes: true))
                        ?.State
                    ?? LoopState.None)
                .Any(value =>
                    compareTo.Any(member =>
                        this.FollowImplied(call: written, member: member, owner: value, arguments: [(written, value)])
                            ?.Findings.Any(static finding => !finding.Escape) == true));
        }
    }

    /// <summary>
    ///     The run of <c>Equals</c> of the comparer of a two-way bindable. Its
    ///     setter compares the value that it has with the value written, in the
    ///     call. The bindable is a local that no code writes, and that
    ///     <c>CreateTwoWay</c> made with a comparer.
    /// </summary>
    /// <param name="bindable">The expression of the bindable.</param>
    /// <param name="value">The value that the code writes.</param>
    /// <param name="write">The assignment.</param>
    public Followed? FollowComparerOfAWrite(ExpressionSyntax bindable, ExpressionSyntax value, SyntaxNode write)
    {
        if ((this.region.Symbols.DeclaredCallbacks & PlatformCallbacks.Equals) == PlatformCallbacks.None
            || this.ValueOfALocalThatNoCodeWrites(Unwrapped(bindable)) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateTwoWay" }
            } creation
            || this.region.SymbolOf(creation) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            return null;
        }

        LoopState written = this.Eval(value);

        return creation.ArgumentList.Arguments
            .Select((argument, position) =>
                (argument.Expression,
                    Parameter: ParameterFor(method: method, argument: argument, position: position)))
            .Where(argument =>
                argument.Parameter is { } parameter
                && OperatorTable.IsComparerAtAWrite(method: method, parameter: parameter))
            .Aggregate(
                seed: (Followed?)null,
                func: (all, argument) =>
                {
                    LoopState comparer = this.Eval(argument.Expression);

                    // The setter calls Equals only, and not GetHashCode.
                    return Callables(state: comparer, parameter: argument.Parameter)
                        .Where(static function =>
                            function.Declaration is MethodDeclarationSyntax { Identifier.ValueText: "Equals" })
                        .Aggregate(
                            seed: all,
                            func: (found, function) =>
                                Both(
                                    first: found,
                                    second: this.FollowFunction(
                                        call: write,
                                        name: "Equals",
                                        function: function,
                                        target: comparer,
                                        arguments: default,
                                        each: (value, written, ManyTimes: false))));
                });
    }

    /// <summary>
    ///     The run of the functions that a map of a collection calls in its
    ///     <c>Dispose</c>. The map calls its eviction handler for each object that
    ///     it built. The map is a local that no code writes, and that <c>Map</c>
    ///     made, or the call of <c>Map</c> itself.
    /// </summary>
    /// <remarks>
    ///     A map builds its objects at the first read of its items, or at the end
    ///     of the transaction, which is after the close. Thus, a map that the code
    ///     uses only to call <c>Dispose</c> built no object in the block, and its
    ///     handler does not run. For each other map, the walk does not know if the
    ///     map built an object before the call. Thus, it follows the handler as if
    ///     the map did. A map that a different transaction made is not a local of
    ///     the block, and the walk does not follow its handler.
    /// </remarks>
    /// <param name="map">The expression of the map.</param>
    /// <param name="dispose">The call of <c>Dispose</c>, or the resource of a <c>using</c>.</param>
    /// <param name="runsOnEachPath">
    ///     True when the <c>Dispose</c> runs on all paths of its function. The
    ///     walk asks only for a map that a different code made.
    /// </param>
    public Followed? FollowEvictionsOfADispose(ExpressionSyntax map, SyntaxNode dispose, Func<bool> runsOnEachPath)
    {
        if (this.MadeValueOf(map) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Map" }
            } creation
            || this.region.SymbolOf(creation) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            // A field or a list of IDisposable can have the map. The store has the handlers of its
            // object.
            return this.FollowEvictions(
                map: map,
                dispose: dispose,
                handlers: this.ObjectsOfADispose(value: map, runsOnEachPath: runsOnEachPath)
                    .SelectMany(made => this.region.Objects.HandlersOfAMap(made)));
        }

        return this.CanHaveBuiltAnObject(creation)
            ? this.FollowEvictions(
                map: map,
                dispose: dispose,
                handlers: this.HandlersAtDispose(map: creation, method: method))
            : null;
    }

    // The handlers that a map of a collection runs in its Dispose, each with its parameter and its
    // state.
    private IEnumerable<MapHandler> HandlersAtDispose(InvocationExpressionSyntax map, IMethodSymbol method) =>
        map.ArgumentList.Arguments
            .Select((argument, position) =>
                (argument.Expression,
                    Parameter: ParameterFor(method: method, argument: argument, position: position)))
            .Where(argument =>
                argument.Parameter is { } parameter && OperatorTable.IsRunAtDispose(method: method, parameter: parameter))
            .Select(argument =>
                new MapHandler(
                    written: argument.Expression,
                    parameter: argument.Parameter,
                    handler: this.Eval(argument.Expression)));

    // The run of each handler of a map at its Dispose.
    private Followed? FollowEvictions(ExpressionSyntax map, SyntaxNode dispose, IEnumerable<MapHandler> handlers)
    {
        // The handler gets each object that the projection gave.
        LoopState objects = this.Eval(map).Content;

        return handlers
            .Aggregate(
                seed: (Followed?)null,
                func: (all, argument) =>
                {
                    LoopState handler = argument.Handler;

                    return Callables(state: handler, parameter: argument.Parameter)
                        .Aggregate(
                            seed: all,
                            func: (found, function) =>
                                Both(
                                    first: found,
                                    second: this.FollowFunction(
                                        call: dispose,
                                        name: NameOf(argument.Written),
                                        function: function,
                                        target: handler,
                                        arguments: default,
                                        each: (map, objects, ManyTimes: true))));
                });
    }

    // A map of a collection with a handler that its Dispose runs is an object of the store, which
    // keeps each handler. A map that can have built no object runs no handler.
    private LoopState AMapWithAHandler(InvocationExpressionSyntax map, IMethodSymbol method, LoopState state)
    {
        if (map.ArgumentList.Arguments.Count < 2
            || this.HandlersAtDispose(map: map, method: method).ToList() is not { Count: > 0 } handlers
            || !this.CanHaveBuiltAnObject(map))
        {
            return state;
        }

        LoopState made = this.MadeHere(place: map, state: state, exact: false);

        foreach (MadeObject each in made.Objects)
        {
            this.region.Objects.IsAMapWith(made: each, handlers: handlers);
        }

        return made;
    }

    /// <summary>
    ///     True for a clock whose type derives from
    ///     <c>TimerSystemImplementationBase</c>. Its <c>SetTimer</c> keeps a
    ///     callback, and its <c>RunTimersTo</c> runs each callback that is due, in
    ///     the call. A local of an interface has such a clock when no code writes
    ///     the local and its value has that type.
    /// </summary>
    /// <param name="clock">The expression of the clock.</param>
    public bool IsAClockOfTheLibraryBase(ExpressionSyntax clock)
    {
        for (ITypeSymbol? type = this.region.TypeOf(this.ValueOfALocalThatNoCodeWrites(Unwrapped(clock)));
             type != null;
             type = type.BaseType)
        {
            if (type.OriginalDefinition.Name == "TimerSystemImplementationBase" && this.region.Symbols.IsSodaFlow(type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The run of a callback that a call of the library runs, as
    ///     <c>RunTimersTo</c> runs the callback of a timer. The walk can take the
    ///     state of the callback where the code gave it.
    /// </summary>
    /// <param name="call">The call that runs the callback.</param>
    /// <param name="callback">The expression of the callback, where the code gave it.</param>
    /// <param name="functions">The state of the callback there.</param>
    public Followed? FollowCallback(SyntaxNode call, ExpressionSyntax callback, LoopState functions) =>
        this.FollowFunctions(
            call: call,
            name: NameOf(callback),
            functions: functions.Functions,
            target: functions,
            arguments: default);

    /// <summary>
    ///     The call of <c>SetTimer</c> of a clock of the library that made a
    ///     timer. The timer is that call, or a local that no code writes and that
    ///     has its value. Null for each other expression.
    /// </summary>
    /// <param name="timer">The expression of the timer.</param>
    public InvocationExpressionSyntax? CallOfSetTimer(ExpressionSyntax timer) =>
        this.MadeValueOf(timer) is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SetTimer" }
        } set
        && this.region.SymbolOf(set) is IMethodSymbol method
        && this.region.Symbols.IsSodaFlow(method)
            ? set
            : null;

    // The value of a local that no code writes. A local of IDisposable can have the value of a
    // different local, thus the search continues through it. The declaration of each local is
    // before its value, thus the search ends.
    private ExpressionSyntax MadeValueOf(ExpressionSyntax expression)
    {
        ExpressionSyntax value = this.ValueOfALocalThatNoCodeWrites(Unwrapped(expression));

        while (value is IdentifierNameSyntax name
               && this.ValueOfALocalThatNoCodeWrites(name) is var earlier
               && earlier.SpanStart < name.SpanStart)
        {
            value = earlier;
        }

        return value;
    }

    /// <summary>
    ///     The run of the members of its implementation that a timer system
    ///     calls in a call. Its constructor calls <c>Start</c> and <c>Now</c>, and
    ///     its <c>Dispose</c> calls <c>Stop</c>.
    /// </summary>
    /// <param name="system">The constructor that made the timer system.</param>
    /// <param name="arguments">The arguments of that constructor.</param>
    /// <param name="call">The node that calls the members.</param>
    /// <param name="members">The names of the members of the implementation.</param>
    public Followed? FollowClock(
        IMethodSymbol system,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SyntaxNode call,
        IEnumerable<string> members)
    {
        if (system.ContainingType is not { Name: "TimerSystem" or "SystemClockTimerSystem" } type
            || !this.region.Symbols.IsSodaFlow(type))
        {
            return null;
        }

        // SystemClockTimerSystem makes its own implementation, whose Now calls the function of the
        // time.
        if (type.Name == "SystemClockTimerSystem")
        {
            return members.Contains("Now")
                   && ArgumentFor(method: system, arguments: arguments, parameter: "now") is { } now
                ? this.FollowCallback(call: call, callback: now, functions: this.Eval(now))
                : null;
        }

        if (system.Parameters.FirstOrDefault(static parameter => parameter.Name == "implementation")
                is not { Type: var contract }
            || ArgumentFor(method: system, arguments: arguments, parameter: "implementation") is not { } implementation)
        {
            return null;
        }

        LoopState clock = this.Eval(implementation);

        // Start gets the handler of the timer system.
        IReadOnlyList<(ExpressionSyntax Written, LoopState State)> handler =
            ArgumentFor(method: system, arguments: arguments, parameter: "handleException") is { } given
                ? [(given, this.Eval(given))]
                : [];

        return members
            .SelectMany(name => contract.GetMembers(name))
            .Select(static member => member is IPropertySymbol property ? property.GetMethod : member as IMethodSymbol)
            .OfType<IMethodSymbol>()
            .Aggregate(
                seed: (Followed?)null,
                func: (all, member) =>
                    Both(
                        first: all,
                        second: this.FollowImplied(
                            call: call,
                            member: member,
                            owner: clock,
                            arguments: member.Parameters.IsEmpty ? [] : handler)));
    }

    /// <summary>
    ///     The run of <c>Stop</c> of the implementation of a timer system, at its
    ///     <c>Dispose</c>. The timer system is a local that no code writes, and that
    ///     a constructor made, or the call of that constructor itself.
    /// </summary>
    /// <param name="system">The expression of the timer system.</param>
    /// <param name="dispose">The call of <c>Dispose</c>, or the resource of a <c>using</c>.</param>
    /// <param name="runsOnEachPath">
    ///     True when the <c>Dispose</c> runs on all paths of its function. The
    ///     walk asks only for a timer system that a different code made.
    /// </param>
    public Followed? FollowClockOfADispose(ExpressionSyntax system, SyntaxNode dispose, Func<bool> runsOnEachPath)
    {
        if (this.MadeValueOf(system) is BaseObjectCreationExpressionSyntax { ArgumentList: { } arguments } creation
            && this.region.SymbolOf(creation) is IMethodSymbol constructor)
        {
            return this.FollowClock(
                system: constructor,
                arguments: arguments.Arguments,
                call: dispose,
                members: ["Stop"]);
        }

        // A field or a list of IDisposable can have the timer system. The store has the clock of
        // its object.
        (IMethodSymbol Stop, LoopState Clock)[] clocks =
        [
            .. this.ObjectsOfADispose(value: system, runsOnEachPath: runsOnEachPath)
                .Select(made => this.region.Objects.ClockOfATimerSystem(made))
                .OfType<(IMethodSymbol Stop, LoopState Clock)>()
        ];

        return clocks.Length == 0
            ? null
            : this.FollowImplied(
                call: dispose,
                member: clocks[0].Stop,
                owner: clocks.Aggregate(seed: LoopState.None, func: static (all, each) => all.Join(each.Clock)),
                arguments: []);
    }

    // The objects that a Dispose of a value disposes. A callee can get an object from its caller,
    // and the walk does not know the path that the arguments of the callee select. Thus, the callee
    // disposes such an object only where its Dispose runs on all paths.
    private IEnumerable<MadeObject> ObjectsOfADispose(ExpressionSyntax value, Func<bool> runsOnEachPath)
    {
        bool? onEachPath = null;

        return this.region.Objects.Now(this.Eval(value).Objects)
            .Where(made => made.IsFrom(this.Frame) || (onEachPath ??= runsOnEachPath()));
    }

    /// <summary>
    ///     Records the clock of a timer system that a constructor made, for its
    ///     <c>Dispose</c>. The Dispose calls <c>Stop</c> of that clock.
    /// </summary>
    /// <param name="creation">The creation of the timer system.</param>
    /// <param name="system">The constructor.</param>
    /// <param name="arguments">The arguments of the constructor.</param>
    public void NotesATimerSystem(
        SyntaxNode creation,
        IMethodSymbol system,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        // Only the constructor of TimerSystem has a parameter of that name.
        if (creation is not BaseObjectCreationExpressionSyntax made
            || system.Parameters.FirstOrDefault(static parameter => parameter.Name == "implementation")
                is not { Type: var contract }
            || contract.GetMembers("Stop").OfType<IMethodSymbol>().FirstOrDefault() is not { } stop
            || ArgumentFor(method: system, arguments: arguments, parameter: "implementation") is not { } implementation)
        {
            return;
        }

        LoopState clock = this.Eval(implementation);

        foreach (MadeObject each in this.Eval(made).Objects)
        {
            this.region.Objects.IsATimerSystemOf(made: each, stop: stop, clock: clock);
        }
    }

    /// <summary>
    ///     The run of the members of its timer system that <c>Debounce</c> calls
    ///     in the call: the getter of <c>Time</c>, and <c>At</c>.
    /// </summary>
    /// <param name="call">The call of <c>Debounce</c>.</param>
    /// <param name="method">The method of the call.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public Followed? FollowTheTimersOfADebounce(
        SyntaxNode call,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (method.Parameters.FirstOrDefault(static parameter => parameter.Name == "timers")
                is not { Type: var contract }
            || ArgumentFor(method: method, arguments: arguments, parameter: "timers") is not { } timers)
        {
            return null;
        }

        LoopState system = this.Eval(timers);
        string[] members = ["Time", "At"];

        return members
            .SelectMany(name => contract.GetMembers(name))
            .Select(static member => member is IPropertySymbol property ? property.GetMethod : member as IMethodSymbol)
            .OfType<IMethodSymbol>()
            .Aggregate(
                seed: (Followed?)null,
                func: (all, member) =>
                    Both(
                        first: all,
                        second: this.FollowImplied(call: call, member: member, owner: system, arguments: [])));
    }

    // True when code can read the items of the map that the call makes. That is so when a local
    // keeps the map, and the code uses that local for more than a call of Dispose. A map that no
    // local keeps, or whose local only gets Dispose, gives its items to no code.
    private bool CanHaveBuiltAnObject(SyntaxNode creation)
    {
        SyntaxNode? above = creation.Parent;

        while (above is ParenthesizedExpressionSyntax or CastExpressionSyntax)
        {
            above = above.Parent;
        }

        if (above is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            || this.region.ModelFor(declarator).GetDeclaredSymbol(declarator) is not ILocalSymbol local)
        {
            return false;
        }

        // With no block to search, the walk cannot see each use of the local.
        return NamesOf(local: local, declarator: declarator, region: this.region) is not { } names
               || names.Any(static name =>
                   name.Parent is not MemberAccessExpressionSyntax
                   {
                       Name.Identifier.ValueText: "Dispose", Parent: InvocationExpressionSyntax
                   } access
                   || access.Expression != name);
    }

    /// <summary>
    ///     The state of what a function returns, for a function that the code
    ///     gives by its name to a map or a lift. The function is a local function,
    ///     a lambda in a local, or a method. Each parameter gets the state at its
    ///     position.
    /// </summary>
    /// <remarks>
    ///     The operator runs the function at a time that the walk does not know.
    ///     Thus, an evaluator that changes nothing reads it. The walk reads it only
    ///     for a result whose values can hold a state, as for a lambda.
    /// </remarks>
    private LoopState ContentOfANamedFunction(InvocationExpressionSyntax invocation, ImmutableArray<LoopState> given)
    {
        if (this.region.SymbolOf(invocation) is not IMethodSymbol method || !this.HoldsContent(method))
        {
            return LoopState.None;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        LoopState content = LoopState.None;

        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax written = arguments[i].Expression;

            if (ParameterFor(method: method, argument: arguments[i], position: i)
                    is not { Type.TypeKind: TypeKind.Delegate }
                || this.Eval(written) is not { Functions.IsEmpty: false } functions)
            {
                continue;
            }

            LoopStateEvaluator quiet = this.Quiet();

            content =
                functions.Functions.Aggregate(
                    seed: content,
                    func: (all, function) =>
                        all.Join(
                            quiet.FollowFunction(
                                    call: invocation,
                                    name: NameOf(written),
                                    function: function,
                                    target: functions,
                                    arguments: default,
                                    each: (written, LoopState.None, ManyTimes: true),
                                    byPosition: given)
                                ?.State
                            ?? LoopState.None));
        }

        return content;
    }
}
