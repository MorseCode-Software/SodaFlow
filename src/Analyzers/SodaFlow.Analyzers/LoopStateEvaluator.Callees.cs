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

                return state.WithMembers(ofItsClass).WithObjects(ImmutableArray.Create(one));
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
            && !this.region.HasFields
            && bindings.All(static binding => binding.State.IsNone);

        switch (noState)
        {
            case true when !this.ReadsForObjects(
                bindings: bindings,
                returnsAFunction: returnsAFunction || returnsAnObject):
                return Followed.Nothing;
            case false when this.depth >= MaxInlineDepth || !this.region.TryEnter():
                this.DoesNotRead(call: call, name: name, members: members);

                return null;
        }

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
                self: self);

        // A local of the function is new in each call. A call of the function in its own code
        // keeps the locals as they are, because the first call reads them after it.
        SyntaxNode? started = function != null && this.region.StartsACallOf(function) ? function : null;

        if (started != null)
        {
            inner.ForgetTheLocalsOf(started);
        }

        List<Finding> found = [];
        SinkWalker walker = inner.ReadBy(new SinkWalker(region: this.region, evaluator: inner, sink: found.Add));
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
                        self: self);

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
                leaving: leaving);

        if (receiver != null)
        {
            this.GivesItsOwner(call: call, owner: receiver, written: owner, sets: after.Sets);
        }

        return after;
    }

    // Takes away the state of each local and each parameter that the given function declares,
    // before a call of that function. A local that a function in it reads keeps its state. That
    // function can run after the next call, with the local of its own call.
    private void ForgetTheLocalsOf(SyntaxNode function)
    {
        if (this.region.CapturedIn(function) is not { } captured)
        {
            return;
        }

        foreach (ISymbol local in this.locals.Keys
                     .Where(local => IsDeclaredIn(local: local, code: function) && !captured.Contains(local))
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
    ///     The evaluator for the body of a callee, with each parameter bound to
    ///     the state of its argument. The parameter is the origin of that state.
    /// </summary>
    private LoopStateEvaluator Inner(
        SyntaxNode call,
        IEnumerable<Binding> bindings,
        LoopStateEvaluator? definer,
        Dictionary<ISymbol, LoopState> members,
        SyntaxNode? function,
        SyntaxNode? eachRun,
        MadeObject? self)
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
                locations: locations));

        inner.Frame.CanBeUnseen.UnionWith(unseen);

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
            alwaysGives: alwaysGives.ToImmutable());

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
}
