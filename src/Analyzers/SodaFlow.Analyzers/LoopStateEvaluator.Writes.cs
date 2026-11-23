using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a write to a member of an object, and for what a write through a name replaces.
internal sealed partial class LoopStateEvaluator
{
    // The location or the first name that EvalAgainForAUse reads now.
    private ExpressionSyntax? readForAUse;

    /// <summary>
    ///     Records the state of a member of the object that the code is in, at
    ///     its initializer or assignment.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    /// <param name="from">The expression of the value, when the code has one.</param>
    public void SetMember(ISymbol member, LoopState state, bool replaces = false, ExpressionSyntax? from = null)
    {
        ISymbol key = member.OriginalDefinition;
        this.region.AddAMemberWrite();

        (this.writesThroughThis ??= new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default))[key] =
            this.region.MemberWrites;

        if (this.Frame.WrittenThroughANameOfThis.Count > 0)
        {
            this.Frame.WrittenThroughANameOfThis.Remove(key);
        }

        // The object can be older than a turn of a loop of this code. The receiver of a method can
        // be older than a turn of a loop of the caller also. A constructor makes a new object.
        state = this.InALocation(
            state: state,
            location: this,
            isOfItsTurn: static (_, made, code) => code.Frame.IsOfAConstructor && code.RunsInTheTurnOf(made));

        // A write that always runs gives the member only the new state. A function has the
        // members of the code that made it, and a callee can have the members of the object of
        // its caller. Each of the two then writes to that object, and its call can run no time. A
        // copy is for one call, and the caller says if that call runs: see GivesItsOwner.
        bool only = replaces && this.call is null or { HasItsOwnMembers: true } or { AlwaysRuns: true };

        this.thisMembers[key] =
            !only && this.thisMembers.TryGetValue(key: key, value: out LoopState? existing)
                ? existing.Join(state)
                : state;

        this.WritesThisInTheStore(key);

        // The member has only this state from now on: see OfThisObject.
        if (only && !this.quiet)
        {
            this.Frame.OwnWriteOf[key] = this.region.MemberWrites;
        }

        this.setOnThis[key] = only && from != null ? this.TheOneCollectionThatIs(from: from, state: state) : null;
    }

    // Records the one collection that a member of an object is after a write to the member. A
    // write that is not sure takes the record away, for each object that the owner can be.
    private void NotesTheCollectionIn(
        ExpressionSyntax? owner,
        LoopState has,
        ISymbol member,
        LoopState state,
        ExpressionSyntax? from,
        bool sure)
    {
        if (this.quiet || owner == null)
        {
            return;
        }

        ISymbol key = member.OriginalDefinition;

        MadeObject? one =
            sure && this.function == null && this.call is null or { AlwaysRuns: true }
                ? this.TheOneCollectionOf(owner)
                : null;

        foreach (MadeObject made in this.region.Objects.Now(has.Objects))
        {
            this.region.Objects.IsTheCollectionIn(owner: made, member: key, collection: null);
        }

        if (one != null)
        {
            this.region.Objects.IsTheCollectionIn(
                owner: one,
                member: key,
                collection: from == null ? null : this.TheOneCollectionThatIs(from: from, state: state));
        }
    }

    /// <summary>
    ///     The expression of the location of a name with ref, when the walk can
    ///     read that expression again and get the location. Null for each other
    ///     expression.
    /// </summary>
    /// <remarks>
    ///     The expression names the same location while no code gives a name in
    ///     it a value. Such a name is the local of the array, of the index, or of
    ///     the object. An assignment to the local that is the location is a
    ///     change to the location itself. A function can run at a different time,
    ///     thus the code of a function gets no location.
    /// </remarks>
    public ExpressionSyntax? TheLocationOf(ExpressionSyntax written)
    {
        if (this.function != null
            || this.Frame.LocationOf.Count == 0
            || Unwrapped(written) is not IdentifierNameSyntax use
            || this.region.SymbolOf(use) is not ILocalSymbol name
            || !this.Frame.LocationOf.TryGetValue(key: name, value: out ExpressionSyntax? location)
            || FunctionOf(use) is not { } code
            || FunctionOf(location) != code
            || use.SpanStart <= location.Span.End)
        {
            return null;
        }

        ExpressionSyntax target = Unwrapped(location);

        IEnumerable<IdentifierNameSyntax> parts =
            target is IdentifierNameSyntax ? [] : target.DescendantNodes().OfType<IdentifierNameSyntax>();

        return parts.Any(part =>
            this.region.SymbolOf(part) is { } symbol and (ILocalSymbol or IParameterSymbol)
            && this.GetsAValue(
                name: symbol,
                code: code,
                after: location.Span.End,
                before: use.SpanStart,
                orAFunctionUsesIt: false))
            ? null
            : location;
    }

    /// <summary>
    ///     For a local that is a second name of a value with no object of the
    ///     store: the expression of the first name. Null for each other
    ///     expression.
    /// </summary>
    /// <remarks>
    ///     A value that is no object of the store keeps the state of its members
    ///     in its name. A local that gets the value of a second local is one more
    ///     name of that value, and not a copy. Each read and each write of a
    ///     member through the second name then goes through the first name. That
    ///     is true while no code gives one of the two names a value. A function
    ///     can run at a different time. Thus, a record holds in the run of the
    ///     code that made it. It holds also in each caller after the call of
    ///     that run, and in each run that such code calls after the record. It
    ///     holds in no other run.
    /// </remarks>
    private ExpressionSyntax? TheFirstNameOf(ExpressionSyntax written) =>
        this.TheFirstNameOf(written: written, at: written);

    // The same, with the first name read at a given use. The location of a name with ref needs
    // it: the walk reads that location again at each use of the name with ref.
    private ExpressionSyntax? TheFirstNameOf(ExpressionSyntax written, SyntaxNode at)
    {
        if (this.Frame.FirstNameOf.Count == 0)
        {
            return null;
        }

        ExpressionSyntax? found = null;
        ExpressionSyntax use = written;

        // The first name can be a second name also. The number of names has an end.
        for (int level = 0; level < MaxOwners; level++)
        {
            if (Unwrapped(use) is not IdentifierNameSyntax name
                || this.region.SymbolOf(name) is not { } second
                || !this.Frame.FirstNameOf.TryGetValue(key: second, value: out ExpressionSyntax? first)
                || this.WindowsOfTheRecordOf(first: first, at: at) is not (var windows, var ran)
                || this.region.SymbolOf(first) is not { } firstName
                || this.GetsAValueIn(windows: windows, ran: ran, names: [firstName, second], use: at))
            {
                break;
            }

            found = first;
            use = first;
        }

        return found;
    }

    // Reads a location or a first name again for a use. TheFirstNameOf followed each record that
    // holds at that use. A record that it did not follow can hold at the location itself, because
    // the location comes before the use. Thus, the read of the location follows no record.
    private LoopState EvalAgainForAUse(ExpressionSyntax location)
    {
        ExpressionSyntax? outer = this.readForAUse;
        this.readForAUse = location;
        LoopState state = this.EvalAgain(location);
        this.readForAUse = outer;

        return state;
    }

    // The parts of code that run between the record of a first name and a use, and the functions
    // whose run made the record. A lambda or a local function that a call runs can give a local
    // of the code around it a value. That record starts at the end of the call, in the code that
    // has the call. The code of the record is the run of the use, or a caller of that run. Then,
    // the parts continue through each run that a call starts, up to the use.
    private (ImmutableArray<(SyntaxNode Code, int After, int Before)> Windows, ImmutableArray<SyntaxNode> Ran)?
        WindowsOfTheRecordOf(ExpressionSyntax first, SyntaxNode at)
    {
        SyntaxNode start = first;
        ImmutableArray<SyntaxNode> ran = ImmutableArray<SyntaxNode>.Empty;

        LoopStateEvaluator code =
            this.Frame.RunThatNamed.TryGetValue(key: first, value: out LoopStateEvaluator? run) ? run : this;

        // The calls from the run of the use up to the code of the block, which has no call.
        List<CallOfThisCode> calls = [];

        for (CallOfThisCode? up = this.call; up != null; up = up.Caller.call)
        {
            calls.Add(up);
        }

        while (code.call is { } call && !calls.Contains(call))
        {
            if (code.function is { } function)
            {
                ran = ran.Add(function);
            }

            start = call.Site;
            code = call.Caller;
        }

        List<(SyntaxNode Code, int After, int Before)> windows = [];
        int? after = start.Span.End;

        // Each call between them runs after the code before it in its run, and so do its arguments.
        for (int level = code.call is { } met ? calls.IndexOf(met) : calls.Count; level > 0; level--)
        {
            SyntaxNode site = calls[level - 1].Site;

            if (FunctionOf(site) is not { } caller)
            {
                return null;
            }

            windows.Add((caller, after ?? caller.SpanStart, site.Span.End));
            after = null;
        }

        if (FunctionOf(at) is not { } use)
        {
            return null;
        }

        windows.Add((use, after ?? use.SpanStart, at.SpanStart));

        return (windows.ToImmutableArray(), ran);
    }

    // True for a name whose own state can be without a write through a second name of its value.
    // The name was one of two names, or it is a second name whose first name the walk cannot
    // read here.
    private bool WasOneOfTwoNames(ExpressionSyntax written) =>
        (this.Frame.WasOneOfTwoNames.Count > 0 || this.Frame.FirstNameOf.Count > 0)
        && Unwrapped(written) is IdentifierNameSyntax name
        && this.region.SymbolOf(name) is { } symbol
        && (this.Frame.WasOneOfTwoNames.Contains(symbol)
            || (this.Frame.FirstNameOf.ContainsKey(symbol) && this.TheFirstNameOf(name) == null));

    // Records what an assignment to a local does to the names of a value with no object.
    private void NotesASecondName(ISymbol local, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet)
        {
            return;
        }

        // The local gets a different value. Each second name of its value keeps that value, with
        // the state that it has now.
        foreach (ISymbol second in this.Frame.FirstNameOf
                     .Where(pair =>
                         SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(pair.Value), y: local))
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            if (this.locals.TryGetValue(key: local, value: out LoopState? now))
            {
                this.locals[second] = now;
            }

            this.Frame.FirstNameOf.Remove(second);
        }

        // A function can give a local of the code around it a value. For that code, the assignment
        // is sure only when each call of the function up to that code always runs. A local of the
        // function itself has the same rule. At a different call, a write through its first name
        // only joins the state of the first name.
        bool sure = replaces && (this.function == null || this.call is { AlwaysRuns: true });

        // After an assignment that is not sure to run, the local can be a second name.
        if (this.Frame.FirstNameOf.TryGetValue(key: local, value: out ExpressionSyntax? first))
        {
            this.Frame.FirstNameOf.Remove(local);

            if (!sure && this.region.SymbolOf(first) is { } firstName)
            {
                this.Frame.WasOneOfTwoNames.Add(local);
                this.Frame.WasOneOfTwoNames.Add(firstName);
            }
        }

        // A read-only view of a list reads the list at each use. Thus, it is a second name of the list.
        if (sure && KeepsItsObject(local) && this.TheListOfAReadOnlyView(value) is { } viewed)
        {
            this.Frame.FirstNameOf[local] = viewed;
            this.Frame.RunThatNamed[viewed] = this;

            return;
        }

        if (!KeepsItsObject(local)
            || Unwrapped(value) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not ((ILocalSymbol or IParameterSymbol) and { } from)
            || !KeepsItsObject(from)
            || SymbolEqualityComparer.Default.Equals(x: from, y: local)
            || this.region.TypeOf(name) is not { } type
            || !this.CanBeAnObject(type)
            || !this.Eval(name).Objects.IsEmpty)
        {
            return;
        }

        if (sure)
        {
            this.Frame.FirstNameOf[local] = name;
            this.Frame.RunThatNamed[name] = this;
        }
        else
        {
            // The local can be a second name after an assignment that is not sure to run.
            this.Frame.WasOneOfTwoNames.Add(local);
            this.Frame.WasOneOfTwoNames.Add(from);
        }
    }

    /// <summary>
    ///     Records the location that a local got a value with no object of the
    ///     store from. The location is an element with a constant index, or a
    ///     field. Each read of that location gives the same value, thus each local
    ///     that got it is a name of one value.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the local then has only this value.</param>
    private void NotesANameOfALocation(ISymbol local, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet)
        {
            return;
        }

        this.Frame.ReadFrom.Remove(local);
        this.Frame.GaveTo.Remove(local);

        if (replaces
            && this.function == null
            && local is ILocalSymbol { RefKind: RefKind.None }
            && this.LocationKeyOf(value) != null
            && this.region.TypeOf(value) is { } type
            && this.CanBeAnObject(type)
            && IsOfNoObjectOfTheStore(this.Eval(value)))
        {
            this.Frame.ReadFrom[local] = value;
        }
    }

    /// <summary>
    ///     Records that a local is a name of the location that gets its value.
    ///     The location is an element with a constant index, or a field, as for
    ///     <see cref="NotesANameOfALocation" />. The local and the location have one
    ///     value with no object of the store after the assignment.
    /// </summary>
    /// <remarks>
    ///     The local keeps the location that it got its value from. Thus, this
    ///     record is apart from that one.
    /// </remarks>
    /// <param name="location">The target of the assignment.</param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the location then has only this value.</param>
    public void NotesALocalOfALocation(SyntaxNode location, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet
            || !replaces
            || this.function != null
            || location is not (ElementAccessExpressionSyntax or MemberAccessExpressionSyntax or IdentifierNameSyntax)
            || Unwrapped(value) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not ILocalSymbol { RefKind: RefKind.None } local
            || this.LocationKeyOf((ExpressionSyntax)location) == null
            || this.region.TypeOf(name) is not { } type
            || !this.CanBeAnObject(type)
            || !IsOfNoObjectOfTheStore(this.Eval(name)))
        {
            return;
        }

        this.Frame.GaveTo[local] = (ExpressionSyntax)location;
    }

    /// <summary>
    ///     The state of a local that got the value of a field, with the state
    ///     that the field has now.
    /// </summary>
    /// <remarks>
    ///     The local and the field are two names of one value with no object of
    ///     the store. An array that a static field holds is such a value. A put
    ///     into an element through the field changes the state of the field.
    ///     That is true also for a put that a callee does. Thus, a read of the
    ///     local gets that state also.
    ///     A call that gives the field a different value is the one that the
    ///     record does not see. Then, the local gets more states than it has,
    ///     and the result is a report and not a miss.
    /// </remarks>
    private LoopState WithItsField(ISymbol local, SyntaxNode use, LoopState state) =>
        this.TheFieldThatItNames(local) is { } field
        && this.IsStillTheLocationOf(name: local, location: field, use: use, aCallCounts: false)
            ? state.Join(this.EvalAgain(field))
            : state;

    // The field that a local got its value from, as NotesANameOfALocation records it. Null for a
    // local that got no value of a field.
    private ExpressionSyntax? TheFieldThatItNames(ISymbol local) =>
        this.Frame.ReadFrom.Count > 0
        && this.Frame.ReadFrom.TryGetValue(key: local, value: out ExpressionSyntax? field)
        && Unwrapped(field) is IdentifierNameSyntax or MemberAccessExpressionSyntax
        && this.LocationKeyOf(field) is { Index: < 0 }
            ? field
            : null;

    // True for a value whose members only a name keeps. The value is no object, or an object that
    // the store does not know.
    private static bool IsOfNoObjectOfTheStore(LoopState value) =>
        value.Objects.All(static one => ReferenceEquals(objA: one, objB: MadeObject.Unknown));

    // The state of a local that gets a value from a location, with the members that each other
    // name of that location has now. The local is a name of the same value.
    private LoopState WithTheOtherNames(ISymbol local, ExpressionSyntax? from, LoopState state)
    {
        if ((this.Frame.ReadFrom.Count == 0 && this.Frame.GaveTo.Count == 0)
            || from == null
            || !IsOfNoObjectOfTheStore(state))
        {
            return state;
        }

        foreach (ISymbol other in this.TheOtherNamesOf(location: from, at: from, self: local))
        {
            if (this.locals.TryGetValue(key: other, value: out LoopState? named))
            {
                state = state.Join(named);
            }
        }

        return state;
    }

    // Gives each other name of the location of an owner a write to a member through that owner.
    // The owner is a name of the location, or the location itself.
    private void GivesTheOtherNames(
        ExpressionSyntax? owner,
        SyntaxNode? at,
        ISymbol member,
        LoopState state,
        bool replaces,
        SyntaxNode? turn)
    {
        // A write in a function counts at the call of the function. The function has the frame of this
        // code, thus its own code changes the records of the names before the write. A call in a
        // different function is not in the code of the names, and the check then finds no name.
        if (this.function != null)
        {
            at = this.call?.Site;
        }

        if ((this.Frame.ReadFrom.Count == 0 && this.Frame.GaveTo.Count == 0)
            || this.quiet
            || owner == null
            || at == null)
        {
            return;
        }

        ISymbol? self =
            Unwrapped(owner) is IdentifierNameSyntax name && this.region.SymbolOf(name) is ILocalSymbol local
                ? local
                : null;

        // A local can be a name of the location that it got its value from, and of the location
        // that got its value.
        ExpressionSyntax?[] locations =
            self == null
                ? [owner]
                :
                [
                    this.Frame.ReadFrom.TryGetValue(key: self, value: out ExpressionSyntax? read) ? read : null,
                    this.Frame.GaveTo.TryGetValue(key: self, value: out ExpressionSyntax? gave) ? gave : null
                ];

        ISymbol[] others =
        [
            .. locations
                .OfType<ExpressionSyntax>()
                .Where(location => self == null || this.IsStillTheLocationOf(name: self, location: location, use: at))
                .SelectMany(location => this.TheOtherNamesOf(location: location, at: at, self: self))
                .Distinct(SymbolEqualityComparer.Default)
        ];

        foreach (ISymbol other in others)
        {
            LoopState known =
                this.locals.TryGetValue(key: other, value: out LoopState? existing) ? existing : LoopState.None;

            Construction had = known.Members ?? Construction.Empty;

            bool only =
                replaces
                    ? this.AlwaysRunsForAName(other)
                    : turn != null && IsMadeInEachTurn(name: other, turn: turn);

            this.locals[other] =
                known.WithMembers(had.With(member: member, state: only ? state : had.StateOf(member).Join(state)));

            this.NotesAChangeTo(other);
        }
    }

    // Each local, other than the given one, that is a name of the same location as the given
    // expression at a use.
    private ISymbol[] TheOtherNamesOf(ExpressionSyntax location, SyntaxNode at, ISymbol? self)
    {
        if (this.LocationKeyOf(location) is not { } key)
        {
            return [];
        }

        return
        [
            .. this.Frame.ReadFrom
                .Concat(this.Frame.GaveTo)
                .Where(pair =>
                    !SymbolEqualityComparer.Default.Equals(x: pair.Key, y: self)
                    && IsTheSameLocation(one: this.LocationKeyOf(pair.Value), other: key)
                    && this.IsStillTheLocationOf(name: pair.Key, location: pair.Value, use: at))
                .Select(static pair => pair.Key)
        ];
    }

    // The parts of a location that the walk can read again. They are the array or the field, the
    // owner of the field, and the index of the element. The array is in a local, in a parameter, or in a
    // field. The owner is null for a static field and for a field of this object. A static property
    // with no code is a static field. Null for each other expression.
    private (ISymbol Of, ISymbol? Owner, int Index)? LocationKeyOf(ExpressionSyntax written) =>
        Unwrapped(written) switch
        {
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
                when Unwrapped(element.Expression) is IdentifierNameSyntax array
                     && this.region.SymbolOf(array) is { } collection
                     && (collection is IFieldSymbol || KeepsItsObject(collection))
                     && this.ConstantIndexOf(indexes[0]) is { } index =>
                (collection, null, index),
            IdentifierNameSyntax name when this.region.SymbolOf(name) is IFieldSymbol field => (field, null, -1),
            (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var named
                when this.region.SymbolOf(named) is IPropertySymbol { IsStatic: true } property
                     && this.IsAStaticLocation(property) =>
                (property, null, -1),
            MemberAccessExpressionSyntax access when this.region.SymbolOf(access) is IFieldSymbol field =>
                Unwrapped(access.Expression) switch
                {
                    ThisExpressionSyntax => (field, null, -1),
                    _ when field.IsStatic => (field, null, -1),
                    IdentifierNameSyntax holder when this.region.SymbolOf(holder) is { } owner
                                                     && KeepsItsObject(owner) =>
                        (field, owner, -1),
                    _ => null
                },
            InvocationExpressionSyntax call => this.LocationReturnedBy(call),
            _ => null
        };

    // True for a static property of this compilation with no code. Each read of it gives the value of
    // the last write to it, as for a static field.
    private bool IsAStaticLocation(IPropertySymbol property) =>
        !property.DeclaringSyntaxReferences.IsEmpty && this.IsALocation(property);

    // The location that a call gives the value of, as a read of it at the call does. A parameter
    // of the callee there is its argument, and an array or an owner of a field must be a name of
    // the caller. Null when the callee returns no location.
    private (ISymbol Of, ISymbol? Owner, int Index)? LocationReturnedBy(InvocationExpressionSyntax call)
    {
        if (this.ReturnedBy(call) is not ({ } returned, var callee)
            || Unwrapped(returned) is InvocationExpressionSyntax
            || this.LocationKeyOf(returned) is not { } key)
        {
            return null;
        }

        ISymbol? of = this.AtTheCall(part: key.Of, call: call, callee: callee);
        ISymbol? owner = key.Owner == null ? null : this.AtTheCall(part: key.Owner, call: call, callee: callee);

        // A field of the object of an instance method is a field of this object only for a call
        // on this object.
        bool onThis =
            callee is not { IsStatic: false }
            || Unwrapped(call.Expression) is IdentifierNameSyntax or GenericNameSyntax
                or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };

        return of == null
               || (key.Owner != null && owner == null)
               || (!onThis && key is { Owner: null, Of: IFieldSymbol { IsStatic: false } })
            ? null
            : (of, owner, key.Index);
    }

    // A part of a location that a callee returns, as a name of the caller. A parameter of the
    // callee gives the name that is its argument. A local of a method is no name of the caller.
    // A lambda reads the locals of the code around it, thus its names are names of the caller.
    private ISymbol? AtTheCall(ISymbol part, InvocationExpressionSyntax call, IMethodSymbol? callee)
    {
        if (callee == null || part is IFieldSymbol)
        {
            return part;
        }

        if (part is not IParameterSymbol parameter
            || !SymbolEqualityComparer.Default.Equals(
                x: parameter.ContainingSymbol,
                y: (callee.ReducedFrom ?? callee).OriginalDefinition))
        {
            return null;
        }

        ExpressionSyntax? argument =
            callee.ReducedFrom != null
                ? parameter.Ordinal == 0
                    ? (call.Expression as MemberAccessExpressionSyntax)?.Expression
                    : ArgumentAt(ordinal: parameter.Ordinal - 1)
                : ArgumentAt(ordinal: parameter.Ordinal);

        return argument != null
               && Unwrapped(argument) is IdentifierNameSyntax name
               && this.region.SymbolOf(name) is { } given
               && (given is IFieldSymbol || KeepsItsObject(given))
            ? given
            : null;

        ExpressionSyntax? ArgumentAt(int ordinal) =>
            call.ArgumentList.Arguments.Any(static each => each.NameColon != null)
            || ordinal >= call.ArgumentList.Arguments.Count
                ? null
                : call.ArgumentList.Arguments[ordinal].Expression;
    }

    /// <summary>
    ///     The expression that a call returns, when the code of the callee is only
    ///     a return of it, with the callee. The callee is a method of this
    ///     compilation that no override can replace. For a transaction, it is the
    ///     function that the transaction runs. Its callee is null for a lambda.
    /// </summary>
    private (ExpressionSyntax? Returned, IMethodSymbol? Callee) ReturnedBy(InvocationExpressionSyntax call)
    {
        if (this.region.SymbolOf(call) is not IMethodSymbol method)
        {
            return (null, null);
        }

        if (this.region.Symbols.IsSodaFlow(method))
        {
            if (!OperatorTable.RunsFunctionOneTime(method)
                || call.ArgumentList.Arguments.Count != 1
                || call.ArgumentList.Arguments[0].Expression is not { } function)
            {
                return (null, null);
            }

            return function switch
            {
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0 } lambda =>
                    (OnlyReturnOf(lambda.Body), null),
                IdentifierNameSyntax or MemberAccessExpressionSyntax
                    when this.region.SymbolOf(function) is IMethodSymbol { Parameters.Length: 0 } group =>
                    ReturnedByMethod(group),
                _ => (null, null)
            };
        }

        return ReturnedByMethod(method);

        static (ExpressionSyntax?, IMethodSymbol?) ReturnedByMethod(IMethodSymbol method) =>
            method is { IsVirtual: false, IsAbstract: false, IsOverride: false }
                ? (OnlyReturnOf(BodyOf((method.ReducedFrom ?? method).OriginalDefinition)), method)
                : (null, null);

        static ExpressionSyntax? OnlyReturnOf(SyntaxNode? body) =>
            body switch
            {
                ExpressionSyntax expression => expression,
                BlockSyntax { Statements: { Count: 1 } statements } =>
                    (statements[0] as ReturnStatementSyntax)?.Expression,
                _ => null
            };
    }

    // True when a local is still a name of the location that it got its value from, at a use. No
    // code between the two gave the local, the array, the field, or the owner a value. No code
    // between them can give the location itself a value. The last argument is false where a call
    // of code of this compilation does not count as such code: see WithItsField.
    private bool IsStillTheLocationOf(
        ISymbol name,
        ExpressionSyntax location,
        SyntaxNode use,
        bool aCallCounts = true)
    {
        if (FunctionOf(location) is not { } code
            || FunctionOf(use) != code
            || location.Span.End > use.SpanStart
            || this.LocationKeyOf(location) is not { } key)
        {
            return false;
        }

        int after = location.Span.End;
        int before = use.SpanStart;

        return new[] { name, key.Of, key.Owner }.All(part =>
                   part == null
                   || !this.GetsAValue(name: part, code: code, after: after, before: before, orAFunctionUsesIt: false))
               && !code.DescendantNodes(TextSpan.FromBounds(start: after, end: before))
                   .Any(node => node.SpanStart > after
                                && node.SpanStart < before
                                && this.CanChangeTheLocation(
                                    node: node,
                                    key: key,
                                    name: name,
                                    aCallCounts: aCallCounts));
    }

    // True for code that can give a location a value. It is an assignment, a deconstruction, or a
    // use with ref of the location. It is a call that gets the array or the owner, or a call of a
    // delegate or of a local function. For a location in a field, each call of code of this
    // compilation can change it also. An assignment of the name itself keeps the name a name of
    // the location.
    private bool CanChangeTheLocation(
        SyntaxNode node,
        (ISymbol Of, ISymbol? Owner, int Index) key,
        ISymbol name,
        bool aCallCounts)
    {
        if (GivesAValue(node) && this.IsAnAssignmentOf(target: node, name: name))
        {
            return false;
        }

        // An assignment to a name with ref is an assignment to its location. A location that the
        // walk cannot read again can be this one.
        if (node is IdentifierNameSyntax aliased && GivesAValue(aliased))
        {
            return this.region.SymbolOf(aliased) is ILocalSymbol { RefKind: not RefKind.None } named
                   && (!this.Frame.LocationOf.TryGetValue(key: named, value: out ExpressionSyntax? location)
                       || IsTheSameLocation(one: this.LocationKeyOf(location), other: key));
        }

        (ExpressionSyntax? receiver, ArgumentListSyntax? arguments) =
            node switch
            {
                ElementAccessExpressionSyntax or MemberAccessExpressionSyntax when GivesAValue(node) =>
                    ((ExpressionSyntax)node, null),
                InvocationExpressionSyntax call =>
                    ((call.Expression as MemberAccessExpressionSyntax)?.Expression, call.ArgumentList),
                BaseObjectCreationExpressionSyntax creation => (null, creation.ArgumentList),
                _ => (null, null)
            };

        if (arguments == null)
        {
            return receiver != null && IsTheSameLocation(one: this.LocationKeyOf(receiver), other: key);
        }

        // A callee that only returns a location changes nothing.
        if (node is InvocationExpressionSyntax read && this.LocationReturnedBy(read) != null)
        {
            return false;
        }

        return (receiver != null && this.IsAPartOf(expression: receiver, key: key))
               || arguments.Arguments.Any(argument => this.IsAPartOf(expression: argument.Expression, key: key))
               || (aCallCounts
                   && this.region.SymbolOf(node) is IMethodSymbol method
                   && (method.MethodKind is MethodKind.DelegateInvoke or MethodKind.LocalFunction
                       || (key is { Owner: null, Of: IFieldSymbol or IPropertySymbol }
                           && !method.DeclaringSyntaxReferences.IsEmpty)));
    }

    // True for the target of an assignment whose value is the given name.
    private bool IsAnAssignmentOf(SyntaxNode target, ISymbol name) =>
        target.Parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment
        && assignment.Left == target
        && Unwrapped(assignment.Right) is IdentifierNameSyntax value
        && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(value), y: name);

    // True for an expression that is the array or the owner of a location. A call that gets the
    // value of a field cannot give the field a value, thus the field itself is no such part.
    private bool IsAPartOf(ExpressionSyntax expression, (ISymbol Of, ISymbol? Owner, int Index) key) =>
        Unwrapped(expression) is IdentifierNameSyntax name
        && this.region.SymbolOf(name) is { } symbol
        && ((key.Index >= 0 && SymbolEqualityComparer.Default.Equals(x: symbol, y: key.Of))
            || SymbolEqualityComparer.Default.Equals(x: symbol, y: key.Owner));

    // The location that an expression reads, as a key of KeptAt. The key has the element or the
    // field, the local that names it, and the member of the value there. Null when the walk cannot
    // read that location again.
    private (ExpressionSyntax Location, ISymbol? Name, ISymbol? Member)? KeptKeyOf(ExpressionSyntax expression) =>
        Unwrapped(expression) switch
        {
            MemberAccessExpressionSyntax access
                when this.region.SymbolOf(access) is { } member
                     && this.IsALocation(member)
                     && this.KeptKeyOf(access.Expression) is (var location, var name, null) =>
                (location, name, member.OriginalDefinition),
            IdentifierNameSyntax local
                when this.region.SymbolOf(local) is ILocalSymbol named
                     && this.Frame.ReadFrom.TryGetValue(key: named, value: out ExpressionSyntax? from) =>
                (from, named, null),
            (ElementAccessExpressionSyntax or MemberAccessExpressionSyntax or IdentifierNameSyntax) and var written
                when this.LocationKeyOf(written) != null =>
                (written, null, null),
            _ => null
        };

    /// <summary>
    ///     What code with no body keeps for the location that an expression
    ///     reads, from <see cref="LoopFrame.KeptAt" />. Only the code between the
    ///     two cannot give the location a new value.
    /// </summary>
    private LoopState KeptThrough(ExpressionSyntax expression)
    {
        if (this.function != null
            || this.KeptKeyOf(expression) is not { } read
            || this.LocationKeyOf(read.Location) is not { } at)
        {
            return LoopState.None;
        }

        LoopState kept = LoopState.None;

        foreach ((ExpressionSyntax location, ISymbol? name, ISymbol? member, LoopState state) in this.Frame.KeptAt)
        {
            if (SymbolEqualityComparer.Default.Equals(x: member, y: read.Member)
                && IsTheSameLocation(one: this.LocationKeyOf(location), other: at)
                && this.IsStillTheLocationOf(name: name ?? at.Of, location: location, use: expression)
                && (read.Name == null
                    || this.IsStillTheLocationOf(name: read.Name, location: read.Location, use: expression)))
            {
                kept = kept.Join(state);
            }
        }

        return kept;
    }

    private static bool IsTheSameLocation(
        (ISymbol Of, ISymbol? Owner, int Index)? one,
        (ISymbol Of, ISymbol? Owner, int Index) other) =>
        one is { } key
        && key.Index == other.Index
        && SymbolEqualityComparer.Default.Equals(x: key.Of, y: other.Of)
        && SymbolEqualityComparer.Default.Equals(x: key.Owner, y: other.Owner);

    /// <summary>
    ///     Records that this code put a value into an element or into a field. A
    ///     parameter with ref, out, or in of this code can be a name of that
    ///     location. Then, the parameter has that value after the put.
    /// </summary>
    /// <param name="field">The field, or null for an element.</param>
    /// <param name="element">The element, or null for a field.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="owner">The expression of the object of a field.</param>
    public void PutsIntoALocation(
        ISymbol? field,
        ElementAccessExpressionSyntax? element,
        LoopState state,
        ExpressionSyntax? owner = null) =>
        this.PutsIntoALocation(
            field: field?.OriginalDefinition,
            index: element is { ArgumentList.Arguments: { Count: 1 } indexes } ? indexes[0] : null,
            state: state,
            owner: element?.Expression ?? owner);

    // The same for a put of this code or of a callee. The caller can have the object of a
    // parameter. Thus, the frame keeps each put into an element or a field of that object for the
    // caller: see Leave.
    private void PutsIntoALocation(ISymbol? field, ArgumentSyntax? index, LoopState state, ExpressionSyntax? owner)
    {
        // A read through the parameter can get each value that has a state, as it gets an object. A
        // pending cell or a function is such a value.
        if (this.quiet || state is { IsNone: true, Objects.IsEmpty: true } || this.bound.Count == 0)
        {
            return;
        }

        if (this.bound.Keys.Any(static name => name is IParameterSymbol { RefKind: not RefKind.None }))
        {
            this.Frame.PutsIntoLocations.Add((field, index, state));
        }

        if (owner != null && this.NameUnder(owner) is IParameterSymbol parameter && this.bound.ContainsKey(parameter))
        {
            this.Frame.PutsThroughParameters.Add((field, index, state, parameter));
        }
    }

    /// <summary>
    ///     Gives this code each put of a callee into an element or into a field
    ///     of the object of a parameter. The argument for that parameter has the
    ///     same object, thus the put is a put of this code through the argument.
    /// </summary>
    /// <param name="callee">The frame of the callee, at the end of its walk.</param>
    /// <param name="argumentOf">The argument for each parameter of the callee.</param>
    private void GetsThePutsOf(LoopFrame callee, IReadOnlyDictionary<ISymbol, Binding> argumentOf)
    {
        foreach ((ISymbol? field, ArgumentSyntax? index, LoopState value, ISymbol parameter)
                 in callee.PutsThroughParameters)
        {
            if (argumentOf.TryGetValue(key: parameter, value: out Binding binding))
            {
                this.PutsIntoALocation(field: field, index: index, state: value, owner: binding.Argument);
            }
        }
    }

    // The state of a parameter with ref, out, or in, where the walk does not read its location in
    // the code of the caller. The location has no object that the store knows, or this code gave
    // the parameter a value. A put of this code into an element or a field after that can be a put
    // into the location of the parameter. Then, the parameter is the object of that put. The walk
    // does not know which of them it is. Thus, the value can be an object that the store does not
    // know also. A write through the parameter is then sure for no object.
    private LoopState WithThePutsOfThisCode(IParameterSymbol parameter, LoopState state)
    {
        List<(ISymbol? Field, ArgumentSyntax? Index, LoopState Value)> puts = this.Frame.PutsIntoLocations;

        int first =
            this.Frame.PutsBeforeAnAssignment.TryGetValue(key: parameter, value: out int before) ? before : 0;

        if (puts.Count <= first)
        {
            return state;
        }

        ExpressionSyntax? location =
            this.call != null
            && this.call.Locations.TryGetValue(
                key: parameter,
                value: out (LoopStateEvaluator Reader, ExpressionSyntax Location) of)
                ? Unwrapped(of.Location)
                : null;

        // An argument for a parameter with in can be a copy, and a copy is no location.
        if (location == null && parameter.RefKind is not (RefKind.Ref or RefKind.Out))
        {
            return state;
        }

        LoopState all = state;

        for (int i = first; i < puts.Count; i++)
        {
            if (this.CanBeTheLocationOf(location: location, field: puts[i].Field, index: puts[i].Index))
            {
                all = all.Join(puts[i].Value.AsElement).OrAnUnknownObject;
            }
        }

        return all;
    }

    // False when a put into a field or an element is sure to be no put into the location of an
    // argument. The location is then a local, or the two are an element and a field. Two fields
    // are two locations also, and two elements with two constant indexes are. The walk does not
    // know the location of an argument that it cannot read again, and that location can be each
    // one.
    private bool CanBeTheLocationOf(ExpressionSyntax? location, ISymbol? field, ArgumentSyntax? index) =>
        location switch
        {
            null => true,
            ElementAccessExpressionSyntax { ArgumentList.Arguments: var indexes } =>
                field == null
                && (index == null
                    || indexes.Count != 1
                    || this.ConstantIndexOf(indexes[0]) is not { } one
                    || this.ConstantIndexOf(index) is not { } other
                    || one == other),
            MemberAccessExpressionSyntax or IdentifierNameSyntax =>
                field != null
                && SymbolEqualityComparer.Default.Equals(
                    x: this.region.SymbolOf(location)?.OriginalDefinition,
                    y: field),
            _ => false
        };

    /// <summary>
    ///     The locations that a call of a method that returns a ref can give,
    ///     in the code of the caller. Each is the argument for a parameter that
    ///     the method returns, or an element of that argument. The walk does not
    ///     know the position of such an element. Empty for each other location.
    /// </summary>
    /// <param name="call">The call that is the target of an assignment.</param>
    public IReadOnlyList<(ExpressionSyntax Argument, bool IsAnElement)> LocationsOfARefReturn(
        InvocationExpressionSyntax call)
    {
        if (this.region.SymbolOf(call) is not IMethodSymbol { ReturnsByRef: true } method
            || BodyOf((method.ReducedFrom ?? method).OriginalDefinition) is not { } body)
        {
            return [];
        }

        IReadOnlyList<Binding> bindings =
            this.Bind(
                definition: (method.ReducedFrom ?? method).OriginalDefinition,
                arguments: call.ArgumentList.Arguments,
                extensionReceiver: method.ReducedFrom != null
                    ? ReceiverOf(symbols: this.region.Symbols, invocation: call, method: method)
                    : null);

        List<(ExpressionSyntax Argument, bool IsAnElement)> locations = [];

        foreach (ExpressionSyntax returned in body is ExpressionSyntax only ? [only] : ReturnExpressions(body))
        {
            (ExpressionSyntax name, bool isAnElement) =
                Unwrapped(this.SelectedLocation(returned)) is ElementAccessExpressionSyntax element
                    ? (Unwrapped(element.Expression), true)
                    : (Unwrapped(this.SelectedLocation(returned)), false);

            // A static field through the name of its type is the same location in the caller.
            if (name is MemberAccessExpressionSyntax && this.IsAStaticField(name))
            {
                locations.Add((name, isAnElement));

                continue;
            }

            if (name is not IdentifierNameSyntax || this.region.SymbolOf(name) is not { } returnedName)
            {
                continue;
            }

            // A local function or a lambda can return a ref to a local of the code around it. Each
            // method can return a ref to a static field. The name is the same in the caller.
            if (returnedName is ILocalSymbol or IFieldSymbol { IsStatic: true })
            {
                locations.Add((name, isAnElement));

                continue;
            }

            if (returnedName is not IParameterSymbol parameter)
            {
                continue;
            }

            locations.AddRange(
                bindings
                    .Where(binding => SymbolEqualityComparer.Default.Equals(x: binding.Parameter, y: parameter))
                    .Select(binding => (binding.Argument, isAnElement)));
        }

        return locations;
    }

    /// <summary>
    ///     The members of its object that an indexer or a method of this
    ///     compilation returns a ref to, as <c>ref this.cells[index]</c> does.
    ///     Each is the member, with true for an element of it. Empty for each
    ///     other location.
    /// </summary>
    /// <param name="definition">The getter of the indexer, or the method.</param>
    public IReadOnlyList<(ISymbol Member, bool IsAnElement)> MembersOfARefReturn(IMethodSymbol definition)
    {
        if (BodyOf(definition) is not { } body)
        {
            return [];
        }

        List<(ISymbol Member, bool IsAnElement)> members = [];

        foreach (ExpressionSyntax returned in body is ExpressionSyntax only ? [only] : ReturnExpressions(body))
        {
            (ExpressionSyntax name, bool isAnElement) =
                Unwrapped(returned) is ElementAccessExpressionSyntax element
                    ? (Unwrapped(element.Expression), true)
                    : (Unwrapped(returned), false);

            if (name is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
                && this.region.SymbolOf(name) is { IsStatic: false } member and (IFieldSymbol or IPropertySymbol))
            {
                members.Add((member, isAnElement));
            }
        }

        return members;
    }

    /// <summary>
    ///     Records that a value went into an element of a member of an object,
    ///     with no expression for that element. An indexer with a ref return can
    ///     give such an element. The walk does not know the position.
    /// </summary>
    /// <param name="owner">The expression of the object.</param>
    /// <param name="member">The member that holds the collection.</param>
    /// <param name="state">The state of the value.</param>
    public void PutsIntoAnElementOf(ExpressionSyntax owner, ISymbol member, LoopState state)
    {
        if (this.quiet)
        {
            return;
        }

        LoopState ofTheOwner = this.Eval(owner);

        LoopState collection =
            this.WithTheObjectsInTheMember(
                owner: ofTheOwner,
                member: member,
                state: StateOfAMember(
                    owner: ofTheOwner,
                    member: member,
                    implementations: this.region.Symbols.Implementations));

        foreach (MadeObject made in collection.Objects)
        {
            this.region.Objects.HasNoPositions(made);
        }

        LoopState put = state.AsContainer.WithNoObjects;

        this.Change(
            owner: collection,
            change: old => old.Join(put).HoldingWhatCameWith(state),
            replaces: false,
            turn: null);

        // The member keeps the collection, which holds the value now.
        this.SetMemberOf(owner: owner, member: member, state: collection.Join(put), replaces: false, turn: null);
    }

    /// <summary>
    ///     The collection of the element that a name with ref is for. It is for
    ///     a name whose location the walk cannot read again: see
    ///     <see cref="TheLocationOf" />. A value that goes to such a name goes into
    ///     that collection, at a position that the walk does not know. Null for
    ///     each other expression.
    /// </summary>
    public ExpressionSyntax? TheCollectionOfARef(ExpressionSyntax written) =>
        Unwrapped(written) is IdentifierNameSyntax use
        && this.region.SymbolOf(use) is ILocalSymbol { RefKind: not RefKind.None } name
        && this.Frame.ElementOf.TryGetValue(key: name, value: out ExpressionSyntax? collection)
            ? collection
            : null;

    /// <summary>
    ///     Records that a collection got a value through a name with ref for one
    ///     of its elements. The walk does not know the position. The ref to the
    ///     element took the positions of the collection away.
    /// </summary>
    /// <param name="collection">The result of <see cref="TheCollectionOfARef" /> for the name.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="value">The expression of the value, when the code has one.</param>
    public void GetsThroughARef(ExpressionSyntax collection, LoopState state, ExpressionSyntax? value)
    {
        if (value != null)
        {
            this.Gets(collection: collection, value: value);
        }
        else
        {
            this.GetsAState(collection: collection, put: state);
        }

        this.Absorb(target: collection, state: state);
    }

    /// <summary>
    ///     True for a <c>ref</c> to an element in the declaration of a local with
    ///     ref. The walk must read that location at each use of the local. Each
    ///     assignment to the local is then an assignment to the element, and the
    ///     positions of the collection stay.
    /// </summary>
    /// <remarks>
    ///     A ref local is no name in a function of its code. A name in the location
    ///     that gets a value after the declaration makes a different location.
    ///     Then, an assignment to the local goes to the collection, which is not
    ///     sure to be the collection of the location.
    /// </remarks>
    public bool KeepsThePositionsAt(RefExpressionSyntax reference) =>
        this.function == null
        && reference.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
        && Unwrapped(reference.Expression) is ElementAccessExpressionSyntax location
        && this.IsALocationThatTheWalkCanReadAgain(location)
        && FunctionOf(declarator) is { } code
        && !location.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(part =>
                this.region.SymbolOf(part) is { } symbol and (ILocalSymbol or IParameterSymbol)
                && this.GetsAValue(
                    name: symbol,
                    code: code,
                    after: reference.Span.End,
                    before: int.MaxValue,
                    orAFunctionUsesIt: false));

    // The location of a ref. A conditional ref on a constant is the location that the constant
    // selects. The walk does not know the location of a conditional ref on a different condition.
    private ExpressionSyntax SelectedLocation(ExpressionSyntax location)
    {
        while (Unwrapped(location) is ConditionalExpressionSyntax
               {
                   WhenTrue: RefExpressionSyntax whenTrue, WhenFalse: RefExpressionSyntax whenFalse
               } conditional
               && SinkWalker.ConstantOf(region: this.region, condition: conditional.Condition) is { } always)
        {
            location = always ? whenTrue.Expression : whenFalse.Expression;
        }

        return location;
    }

    // True for a static field. A local with ref for it reads the field again at each use. Thus, it
    // gets each value that code gives the field after the declaration.
    private bool IsAStaticField(ExpressionSyntax location) =>
        Unwrapped(location) is (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var named
        && this.region.SymbolOf(named) is IFieldSymbol { IsStatic: true };

    // True for the expression of a location that TheLocationOf can give. That is a local, an
    // element of an array in a local, or a field of an object in a local or of this object. An
    // element of a view of such an array is one also.
    // An element of a ref indexer of the code, on a local, at a constant index or at an index in a
    // local. The walk reads that ref again at each use of a ref local of it.
    private bool IsARefIndexerOfALocal(ExpressionSyntax location) =>
        Unwrapped(location) is ElementAccessExpressionSyntax
        {
            ArgumentList.Arguments: { Count: 1 } indexes,
            Expression: IdentifierNameSyntax holder
        } element
        && this.region.SymbolOf(element) is IPropertySymbol { ReturnsByRef: true }
        && this.region.SymbolOf(holder) is { } local
        && KeepsItsObject(local)
        && (indexes[0].Expression is LiteralExpressionSyntax
            || (indexes[0].Expression is IdentifierNameSyntax index
                && this.region.SymbolOf(index) is { } number
                && KeepsItsObject(number)));

    // A field of an element of an array in a static field, at a constant index or at an index in a
    // local. A ref local of it in a method is a name of that field, which the caller then sees through
    // the static field.
    private bool IsAFieldOfAnElementOfAStaticArray(ExpressionSyntax location) =>
        Unwrapped(location) is MemberAccessExpressionSyntax
        {
            Expression: ElementAccessExpressionSyntax
            {
                ArgumentList.Arguments: { Count: 1 } indexes,
                Expression: IdentifierNameSyntax array
            }
        } access
        && this.region.SymbolOf(access) is IFieldSymbol { IsStatic: false }
        && (indexes[0].Expression is LiteralExpressionSyntax
            || (indexes[0].Expression is IdentifierNameSyntax index
                && this.region.SymbolOf(index) is { } number
                && KeepsItsObject(number)))
        && this.region.SymbolOf(array) is IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol };

    private bool IsALocationThatTheWalkCanReadAgain(ExpressionSyntax location) =>
        Unwrapped(location) switch
        {
            IdentifierNameSyntax name => this.region.SymbolOf(name) is { } local && KeepsItsObject(local),
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element =>
                ((Unwrapped(element.Expression) is IdentifierNameSyntax array
                  && this.region.SymbolOf(array) is { } local
                  && KeepsItsObject(local)
                  && this.region.TypeOf(array) is IArrayTypeSymbol)
                 || this.TheArrayOfAViewOf(element) != null)
                && (indexes[0].Expression is LiteralExpressionSyntax
                    || (indexes[0].Expression is IdentifierNameSyntax index
                        && this.region.SymbolOf(index) is { } number
                        && KeepsItsObject(number))),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } own =>
                this.region.SymbolOf(own) is IFieldSymbol { IsStatic: false },
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax holder } access =>
                this.region.SymbolOf(access) is IFieldSymbol { IsStatic: false }
                && this.region.SymbolOf(holder) is { } local
                && KeepsItsObject(local),
            _ => false
        };

    /// <summary>
    ///     Records the content of a send for the next read of the block. That
    ///     read gives the content to the sink from the start. Thus, a value that
    ///     the code made from the sink before the send gets it also.
    /// </summary>
    /// <remarks>
    ///     The sink is an object of the store, through each name of it. A sink
    ///     that the walk did not see made has only the name of the send.
    /// </remarks>
    /// <param name="sink">The receiver of the send.</param>
    /// <param name="content">The state of the sink after the send of the value.</param>
    public void Sends(ExpressionSyntax sink, LoopState content)
    {
        // An evaluator that reads a posted function changes nothing else. The send in that
        // function comes after the close, thus no part of its content is pending.
        bool later = this.region.ReadsCodeAfterTheTransaction;

        // A send of no content also counts for the coalesce function of a sink.
        if ((this.quiet && !later) || (content.IsNone && !this.region.Sent.HasCoalescing))
        {
            return;
        }

        if (later)
        {
            content = content.AfterTheClose;
        }

        bool toAnObject = false;
        ImmutableArray<MadeObject> sinks = this.region.Objects.Now(this.Eval(sink).Objects);

        foreach (MadeObject made in sinks)
        {
            if (this.region.Objects.CreationOfASink(made) is { } creation)
            {
                if (this.region.Sent.Coalesces(creation))
                {
                    this.region.Sent.Count(
                        creation: creation,
                        inTheBlock: sinks.Length == 1 ? this.SendOfTheBlock(sink) : null,
                        content: content);
                }

                if (!content.IsNone)
                {
                    this.region.Sent.Add(creation: creation, content: content);
                }

                toAnObject = true;
            }
        }

        if (content.IsNone)
        {
            return;
        }

        if (!toAnObject
            && Unwrapped(sink) is IdentifierNameSyntax or MemberAccessExpressionSyntax
            && this.region.SymbolOf(Unwrapped(sink))
                is { } name and (ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol))
        {
            this.region.Sent.Add(name: name.OriginalDefinition, content: content);
        }
    }

    // A sink that a call makes is an object of the store. It has the content that the sends of
    // the read before gave it.
    private LoopState ASink(InvocationExpressionSyntax creation, IMethodSymbol method, LoopState state)
    {
        LoopState made =
            this.MadeHere(
                place: creation,
                state: state.Join(this.SentBefore(creation: creation, method: method)),
                exact: false);

        foreach (MadeObject sink in made.Objects)
        {
            this.region.Objects.IsASink(made: sink, creation: creation);
        }

        return made;
    }

    // A name of a sink whose creation the walk did not see has the content that the sends of the
    // read before gave it.
    private LoopState WithTheSendsBefore(SyntaxNode name, LoopState state) =>
        this.region.SentBefore.HasNames && this.region.SymbolOf(name) is { } symbol
            ? state.Join(this.region.SentBefore.To(symbol.OriginalDefinition))
            : state;

    /// <summary>
    ///     The state of a delegate after a remove, as in <c>handlers -= handler</c>,
    ///     that takes away the last occurrence of a lambda. Null when the walk
    ///     is not sure of that, and the delegate keeps the function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A remove takes away one occurrence of a delegate that is equal to
    ///         the one that it gets. The state of a delegate is a set of
    ///         functions. It has no count, and it does not tell two instances
    ///         apart. Thus, the walk counts here, only where the code shows each
    ///         occurrence.
    ///     </para>
    ///     <para>
    ///         The value is a local with one lambda. No loop and no branch is
    ///         around its declaration, and it gets no second value. Thus, it is
    ///         one instance in each run of the code. Each use of it adds it to
    ///         the location, removes it from the location, or calls it.
    ///     </para>
    ///     <para>
    ///         The location is a local of the code, or an event of a local that
    ///         has a new object. Each other use of the location adds a value,
    ///         removes a value, or calls it. Each add and each remove of the
    ///         lambda has no loop, branch, or function around it. For an event,
    ///         code between the first add and the remove must not get to the
    ///         object.
    ///     </para>
    /// </remarks>
    /// <param name="remove">The assignment with <c>-=</c>.</param>
    public LoopState? AfterTheLastRemove(AssignmentExpressionSyntax remove)
    {
        if (this.quiet
            || Unwrapped(remove.Right) is not IdentifierNameSyntax handler
            || this.region.SymbolOf(handler) is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
            || Unwrapped(value) is not AnonymousFunctionExpressionSyntax lambda
            || FunctionOf(remove) is not { } code
            || code.DescendantNodes().OfType<LabeledStatementSyntax>().Any()
            || !RunsStraightIn(node: declarator, code: code)
            || this.GetsASecondValue(local: local, scope: code)
            || this.UsesOfTheLocation(location: remove.Left, code: code) is not (var changes, var start))
        {
            return null;
        }

        int count = 0;
        SyntaxNode? first = null;
        HashSet<SyntaxNode> ofTheLambda = [];

        foreach (IdentifierNameSyntax use in code.DescendantNodes()
                     .OfType<IdentifierNameSyntax>()
                     .Where(use =>
                         use.Identifier.ValueText == local.Name
                         && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: local)))
        {
            if (FunctionOf(use) != code)
            {
                return null;
            }

            switch (use.Parent)
            {
                case InvocationExpressionSyntax call when call.Expression == use:
                    continue;
                case AssignmentExpressionSyntax change when change.Right == use && changes.Contains(change):
                {
                    if (!RunsStraightIn(node: change, code: code))
                    {
                        return null;
                    }

                    ofTheLambda.Add(change);

                    if (change.SpanStart < remove.SpanStart)
                    {
                        count += change.IsKind(SyntaxKind.AddAssignmentExpression) ? 1 : -1;
                        first ??= change;
                    }

                    continue;
                }
            }

            // An operand of the sum that is the first value of the local location.
            SyntaxNode sum =
                use.Ancestors()
                    .TakeWhile(static around =>
                        around is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression }
                            or ParenthesizedExpressionSyntax)
                    .LastOrDefault()
                ?? use;

            if (start == null || sum.Parent != start.Initializer)
            {
                return null;
            }

            count++;
            first ??= start;
        }

        // The remove takes away the last occurrence. A call between the first add and the remove
        // can copy the delegate into its location again, but only through a name of the location.
        // Code names a local location only to add, remove, or call: see UsesOfTheLocation. For an
        // event, see OnlyTheEventReaches.
        if (count != 1
            || first == null
            || (start == null
                && code.DescendantNodes(descendIntoChildren: node =>
                        node == code || node is not AnonymousFunctionExpressionSyntax)
                    .Any(node =>
                        node.SpanStart >= first.Span.End
                        && node.Span.End <= remove.SpanStart
                        && !node.AncestorsAndSelf().Any(ofTheLambda.Contains)
                        && node is InvocationExpressionSyntax
                            or BaseObjectCreationExpressionSyntax
                            or AssignmentExpressionSyntax
                            or AwaitExpressionSyntax
                            or PrefixUnaryExpressionSyntax
                            or PostfixUnaryExpressionSyntax
                            or ElementAccessExpressionSyntax
                            or MemberAccessExpressionSyntax)
                && !this.OnlyTheEventReaches(remove: remove, changes: changes)))
        {
            return null;
        }

        return this.Eval(remove.Left).WithoutFunctionsOf(lambda);
    }

    // True when code before a remove from an event of a local object gets to the object only to add
    // or remove a handler. Thus, no code between the add and the remove can copy the handlers. The
    // constructor must keep the object: see KeepsItsObject.
    private bool OnlyTheEventReaches(AssignmentExpressionSyntax remove, ICollection<SyntaxNode> changes)
    {
        if (Unwrapped(remove.Left) is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner }
            || this.region.SymbolOf(owner) is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: BaseObjectCreationExpressionSyntax made }
            || this.region.SymbolOf(made) is not IMethodSymbol constructor
            || !this.KeepsItsObject(constructor)
            || FunctionOf(remove) is not { } code)
        {
            return false;
        }

        return code.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(use => use.SpanStart < remove.SpanStart
                          && use.Identifier.ValueText == local.Name
                          && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: local))
            .All(use => use.Parent is MemberAccessExpressionSyntax { Parent: AssignmentExpressionSyntax change }
                        && changes.Contains(change));
    }

    // True when no constructor that a creation runs can give the new object to other code. Each
    // constructor of the chain must be in this compilation, or be the constructor of object. A
    // constructor with no syntax is one that the compiler adds. It calls the base constructor with
    // no arguments.
    private bool KeepsItsObject(IMethodSymbol constructor)
    {
        HashSet<IMethodSymbol> read = new(SymbolEqualityComparer.Default);

        while (read.Add(constructor))
        {
            if (constructor.ContainingType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            SyntaxNode? declared = constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

            if ((declared == null && !constructor.IsImplicitlyDeclared)
                || (declared is ConstructorDeclarationSyntax written && this.CanGiveItsObject(written)))
            {
                return false;
            }

            SyntaxNode? chain =
                declared switch
                {
                    ConstructorDeclarationSyntax own => own.Initializer,
                    TypeDeclarationSyntax type =>
                        type.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault(),
                    _ => null
                };

            if ((chain != null
                    ? this.region.SymbolOf(chain)
                    : constructor.ContainingType.BaseType?.InstanceConstructors.FirstOrDefault(static first =>
                        first.Parameters.IsEmpty)) is not IMethodSymbol next)
            {
                return false;
            }

            constructor = next;
        }

        return false;
    }

    // True when the code of a constructor can give its object to other code. The code names this
    // other than to get to a member, or it names a member of the object that runs code. A lambda
    // or a local function that keeps this also names it or a member of the object.
    private bool CanGiveItsObject(BaseMethodDeclarationSyntax constructor)
    {
        INamedTypeSymbol? type = this.region.ModelFor(constructor).GetDeclaredSymbol(constructor)?.ContainingType;

        return type == null
               || constructor.DescendantNodes()
                   .Any(node => node switch
                   {
                       ThisExpressionSyntax =>
                           node.Parent is not MemberAccessExpressionSyntax access || access.Expression != node,
                       SimpleNameSyntax name =>
                           this.region.SymbolOf(name)
                               is { IsStatic: false } member and (IMethodSymbol or IPropertySymbol or IEventSymbol)
                           && !KeepsOnlyAValue(member)
                           && IsAMemberOf(member: member, type: type),
                       _ => false
                   });
    }

    // True for a member that only keeps a value. Such a member is a property whose accessors have
    // no code, or an event with a field for its handlers.
    private static bool KeepsOnlyAValue(ISymbol member) =>
        member switch
        {
            IEventSymbol => SodaFlowSymbols.IsFieldLikeEvent(member),
            IPropertySymbol property =>
                !property.DeclaringSyntaxReferences.IsEmpty
                && property.DeclaringSyntaxReferences.All(static reference =>
                    reference.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } list }
                    && list.Accessors.All(static accessor => accessor is { Body: null, ExpressionBody: null })),
            _ => false
        };

    // True for a member of a type or of one of its base types.
    private static bool IsAMemberOf(ISymbol member, INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? part = type; part != null; part = part.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    x: part.OriginalDefinition,
                    y: member.ContainingType?.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    // The assignments with += and -= of a delegate location of this run of the code, and, for a
    // local, its declarator. The location is a local, or an event of a local whose value is a new
    // object. Null when a use of the location can copy the delegate, or when the location can be
    // of a different run.
    private (HashSet<SyntaxNode> Changes, VariableDeclaratorSyntax? Start)? UsesOfTheLocation(
        ExpressionSyntax location,
        SyntaxNode code)
    {
        (ISymbol Variable, ISymbol? Member)? named =
            Unwrapped(location) switch
            {
                IdentifierNameSyntax name when this.region.SymbolOf(name) is ILocalSymbol own => (own, null),
                MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } access
                    when this.region.SymbolOf(owner) is ILocalSymbol own
                         && this.region.SymbolOf(access) is IEventSymbol happening
                         && SodaFlowSymbols.IsFieldLikeEvent(happening)
                         && !happening.ContainingType.DeclaringSyntaxReferences.Any(reference =>
                             reference.SyntaxTree == location.SyntaxTree
                             && reference.Span.Contains(location.Span)) => (own, happening),
                _ => null
            };

        if (named is not (var variable, var member)
            || variable.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax declarator
            || FunctionOf(declarator) != code
            || !RunsStraightIn(node: declarator, code: code)
            || (member != null
                && (declarator.Initializer?.Value is not BaseObjectCreationExpressionSyntax
                    || this.GetsASecondValue(local: variable, scope: code))))
        {
            return null;
        }

        HashSet<SyntaxNode> changes = [];

        foreach (IdentifierNameSyntax use in code.DescendantNodes()
                     .OfType<IdentifierNameSyntax>()
                     .Where(use =>
                         use.Identifier.ValueText == variable.Name
                         && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: variable)))
        {
            if (FunctionOf(use) != code)
            {
                return null;
            }

            // For an event, a use of the local can name a different member. That use reads the
            // member, or calls a method of the object. Such code runs only where the remove
            // permits it.
            SyntaxNode? at =
                member == null
                    ? use
                    : use.Parent is MemberAccessExpressionSyntax access
                      && access.Expression == use
                      && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(access), y: member)
                        ? access
                        : null;

            if (at == null)
            {
                continue;
            }

            switch (at.Parent)
            {
                case InvocationExpressionSyntax call when call.Expression == at:
                case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } invoke
                    when invoke.Expression == at:
                case ConditionalAccessExpressionSyntax conditional when conditional.Expression == at:
                    continue;
                case AssignmentExpressionSyntax change
                    when change.Left == at
                         && change.Kind() is SyntaxKind.AddAssignmentExpression
                             or SyntaxKind.SubtractAssignmentExpression:
                    changes.Add(change);

                    continue;
                default:
                    return null;
            }
        }

        return (changes, member == null ? declarator : null);
    }

    // True when each run of the code runs the node. The node has only blocks and its own statement
    // around it, up to the code.
    private static bool RunsStraightIn(SyntaxNode node, SyntaxNode code) =>
        node.Ancestors()
            .TakeWhile(around => around != code)
            .All(static around =>
                around is BlockSyntax
                    or ExpressionStatementSyntax
                    or LocalDeclarationStatementSyntax
                    or VariableDeclarationSyntax
                    or VariableDeclaratorSyntax
                    or EqualsValueClauseSyntax);

    // The state of an expression at this time. The walk reads a node one time, and this takes
    // that read away for the expression and for each of its parts.
    private LoopState EvalAgain(ExpressionSyntax expression)
    {
        foreach (ExpressionSyntax part in expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            this.memo.Remove(part);
        }

        return this.Eval(expression);
    }

    // True when code between the start of a name with ref and a use of it can give a location a
    // value. Then, the name can be a different object at the use. A write to a member through the
    // name itself gives no location a value. A use in a loop can come after each statement of
    // that loop.
    private bool ALocationCanGetAValueBefore(ISymbol name, SyntaxNode use)
    {
        if (this.function != null
            || FunctionOf(use) is not { } code
            || name.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } declared
            || use.Ancestors().TakeWhile(around => around != code).Any(static around => LoopBodyOf(around) != null))
        {
            return true;
        }

        int start = name is IParameterSymbol ? code.SpanStart : declared.Span.End;

        return code.DescendantNodes()
            .Any(node =>
                node.SpanStart >= start
                && node.Span.End <= use.SpanStart
                && node switch
                {
                    AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax target } =>
                        !SymbolEqualityComparer.Default.Equals(x: this.NameUnder(target.Expression), y: name),
                    AssignmentExpressionSyntax
                        or InvocationExpressionSyntax
                        or BaseObjectCreationExpressionSyntax => true,
                    _ => node.Kind() is SyntaxKind.PreIncrementExpression
                        or SyntaxKind.PreDecrementExpression
                        or SyntaxKind.PostIncrementExpression
                        or SyntaxKind.PostDecrementExpression
                });
    }

    // True for the body of a loop statement. A lambda that a callee runs for each element has a
    // turn also, and this rule is only for a loop statement.
    private static bool IsTheBodyOfALoop(SyntaxNode turn) => turn.Parent is { } loop && LoopBodyOf(loop) == turn;

    // Records the state that a member had before a loop, at the first write of a turn that
    // replaces it. The frame is that of the code that has the loop.
    private static void KeepsForAfterTheLoop(
        LoopFrame frame,
        SyntaxNode turn,
        MadeObject? made,
        ISymbol? name,
        ISymbol member,
        LoopState before)
    {
        ISymbol key = member.OriginalDefinition;

        if (!frame.BeforeATurn.Any(kept =>
                kept.Turn == turn
                && ReferenceEquals(objA: kept.Made, objB: made)
                && SymbolEqualityComparer.Default.Equals(x: kept.Name, y: name)
                && SymbolEqualityComparer.Default.Equals(x: kept.Member, y: key)))
        {
            frame.BeforeATurn.Add((turn, made, name, key, before));
        }
    }

    /// <summary>
    ///     Records the end of the walk of a loop statement. Each member that a
    ///     write of its turn replaced can have the state from before the loop
    ///     also, because the loop can run no turn.
    /// </summary>
    /// <param name="turn">The body of the loop.</param>
    public void GivesBackWhatATurnReplaced(SyntaxNode turn)
    {
        foreach ((SyntaxNode _, MadeObject? made, ISymbol? name, ISymbol member, LoopState before)
                 in this.Frame.BeforeATurn.Where(kept => kept.Turn == turn).ToArray())
        {
            if (made != null && this.region.Objects.StateOf(made) is { } inTheStore)
            {
                this.region.Objects.Set(found: made, state: WithAlso(state: inTheStore, member: member, also: before));
            }
            else if (name != null && this.locals.TryGetValue(key: name, value: out LoopState? inTheName))
            {
                this.locals[name] = WithAlso(state: inTheName, member: member, also: before);
            }
        }

        this.Frame.BeforeATurn.RemoveAll(kept => kept.Turn == turn);

        // A position has the value from before the loop also. Positions of a different count
        // tell nothing.
        foreach ((SyntaxNode _, MadeObject made, ImmutableArray<LoopState> before)
                 in this.Frame.PositionsBeforeATurn.Where(kept => kept.Turn == turn).ToArray())
        {
            if (this.region.Objects.PositionsOf(made) is { } after && after.Length == before.Length)
            {
                this.region.Objects.HasPositions(
                    collection: made,
                    values: after.Select((value, position) => value.Join(before[position])).ToImmutableArray());
            }
            else
            {
                this.region.Objects.HasNoPositions(made);
            }
        }

        this.Frame.PositionsBeforeATurn.RemoveAll(kept => kept.Turn == turn);
    }

    private static LoopState WithAlso(LoopState state, ISymbol member, LoopState also)
    {
        Construction members = state.Members ?? Construction.Empty;

        return state.WithMembers(members.With(member: member, state: members.StateOf(member).Join(also)));
    }

    /// <summary>
    ///     Gives an argument each member that a callee set through its parameter.
    ///     For an object of the store, the callee changed the store, and this does
    ///     nothing. A value with no object keeps its members in the name of the
    ///     caller. Thus, the write of the callee is a write through that name.
    ///     The state of the callee started with the state of the argument, thus
    ///     it replaces when the call always runs.
    /// </summary>
    /// <param name="argument">The expression of the argument.</param>
    /// <param name="parameter">The parameter that got it.</param>
    /// <param name="followed">What the walk found in the callee.</param>
    public void GivesAnArgument(ExpressionSyntax argument, IParameterSymbol parameter, Followed followed)
    {
        if (this.quiet
            || followed.Through.IsEmpty
            || !this.Eval(argument).Objects.IsEmpty
            || argument.FirstAncestorOrSelf<SyntaxNode>(static node =>
                    node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
                is not { } call)
        {
            return;
        }

        (bool always, SyntaxNode? turn) = this.WhereItRuns(call);

        foreach ((IParameterSymbol got, ISymbol member, LoopState state) in followed.Through)
        {
            if (SymbolEqualityComparer.Default.Equals(x: got, y: parameter))
            {
                this.SetMemberOf(
                    owner: argument,
                    member: member,
                    state: state,
                    replaces: always,
                    turn: turn,
                    fromTheCallee: true);
            }
        }
    }

    /// <summary>
    ///     Gives an object each member that a callee set on it. The callee got a
    ///     copy of the members of the object, and the object is in the code of
    ///     the caller.
    /// </summary>
    /// <remarks>
    ///     The copy had the state of each member at the call. The callee gave
    ///     it each of its writes, each one as a write that replaces or as a write
    ///     that adds. Thus, the state that comes back is the state of the member
    ///     after the call, and it replaces when the call always runs.
    /// </remarks>
    /// <param name="call">The call.</param>
    /// <param name="owner">The state of the object.</param>
    /// <param name="written">The expression of the object, when the call has one.</param>
    /// <param name="sets">The members, from <see cref="Followed.Sets" />.</param>
    /// <param name="later">
    ///     The members that the callee wrote through a second owner after its
    ///     last write through this. That owner can be the same object, thus the
    ///     copy does not replace what the member has.
    /// </param>
    private void GivesItsOwner(
        SyntaxNode call,
        LoopState owner,
        ExpressionSyntax? written,
        ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)> sets,
        IReadOnlyDictionary<ISymbol, int> later)
    {
        // C# calls a method of a struct that the code cannot write on a copy of it. The writes of
        // the method go to the copy, and the struct keeps its members.
        if (this.quiet || sets.IsEmpty || (written != null && this.IsAStructThatTheCodeCannotWrite(written)))
        {
            return;
        }

        (bool always, SyntaxNode? turn) = this.WhereItRuns(call);

        foreach ((ISymbol member, LoopState state, MadeObject? one) in sets)
        {
            int? thisAt = later.TryGetValue(key: member.OriginalDefinition, value: out int at) ? at : null;

            // The write is a write to the member through the expression, as an assignment is.
            if (written != null)
            {
                this.SetMemberOf(
                    owner: written,
                    member: member,
                    state: state,
                    replaces: always && thisAt == null,
                    turn: turn,
                    givenBackBy: call,
                    laterThan: thisAt);

                // The callee knows the collection that it gave the member.
                if (always && thisAt == null && this.function == null && this.TheOneCollectionOf(written) is { } self)
                {
                    this.region.Objects.IsTheCollectionIn(owner: self, member: member, collection: one);
                }

                continue;
            }

            foreach (MadeObject made in this.region.Objects.Now(owner.Objects))
            {
                this.region.Objects.IsTheCollectionIn(owner: made, member: member, collection: null);
            }

            LoopState inOwner = this.InAnObject(owner: owner, state: state);
            this.region.AddAMemberWrite();
            this.Frame.LastWrite = null;

            this.Change(
                owner: owner,
                change: old =>
                    old.WithMembers((old.Members ?? Construction.Empty).With(member: member, state: inOwner)),
                replaces: always && thisAt == null,
                turn: turn,
                member: member);

            // An owner that the store does not know keeps the state for the field.
            if (owner.Objects.IsEmpty || owner.Objects.Contains(MadeObject.Unknown))
            {
                this.SetField(field: member, state: inOwner);
            }
        }
    }

    // True for a struct that the code cannot write. Such a struct is in a readonly field, out of the
    // code that can write the field. It can also be in a parameter with in or ref readonly, or in a
    // ref readonly local.
    private bool IsAStructThatTheCodeCannotWrite(ExpressionSyntax written) =>
        this.region.SymbolOf(Unwrapped(written)) switch
        {
            IFieldSymbol { IsReadOnly: true, Type.IsValueType: true } field =>
                !this.CanWriteTheField(field: field, at: written),
            IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter, Type.IsValueType: true } => true,
            ILocalSymbol { RefKind: RefKind.RefReadOnly, Type.IsValueType: true } => true,
            // A property that returns no ref gives a copy of its struct.
            IPropertySymbol { ReturnsByRef: false, Type.IsValueType: true } => true,
            _ => false
        };

    // A constructor of the type of a readonly field can write the field. An init accessor of that
    // type can write a field that is not static. A function in that code cannot.
    private bool CanWriteTheField(ISymbol field, SyntaxNode at) =>
        at.Ancestors()
                .FirstOrDefault(static code =>
                    code is BaseMethodDeclarationSyntax
                        or AccessorDeclarationSyntax
                        or AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax)
            switch
            {
                ConstructorDeclarationSyntax constructor =>
                    constructor.Modifiers.Any(SyntaxKind.StaticKeyword) == field.IsStatic
                    && SymbolEqualityComparer.Default.Equals(
                        x: this.region.ModelFor(constructor).GetDeclaredSymbol(constructor)?.ContainingType,
                        y: field.ContainingType),
                AccessorDeclarationSyntax { RawKind: (int)SyntaxKind.InitAccessorDeclaration } init =>
                    !field.IsStatic
                    && SymbolEqualityComparer.Default.Equals(
                        x: this.region.ModelFor(init).GetDeclaredSymbol(init)?.ContainingType,
                        y: field.ContainingType),
                _ => false
            };

    /// <summary>
    ///     Records a value that the code gives to a member of an object, as in
    ///     <c>row.Share = value</c>. An object in a local keeps the state with its
    ///     members. For each other owner, and for a static member, the region keeps
    ///     the state for the field.
    /// </summary>
    /// <param name="owner">The expression of the object. Null for a static member.</param>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    /// <param name="turn">
    ///     The body of the loop whose each turn always runs the assignment. Null
    ///     when no loop does.
    /// </param>
    /// <param name="from">The expression of the value, when the code has one.</param>
    /// <param name="fromTheCallee">
    ///     True for a write that a callee made through its parameter, which
    ///     the call gives back to the argument.
    /// </param>
    /// <param name="givenBackBy">
    ///     The call whose callee wrote the member through this, when the write
    ///     comes back to the receiver. Null for each other write.
    /// </param>
    /// <param name="laterThan">See NotesAWriteThroughANameOfOneObject.</param>
    public void SetMemberOf(
        ExpressionSyntax? owner,
        ISymbol member,
        LoopState state,
        bool replaces,
        SyntaxNode? turn,
        ExpressionSyntax? from = null,
        bool fromTheCallee = false,
        SyntaxNode? givenBackBy = null,
        int? laterThan = null)
    {
        bool isThroughThis = owner != null && this.IsANameOfThis(owner);
        state = this.ForANameOfTheBuiltObject(state: state, from: from);

        // A write through a name that got this is a write to the object that the code is in.
        if (isThroughThis)
        {
            this.SetMember(member: member, state: state, replaces: replaces, from: from);

            if (!this.quiet)
            {
                this.Frame.WrittenThroughANameOfThis[member.OriginalDefinition] = state.From(member);
            }
        }

        if (owner is MemberAccessExpressionSyntax or IdentifierNameSyntax
            && this.region.SymbolOf(owner) is IFieldSymbol { Type.IsValueType: true } field)
        {
            this.WritesInPlace(
                owner: owner,
                field: field,
                member: member,
                state: state,
                replaces: replaces,
                turn: turn);
        }

        // A write through a name with ref is a write through its location, read at this time.
        ExpressionSyntax? written = owner;

        // The location can be a second name. Its first name holds from the code that gave the
        // location its value up to this write.
        if (owner != null
            && (this.TheLocationOf(owner) is { } named
                ? this.TheFirstNameOf(written: named, at: owner) ?? named
                : this.TheFirstNameOf(owner)) is { } location)
        {
            this.EvalAgain(location);
            owner = location;
        }

        bool isThroughALocation = !ReferenceEquals(objA: written, objB: owner);
        LoopState has = this.Eval(owner);
        state = this.InAnObject(owner: has, state: state);
        this.region.AddAMemberWrite();

        if (!isThroughThis)
        {
            (this.writesThroughOthers ??= new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default))[
                member.OriginalDefinition] = this.region.MemberWrites;
        }

        this.NotesAnOwnWrite(written);
        this.NotesAnOwnWrite(owner);

        // A field is a location that a parameter with ref can be a name of.
        if (member is IFieldSymbol)
        {
            this.PutsIntoALocation(field: member, element: null, state: state, owner: owner);
        }

        // A name with ref is its location, and code can give that location a different object with
        // no assignment to the name. Thus, a write through such a name is sure for no object. A
        // parameter whose location the walk reads in the code of the caller has the object that
        // the location has at this write.
        bool isReadInTheCaller = owner != null && this.IsReadInTheCaller(owner);
        bool canBeADifferentObject = false;
        bool always = replaces;

        if (owner != null
            && !isReadInTheCaller
            && this.NameUnder(owner) is { } aliased
            && !KeepsItsObject(aliased)
            && this.ALocationCanGetAValueBefore(name: aliased, use: owner))
        {
            replaces = false;
            turn = null;
            canBeADifferentObject = true;
        }

        // The owner can be an object that the store does not know. The write is then a change to
        // that object, and each object of the store that the owner can be keeps what it has.
        bool isKnown = !this.CanBeUnseen(owner);
        this.NoteAWriteThroughEach(owner: owner, member: member, state: state, turn: turn);

        this.NotesTheCollectionIn(
            owner: owner,
            has: has,
            member: member,
            state: state,
            from: from,
            sure: replaces && isKnown);

        // A local with ref that keeps its location names that location at each use. The record of the names of one
        // object has the name of the location at this write.
        this.NotesAWriteThroughANameOfOneObject(
            written: written,
            member: member,
            state: state,
            replaces: replaces || (always && written != null && this.TheDeclaredLocationOf(written) != null),
            seen: fromTheCallee,
            givenBackBy: givenBackBy,
            laterThan: laterThan);

        this.NotesAWriteAtAnElement(
            written: written,
            member: member,
            state: state,
            replaces: replaces,
            has: has,
            isKnown: isKnown,
            turn: turn);

        if (!replaces
            || isThroughALocation
            || this.quiet
            || this.function != null
            || owner is not IdentifierNameSyntax
            || this.region.SymbolOf(owner) is not { } name
            || !KeepsItsObject(name)
            || !this.IsALocation(member)
            || FunctionOf(owner) is not { } code)
        {
            this.Write(
                owner: owner,
                member: member,
                state: state,
                replaces: replaces,
                turn: turn,
                has: has,
                isKnown: isKnown,
                isReadInTheCaller: isReadInTheCaller);

            this.Frame.LastWrite = null;

            this.NotesAWriteForTheLaterReads(
                written: written,
                member: member,
                has: has,
                canBeADifferentObject: canBeADifferentObject);

            this.GivesTheOtherNames(
                owner: owner,
                at: written,
                member: member,
                state: state,
                replaces: replaces,
                turn: turn);

            return;
        }

        this.TakeBackTheWriteBefore(name: name, member: member, code: code, position: owner.SpanStart);

        List<(MadeObject Object, LoopState Before)> before = [];

        foreach (MadeObject made in this.region.Objects.Now(has.Objects))
        {
            if (this.region.Objects.StateOf(made) is { } old)
            {
                before.Add((made, old));
            }
        }

        this.Write(
            owner: owner,
            member: member,
            state: state,
            replaces: replaces,
            turn: turn,
            has: has,
            isKnown: isKnown,
            isReadInTheCaller: false);

        this.NotesAWriteForTheLaterReads(
            written: written,
            member: member,
            has: has,
            canBeADifferentObject: canBeADifferentObject);

        this.GivesTheOtherNames(owner: owner, at: owner, member: member, state: state, replaces: replaces, turn: turn);

        this.Frame.LastWrite =
            new WriteThroughAName(
                name: name,
                member: member.OriginalDefinition,
                code: code,
                position: owner.SpanStart,
                state: state,
                local: this.locals[name],
                writes: this.region.MemberWrites,
                objects:
                [
                    .. before.Select(one =>
                        (one.Object, one.Before, After: this.region.Objects.StateOf(one.Object) ?? one.Before))
                ]);
    }

    // A field of a value type is a location in the object that holds it. Thus, a write to a member
    // of that field changes the value in the location, and not a copy of it. The holder gets the
    // value of the field with the new state of the member. A static field has no holder. Each
    // other field with no owner in the code is a field of the object that the code is in.
    private void WritesInPlace(
        ExpressionSyntax owner,
        ISymbol field,
        ISymbol member,
        LoopState state,
        bool replaces,
        SyntaxNode? turn)
    {
        if (this.Eval(owner) is not { Members: { } parts } value)
        {
            return;
        }

        LoopState changed = value.WithMembers(parts.With(member: member, state: state));
        ExpressionSyntax? holder = (owner as MemberAccessExpressionSyntax)?.Expression;

        if (!field.IsStatic && holder is null or ThisExpressionSyntax)
        {
            this.SetMember(member: field, state: changed, replaces: replaces);
        }
        else
        {
            this.SetMemberOf(
                owner: field.IsStatic ? null : holder,
                member: field,
                state: changed,
                replaces: replaces,
                turn: turn);
        }
    }

    /// <summary>
    ///     Records the start of the walk of a foreach statement, after the walk
    ///     of its collection. A write through an element takes the positions of
    ///     the collection away, thus the elements are from this time.
    /// </summary>
    public void StartsEach(ForEachStatementSyntax loop)
    {
        if (this.quiet || this.function != null)
        {
            return;
        }

        IReadOnlyList<ExpressionSyntax>? elements = this.ElementsOf(loop.Expression);
        IReadOnlyList<LoopState>? values = elements == null ? this.StorePositionsOf(loop.Expression)?.All : null;
        IReadOnlyList<MadeObject>? whole = elements == null && values == null ? this.WhollyIn(loop) : null;

        if (elements != null || values != null || whole != null)
        {
            this.Frame.WritesThroughEach[loop] =
                new WritesThroughEach(
                    elements: elements ?? [],
                    values: values,
                    whole: whole,
                    start: this.region.MemberWrites,
                    member: null,
                    state: LoopState.None,
                    same: true,
                    visits: 0,
                    last: 0);
        }
    }

    /// <summary>
    ///     Records a call of <c>Add</c> of a list of the platform, which each
    ///     turn of a loop always runs. When the call adds the object of the
    ///     turn, each object that the same code makes in a turn goes into the
    ///     list. See <see cref="WhollyIn" />.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="collection">The owner of the call.</param>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments of the call.</param>
    /// <param name="turn">The body of the loop.</param>
    public void AddsInEachTurn(
        SyntaxNode call,
        ExpressionSyntax collection,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SyntaxNode turn)
    {
        if (this.quiet
            || arguments.Count != 1
            || method is not { Name: "Add", ContainingType: { Name: "List" or "IList" or "ICollection" } owner }
            || !SodaFlowSymbols.IsPlatform(owner))
        {
            return;
        }

        bool noted =
            this.Frame.AddsOfEachTurn.TryGetValue(key: call, value: out (MadeObject List, MadeObject? Added) before);

        // Each read of the loop must add the object of its turn to the same list, which no loop made.
        if (this.TheOneCollectionOf(collection) is not { Turn: null, IsOfTurnsBefore: false } list
            || (noted && (before.Added == null || !ReferenceEquals(objA: before.List, objB: list))))
        {
            if (noted)
            {
                this.Frame.AddsOfEachTurn[call] = before with { Added = null };
            }

            return;
        }

        this.Frame.AddsOfEachTurn[call] =
            (list,
                this.TheOneObjectOf(arguments[0].Expression) is { IsOfTurnsBefore: false } added && added.Turn == turn
                    ? added
                    : null);
    }

    // Each identity of the store whose objects are all elements of the collection of a foreach. An
    // Add that each turn of a loop always runs put each of those objects into the collection. Null
    // when the walk knows no such identity, or when code can take an element out of the collection.
    // When the walk knows such an identity, the result also has the objects of PutOneTimeBefore.
    private List<MadeObject>? WhollyIn(CommonForEachStatementSyntax loop)
    {
        if (this.Frame.AddsOfEachTurn.Count == 0
            || this.TheOneCollectionOf(loop.Expression) is not { } list
            || !this.OnlyGetsAdds(loop.Expression))
        {
            return null;
        }

        List<MadeObject> whole = [];

        foreach ((MadeObject into, MadeObject? added) in this.Frame.AddsOfEachTurn.Values)
        {
            if (added != null && ReferenceEquals(objA: into, objB: list))
            {
                whole.AddRange(this.region.Objects.Now(ImmutableArray.Create(added)));
            }
        }

        if (whole.Count == 0)
        {
            return null;
        }

        whole.AddRange(this.PutOneTimeBefore(loop));

        return whole;
    }

    // Each object that the literal of the local of a foreach, or a put before the foreach, put into
    // the list. Such a put is an Add, an Insert, or an AddRange of a literal: see APutOf. It is a
    // statement at the first level of the block of the local. EndsEach uses the result only for a
    // foreach that always runs in its function, in no loop. Thus, no goto jumps over such a put,
    // and the put ran before the foreach. No use of the local removes an object. The result has
    // only objects that this code made one time and that the store has.
    private IEnumerable<MadeObject> PutOneTimeBefore(CommonForEachStatementSyntax loop)
    {
        if (this.region.SymbolOf(Unwrapped(loop.Expression)) is not ILocalSymbol local
            || this.LiteralOf(local) is not (var written, var uses)
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().FirstAncestorOrSelf<BlockSyntax>()
                is not { } scope)
        {
            return [];
        }

        return written
            .Concat(
                uses.Where(use => use.SpanStart < loop.SpanStart)
                    .SelectMany(use => this.APutOf(use) is (var put, var statement) && statement.Parent == scope
                        ? put
                        : []))
            .Select(this.TheOneObjectOf)
            .OfType<MadeObject>()
            .Where(made => made is { Turn: null, IsOfTurnsBefore: false }
                           && made.IsFrom(this.Frame)
                           && this.region.Objects.StateOf(made) != null);
    }

    // A write to a member through the variable of a foreach statement, which each turn of the
    // statement always runs. EndsEach uses it.
    private void NoteAWriteThroughEach(SyntaxNode? owner, ISymbol member, LoopState state, SyntaxNode? turn)
    {
        if (!this.quiet
            && this.function == null
            && turn != null
            && owner is IdentifierNameSyntax
            && this.region.SymbolOf(owner) is ILocalSymbol each
            && each.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ForEachStatementSyntax loop
            && loop.Statement == turn
            && this.Frame.WritesThroughEach.TryGetValue(key: loop, value: out WritesThroughEach? noted))
        {
            this.Frame.WritesThroughEach[loop] =
                noted.With(
                    written: member.OriginalDefinition,
                    value: state,
                    isALocation: this.IsALocation(member),
                    writes: this.region.MemberWrites);
        }
    }

    /// <summary>
    ///     Records the end of the walk of a foreach statement. When the statement
    ///     gave each element of its collection a value for a member, each of those
    ///     objects then has only that value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A write in the body of a loop adds, because the walk does not know
    ///         which turns ran. After a loop that has a turn for each element, each
    ///         element has the value. The collection must keep its positions, thus
    ///         the walk knows each of its elements. Each element must be one object
    ///         that the code of this frame made.
    ///     </para>
    ///     <para>
    ///         A collection with no positions can have the objects of the turns of
    ///         a loop, which each turn added. The objects of such an identity are
    ///         all elements, thus each of them has the value also.
    ///     </para>
    ///     <para>
    ///         A second write to a member in the body can give an element that a
    ///         turn before changed a different value. Thus, each write to a member
    ///         in each read of the body is the write through the variable.
    ///     </para>
    /// </remarks>
    /// <param name="loop">The statement.</param>
    /// <param name="hasATurnForEachElement">
    ///     True when the statement always runs in its function, and no statement
    ///     ends a turn or the loop before its end.
    /// </param>
    public void EndsEach(ForEachStatementSyntax loop, bool hasATurnForEachElement)
    {
        if (!this.Frame.WritesThroughEach.TryGetValue(key: loop, value: out WritesThroughEach? noted))
        {
            return;
        }

        this.Frame.WritesThroughEach.Remove(loop);

        if (!hasATurnForEachElement
            || noted.Member is not { } member
            || !noted.IsTheOnlyWrite(this.region.MemberWrites))
        {
            return;
        }

        List<(MadeObject Object, LoopState State)> objects = [];

        foreach (MadeObject? made in noted.Whole
                                     ?? noted.Values?.Select(this.TheOneObjectIn)
                                     ?? noted.Elements.Select(this.TheOneObjectOf))
        {
            if (made == null
                || (noted.Whole == null && made is not { Turn: null, IsOfTurnsBefore: false })
                || !made.IsFrom(this.Frame)
                || this.region.Objects.StateOf(made) is not { } old)
            {
                return;
            }

            objects.Add((made, old));
        }

        foreach ((MadeObject made, LoopState old) in objects)
        {
            this.region.Objects.Set(
                found: made,
                state: old.WithMembers((old.Members ?? Construction.Empty).With(member: member, state: noted.State)));
        }
    }

    // The object of the store that the value of an expression is now, when it is one object and no
    // other value.
    private MadeObject? TheOneObjectOf(ExpressionSyntax expression) => this.TheOneObjectIn(this.Eval(expression));

    // The same for a value that the walk has the state of.
    private MadeObject? TheOneObjectIn(LoopState value)
    {
        ImmutableArray<MadeObject> now = this.region.Objects.Now(value.Objects);

        return now.Length == 1 ? now[0] : null;
    }

    // True for a local or a parameter that is one object until code gives it a value. A name with
    // ref is a second name of a location, and a write to that location gives the name a value.
    private static bool KeepsItsObject(ISymbol name) =>
        name is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None };

    // True for a local or a parameter of a value type with no ref. It has a copy of the value that
    // it gets. Thus, it is no second name of an element, as CanBeAnObject says for a location.
    private static bool HasACopy(ISymbol name) =>
        name is ILocalSymbol { RefKind: RefKind.None, Type.IsValueType: true }
            or IParameterSymbol { RefKind: RefKind.None, Type.IsValueType: true };

    // True for a name with ref of a value type that gets a value with no ref. The assignment
    // copies the value into the location of the name. Thus, the name is no second name of the
    // element that it copies, and it keeps its location.
    private static bool CopiesIntoItsLocation(ISymbol name, ExpressionSyntax value) =>
        value is not RefExpressionSyntax
        && name is ILocalSymbol { RefKind: not RefKind.None, Type.IsValueType: true }
            or IParameterSymbol { RefKind: not RefKind.None, Type.IsValueType: true };

    // True for a member that is a location and no code: a field, or a property whose accessors
    // have no body. A read of it gives the value of the last write to it. A read or a write of a
    // virtual property runs the override of the class of the object, which can have code.
    private bool IsALocation(ISymbol member) => HasNoCode(member) && !this.CanRunTheCodeOfAnOverride(member);

    // True for a property that an override with code can replace. The test of the modifiers comes
    // first only for speed. Most properties are not virtual, and the index makes its set at the
    // first question.
    private bool CanRunTheCodeOfAnOverride(ISymbol member) =>
        member is IPropertySymbol property
        && (property.IsVirtual || property.IsAbstract || property.IsOverride)
        && this.region.Symbols.Overrides.ReplacesWithCode(property);

    // True for a field, or for a property of which each declaration has no accessor with a body. A
    // partial property has its code in its second part, which DeclarationsOf finds.
    private static bool HasNoCode(ISymbol member) =>
        member is IFieldSymbol
        || (member is IPropertySymbol property
            && DeclarationsOf(property).All(static declared =>
                declared is ParameterSyntax or PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: null }
                || (declared is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } list }
                    && list.Accessors.All(static accessor => accessor.Body == null && accessor.ExpressionBody == null))));

    /// <summary>
    ///     The state of a read of a member through a name, when the last write
    ///     through that name gave the member its value. Null for each other read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The name can be one of two or more objects, and the store has the
    ///         write and the older state together for each of them. The name is
    ///         one object, and that object has only what the write gave.
    ///     </para>
    ///     <para>
    ///         That is true while no code changes the member or the name. Thus, no
    ///         write to a member came after that write, no code gave the name a
    ///         value, and no function uses the name. The value has no members and
    ///         is no object, thus no change after the write is a change to it.
    ///     </para>
    /// </remarks>
    private LoopState? WrittenThroughItsName(MemberAccessExpressionSyntax read) =>
        !this.quiet
        && this.function == null
        && this.Frame.LastWrite is { State: { Members: null, Objects.IsEmpty: true } } last
        && last.Writes == this.region.MemberWrites
        && Unwrapped(read.Expression) is IdentifierNameSyntax owner
        && owner.SpanStart > last.Position
        && FunctionOf(owner) == last.Code
        && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(owner), y: last.Name)
        && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(read)?.OriginalDefinition, y: last.Member)
        && this.locals.TryGetValue(key: last.Name, value: out LoopState? own)
        && ReferenceEquals(objA: own, objB: last.Local)
        && last.Objects.All(one => ReferenceEquals(objA: this.region.Objects.StateOf(one.Object), objB: one.After))
        && !this.GetsAValue(
            name: last.Name,
            code: last.Code,
            after: last.Position,
            before: owner.SpanStart,
            orAFunctionUsesIt: true)
            ? last.State.Join(this.TypeState(read))
            : null;

    // The function that has a node in its code.
    private static SyntaxNode? FunctionOf(SyntaxNode node) =>
        node.Ancestors()
            .FirstOrDefault(static around =>
                around is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or BaseMethodDeclarationSyntax
                    or AccessorDeclarationSyntax);

    /// <summary>
    ///     Takes the last write through a name back, before a second write
    ///     through that name to the same member.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The name is one object for the two writes, because no code gives it
    ///         a value between them. Thus, that object has only the new state, and
    ///         each other object that the name can be did not get the first write.
    ///         Each object of the store gets the state from before the first write
    ///         back, and the second write then adds its state.
    ///     </para>
    ///     <para>
    ///         The two writes are at the first level of one function, in the order
    ///         of the code. Thus, one call of the function runs them. No object
    ///         changed between them: no write to a member came between, and each
    ///         object has the state that the first write gave.
    ///     </para>
    /// </remarks>
    private void TakeBackTheWriteBefore(ISymbol name, ISymbol member, SyntaxNode code, int position)
    {
        if (this.Frame.LastWrite is not { } last
            || last.Code != code
            || last.Position >= position
            || !SymbolEqualityComparer.Default.Equals(x: last.Name, y: name)
            || !SymbolEqualityComparer.Default.Equals(x: last.Member, y: member.OriginalDefinition)
            || last.Writes != this.region.MemberWrites - 1
            || last.Objects.Any(one =>
                !ReferenceEquals(objA: this.region.Objects.StateOf(one.Object), objB: one.After))
            || this.GetsAValue(
                name: name,
                code: code,
                after: last.Position,
                before: position,
                orAFunctionUsesIt: false))
        {
            return;
        }

        foreach ((MadeObject found, LoopState before, _) in last.Objects)
        {
            this.region.Objects.Set(found: found, state: before);
        }
    }

    // True when code of a function can give a name a value between two positions. That code is
    // between the two, or it is in a function in that function, which can run between them. A
    // function that uses the name can change the object of the name also, and the last argument
    // says if that use is such code.
    private bool GetsAValue(ISymbol name, SyntaxNode code, int after, int before, bool orAFunctionUsesIt) =>
        this.GetsAValue(
            name: name,
            code: code,
            after: after,
            before: before,
            orAFunctionUsesIt: orAFunctionUsesIt,
            ran: ImmutableArray<SyntaxNode>.Empty,
            aFunctionCanRun: true);

    // The same, with no use in the given functions. They ran before the first position. A use in
    // a function counts only when a function can run between the two positions.
    private bool GetsAValue(
        ISymbol name,
        SyntaxNode code,
        int after,
        int before,
        bool orAFunctionUsesIt,
        ImmutableArray<SyntaxNode> ran,
        bool aFunctionCanRun) =>
        code.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(use =>
                use.Identifier.ValueText == name.Name
                && (FunctionOf(use) is { } function && function != code
                    ? aFunctionCanRun && (orAFunctionUsesIt || GivesAValue(use)) && !ran.Contains(function)
                    : use.SpanStart > after && use.SpanStart < before && GivesAValue(use))
                && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: name));

    // True for a use of a name that can give the name a value. The use is the target of an
    // assignment or of a deconstruction, or an argument or an expression with ref, out, or in. The
    // operand of an operator that changes it is such a use also.
    private static bool GivesAValue(SyntaxNode use)
    {
        SyntaxNode target = use;

        while (target.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
        {
            target = tuple;
        }

        return target.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == target,
            ForEachVariableStatementSyntax each => each.Variable == target,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefExpressionSyntax => true,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax =>
                target.Parent.Kind() is SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PreDecrementExpression
                    or SyntaxKind.PostIncrementExpression
                    or SyntaxKind.PostDecrementExpression,
            _ => false
        };
    }

    // The local or the parameter that an expression is, or that it reads a member of.
    private ISymbol? NameUnder(ExpressionSyntax owner)
    {
        ExpressionSyntax written = Unwrapped(owner);

        while (written is MemberAccessExpressionSyntax access)
        {
            written = Unwrapped(access.Expression);
        }

        return written is IdentifierNameSyntax
               && this.region.SymbolOf(written) is { } name and (ILocalSymbol or IParameterSymbol)
            ? name
            : null;
    }

    /// <summary>
    ///     True when the object of an expression can be one that the store does
    ///     not know, because a collection gave it. The expression is an element
    ///     of a collection, a local that got such an element, or a member of one
    ///     of the two.
    /// </summary>
    /// <remarks>
    ///     An element at a position that the walk knows is the value of that
    ///     position. Each other element can be each object that its collection
    ///     holds.
    /// </remarks>
    private bool CanBeUnseen(ExpressionSyntax? written)
    {
        while (written != null)
        {
            written = Unwrapped(written);

            switch (written)
            {
                case IdentifierNameSyntax
                    when this.region.SymbolOf(written) is { } name and (ILocalSymbol or IParameterSymbol):
                    return this.Frame.CanBeUnseen.Contains(name);
                case ElementAccessExpressionSyntax element when this.PositionOf(element) is { } position:
                    written = position;

                    continue;
                case ElementAccessExpressionSyntax or InvocationExpressionSyntax
                    when this.CollectionOf(written) is { } collection:
                    return !this.HoldsOnlyWhatWentIn(collection);
                case MemberAccessExpressionSyntax access:
                    written = access.Expression;

                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    // True when a write that always runs in its function gives a name only the new state. The
    // function is the code of this frame, or each call of it makes the name, or its call always
    // runs.
    private bool AlwaysRunsForAName(ISymbol name) =>
        this.function == null
        || IsDeclaredIn(local: name, code: this.function)
        || this.call is { AlwaysRuns: true };

    // True for a name that is new in each turn. The body of the loop declares such a name. The
    // variable of the foreach statement is one also, and so is a parameter of the lambda that has
    // the body.
    private static bool IsMadeInEachTurn(ISymbol name, SyntaxNode turn) =>
        IsDeclaredIn(local: name, code: turn)
        || turn.Parent switch
        {
            AnonymousFunctionExpressionSyntax lambda =>
                name is IParameterSymbol && IsDeclaredIn(local: name, code: lambda),
            ForEachStatementSyntax each =>
                name.DeclaringSyntaxReferences.Any(declared => declared.GetSyntax() == each),
            ForEachVariableStatementSyntax parts => IsDeclaredIn(local: name, code: parts.Variable),
            _ => false
        };

    // The write itself: what SetMemberOf records for each owner. The store does not know each
    // object that the owner can be when isKnown is false. The last argument is true for a
    // parameter that reads its location in the code of the caller.
    private void Write(
        ExpressionSyntax? owner,
        ISymbol member,
        LoopState state,
        bool replaces,
        SyntaxNode? turn,
        LoopState has,
        bool isKnown,
        bool isReadInTheCaller)
    {
        LoopState changed = LoopState.None.WithMembers(Construction.Empty.With(member: member, state: state));

        // The object of the store gets the state for the member, for each name of the object.
        if (owner != null)
        {
            this.Change(
                owner: has,
                change: old => old.WithMembers((old.Members ?? Construction.Empty).With(member: member, state: state)),
                replaces: replaces && isKnown,
                turn: isKnown ? turn : null,
                member: member);
        }

        // The owner is an element of a collection. The collection then contains an object whose
        // member has the state.
        if (owner != null && this.CollectionOf(owner) != null)
        {
            this.Merge(target: owner, state: changed);
        }

        if (owner is IdentifierNameSyntax
            && this.region.SymbolOf(owner) is { } holder
                and (ILocalSymbol or IParameterSymbol))
        {
            // Such a parameter is only objects of the store at this write. A state of its own for
            // the member is a state of the object that it got at first. The callee gives that state
            // back to the location.
            if (isReadInTheCaller)
            {
                return;
            }

            // A collection that got the object of this local contains that object also.
            this.MergeIntoHolders(
                local: holder,
                state: AsACopyOfAChangeTo(changed: has, state: changed),
                level: 1);

            // A parameter starts with the state of its argument, which has the type of the object.
            // The callee gives this state back, and the object of the caller keeps its type.
            LoopState known =
                this.locals.TryGetValue(key: holder, value: out LoopState? existing)
                    ? existing
                    : this.bound.TryGetValue(key: holder, value: out LoopState? given)
                        ? given
                        : LoopState.None;

            // After a write that does not always run, the member can have the state that it had
            // also. The name then has the two states together, as an object of the store has.
            Construction had = known.Members ?? Construction.Empty;

            bool only =
                replaces
                    ? this.AlwaysRunsForAName(holder)
                    : turn != null && IsMadeInEachTurn(name: holder, turn: turn);

            // The same as for an object of the store: see Change.
            SyntaxNode? itsTurn = !only && !this.quiet && turn != null && IsTheBodyOfALoop(turn) ? turn : null;

            if (itsTurn != null)
            {
                KeepsForAfterTheLoop(
                    frame: this.Frame,
                    turn: itsTurn,
                    made: null,
                    name: holder,
                    member: member,
                    before: had.StateOf(member));
            }

            this.locals[holder] =
                known.WithMembers(
                    had.With(
                        member: member,
                        state: only || itsTurn != null ? state : had.StateOf(member).Join(state)));

            // A callee gives its caller what it set through a parameter. See GivesAnArgument.
            if (!this.quiet)
            {
                if (!this.Frame.SetThrough.TryGetValue(key: holder, value: out HashSet<ISymbol>? sets))
                {
                    sets = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    this.Frame.SetThrough[holder] = sets;
                }

                sets.Add(member.OriginalDefinition);
            }

            // The value has two names, or it had them. A read through the other name can be one
            // that does not see the state of this name. Thus, the region keeps the write also. A
            // second call can give an object that a call gave, which is a second name of it too.
            if (this.IsOneOfTwoNames(holder) || CanBeAnOlderObject(has))
            {
                this.SetField(field: member, state: state);
            }

            return;
        }

        // A static field of a type that is not generic has one location. Thus, a write that always
        // runs in the block replaces the value before it.
        this.SetField(
            field: member,
            state: state,
            replaces: replaces
                      && (this.call?.AlwaysRuns ?? true)
                      && member is IFieldSymbol { IsStatic: true, ContainingType: var type }
                      && !IsGeneric(type));

        return;

        static bool IsGeneric(INamedTypeSymbol? type) =>
            type != null && (type.IsGenericType || IsGeneric(type.ContainingType));
    }

    // True for a name of a value with no object that has a second name, or had one.
    private bool IsOneOfTwoNames(ISymbol name) =>
        (this.Frame.WasOneOfTwoNames.Count > 0 || this.Frame.FirstNameOf.Count > 0)
        && (this.Frame.WasOneOfTwoNames.Contains(name)
            || this.Frame.FirstNameOf.ContainsKey(name)
            || this.Frame.FirstNameOf.Values.Any(first =>
                SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(first), y: name)));
}
