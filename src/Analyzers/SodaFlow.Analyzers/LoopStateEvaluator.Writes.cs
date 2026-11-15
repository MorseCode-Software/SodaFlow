using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a write to a member of an object, and for what a write through a name replaces.
internal sealed partial class LoopStateEvaluator
{
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
    ///     can run at a different time, thus the code of a function gets a first
    ///     name only for a local of its own. One run of the function makes such
    ///     a local, and the two names are one value in that run.
    /// </remarks>
    private ExpressionSyntax? TheFirstNameOf(ExpressionSyntax written)
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
                || (this.function != null && !IsDeclaredIn(local: second, code: this.function))
                || !this.Frame.FirstNameOf.TryGetValue(key: second, value: out ExpressionSyntax? first)
                || FunctionOf(written) is not { } code
                || this.region.SymbolOf(first) is not { } firstName
                || new[] { firstName, second }.Any(one =>
                    this.GetsAValue(
                        name: one,
                        code: code,
                        after: first.Span.End,
                        before: written.SpanStart,
                        orAFunctionUsesIt: false)))
            {
                break;
            }

            found = first;
            use = first;
        }

        return found;
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

        // After an assignment that is not sure to run, the local can be a second name.
        if (this.Frame.FirstNameOf.TryGetValue(key: local, value: out ExpressionSyntax? first))
        {
            this.Frame.FirstNameOf.Remove(local);

            if (!replaces && this.region.SymbolOf(first) is { } firstName)
            {
                this.Frame.WasOneOfTwoNames.Add(local);
                this.Frame.WasOneOfTwoNames.Add(firstName);
            }
        }

        if ((this.function != null && !IsDeclaredIn(local: local, code: this.function))
            || !KeepsItsObject(local)
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

        if (replaces)
        {
            this.Frame.FirstNameOf[local] = name;
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
    public void NotesANameOfALocation(ISymbol local, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet)
        {
            return;
        }

        this.Frame.ReadFrom.Remove(local);

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

    // True for a value whose members only a name keeps. The value is no object, or an object that
    // the store does not know.
    private static bool IsOfNoObjectOfTheStore(LoopState value) =>
        value.Objects.All(static one => ReferenceEquals(objA: one, objB: MadeObject.Unknown));

    // The state of a local that gets a value from a location, with the members that each other
    // name of that location has now. The local is a name of the same value.
    private LoopState WithTheOtherNames(ISymbol local, ExpressionSyntax? from, LoopState state)
    {
        if (this.Frame.ReadFrom.Count == 0 || from == null || !IsOfNoObjectOfTheStore(state))
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
        if (this.Frame.ReadFrom.Count == 0 || this.quiet || this.function != null || owner == null || at == null)
        {
            return;
        }

        ISymbol? self =
            Unwrapped(owner) is IdentifierNameSyntax name && this.region.SymbolOf(name) is ILocalSymbol local
                ? local
                : null;

        ExpressionSyntax? location =
            self == null
                ? owner
                : this.Frame.ReadFrom.TryGetValue(key: self, value: out ExpressionSyntax? read)
                    ? read
                    : null;

        if (location == null
            || this.TheOtherNamesOf(location: location, at: at, self: self) is not { Length: > 0 } others
            || (self != null && !this.IsStillTheLocationOf(name: self, location: location, use: at)))
        {
            return;
        }

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

        return this.Frame.ReadFrom
            .Where(pair =>
                !SymbolEqualityComparer.Default.Equals(x: pair.Key, y: self)
                && IsTheSameLocation(this.LocationKeyOf(pair.Value), key)
                && this.IsStillTheLocationOf(name: pair.Key, location: pair.Value, use: at))
            .Select(static pair => pair.Key)
            .ToArray();
    }

    // The parts of a location that the walk can read again. They are the array or the field, the
    // owner of the field, and the index of the element. The array is in a local, in a parameter, or in a
    // field. The owner is null for a static field and for a field of this object. Null for each
    // other expression.
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
            _ => null
        };

    // True when a local is still a name of the location that it got its value from, at a use. No
    // code between the two gave the local, the array, the field, or the owner a value. No code
    // between them gave the location itself a value. A call between them is not such code for
    // the walk.
    private bool IsStillTheLocationOf(ISymbol name, ExpressionSyntax location, SyntaxNode use)
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
               && !code.DescendantNodes()
                   .OfType<AssignmentExpressionSyntax>()
                   .Any(assignment =>
                       assignment.SpanStart > after
                       && assignment.SpanStart < before
                       && assignment.Left is ElementAccessExpressionSyntax or MemberAccessExpressionSyntax
                       && IsTheSameLocation(this.LocationKeyOf(assignment.Left), key));
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
        if (this.quiet || state.Objects.IsEmpty || this.bound.Count == 0)
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
            MemberAccessExpressionSyntax access =>
                field != null
                && SymbolEqualityComparer.Default.Equals(
                    x: this.region.SymbolOf(access)?.OriginalDefinition,
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
                Unwrapped(returned) is ElementAccessExpressionSyntax element
                    ? (Unwrapped(element.Expression), true)
                    : (Unwrapped(returned), false);

            if (name is not IdentifierNameSyntax || this.region.SymbolOf(name) is not IParameterSymbol parameter)
            {
                continue;
            }

            foreach (Binding binding in bindings)
            {
                if (SymbolEqualityComparer.Default.Equals(x: binding.Parameter, y: parameter))
                {
                    locations.Add((binding.Argument, isAnElement));
                }
            }
        }

        return locations;
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

    // True for the expression of a location that TheLocationOf can give. That is a local, an
    // element of an array in a local, or a field of an object in a local or of this object.
    private bool IsALocationThatTheWalkCanReadAgain(ExpressionSyntax location) =>
        Unwrapped(location) switch
        {
            IdentifierNameSyntax name => this.region.SymbolOf(name) is { } local && KeepsItsObject(local),
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element =>
                Unwrapped(element.Expression) is IdentifierNameSyntax array
                && this.region.SymbolOf(array) is { } local
                && KeepsItsObject(local)
                && this.region.TypeOf(array) is IArrayTypeSymbol
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

    // Records a write through an element for WrittenThroughItsElement. Each other write to a
    // member takes the record away.
    // The second argument is the owner as the code has it at the write. A name with ref there
    // has its location as the first argument.
    private void NotesAWriteThroughAnElement(
        ExpressionSyntax? owner,
        SyntaxNode? at,
        ISymbol member,
        LoopState state,
        bool replaces)
    {
        this.Frame.LastElementWrite = null;

        if (replaces
            && !this.quiet
            && this.function == null
            && state is { Members: null, Objects.IsEmpty: true }
            && IsALocation(member)
            && owner != null
            && at != null
            && Unwrapped(owner) is ElementAccessExpressionSyntax element
            && this.CollectionAndIndexOf(element) is (var collection, var index)
            && at.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is { } assignment
            && FunctionOf(at) is { } code)
        {
            this.Frame.LastElementWrite =
                new WriteThroughAnElement(
                    collection: collection,
                    index: index,
                    member: member.OriginalDefinition,
                    code: code,
                    end: assignment.Span.End,
                    state: state,
                    writes: this.region.MemberWrites);
        }
    }

    // The local of the collection and the index of an element access with one index. The index
    // is a constant number or a local.
    private (ISymbol Collection, object Index)? CollectionAndIndexOf(ElementAccessExpressionSyntax element)
    {
        if (element.ArgumentList.Arguments.Count != 1
            || Unwrapped(element.Expression) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not { } collection
            || !KeepsItsObject(collection))
        {
            return null;
        }

        ExpressionSyntax written = element.ArgumentList.Arguments[0].Expression;

        object? index =
            this.region.ModelFor(written).GetConstantValue(written) is { HasValue: true, Value: int number }
                ? number
                : written is IdentifierNameSyntax && this.region.SymbolOf(written) is ILocalSymbol or IParameterSymbol
                    ? this.region.SymbolOf(written)
                    : null;

        return index == null ? null : (collection, index);
    }

    /// <summary>
    ///     The state of a read of a member through an element, when the last
    ///     write through that element gave the member its value. Null for each
    ///     other read.
    /// </summary>
    /// <remarks>
    ///     The element is one object for the write and the read when no code runs
    ///     between the two. Thus, the code has no call, no creation, and no
    ///     assignment between them. No write to a member came after that write.
    /// </remarks>
    private LoopState? WrittenThroughItsElement(MemberAccessExpressionSyntax read) =>
        !this.quiet
        && this.function == null
        && this.Frame.LastElementWrite is { } last
        && last.Writes == this.region.MemberWrites
        && Unwrapped(read.Expression) is var owner
        && Unwrapped(this.TheLocationOf(owner) ?? owner) is ElementAccessExpressionSyntax element
        && owner.SpanStart >= last.End
        && FunctionOf(owner) == last.Code
        && this.CollectionAndIndexOf(element) is (var collection, var index)
        && SymbolEqualityComparer.Default.Equals(x: collection, y: last.Collection)
        && (index is ISymbol local
            ? last.Index is ISymbol before && SymbolEqualityComparer.Default.Equals(x: local, y: before)
            : index.Equals(last.Index))
        && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(read)?.OriginalDefinition, y: last.Member)
        && !last.Code.DescendantNodes()
            .Any(node =>
                node.SpanStart >= last.End
                && node.Span.End <= owner.SpanStart
                && (node is InvocationExpressionSyntax
                        or BaseObjectCreationExpressionSyntax
                        or AssignmentExpressionSyntax
                    || node.Kind() is SyntaxKind.PreIncrementExpression
                        or SyntaxKind.PreDecrementExpression
                        or SyntaxKind.PostIncrementExpression
                        or SyntaxKind.PostDecrementExpression))
            ? last.State.Join(this.TypeState(read))
            : null;

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
                this.SetMemberOf(owner: argument, member: member, state: state, replaces: always, turn: turn);
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
    private void GivesItsOwner(
        SyntaxNode call,
        LoopState owner,
        ExpressionSyntax? written,
        ImmutableArray<(ISymbol Member, LoopState State, MadeObject? One)> sets)
    {
        if (this.quiet || sets.IsEmpty)
        {
            return;
        }

        (bool always, SyntaxNode? turn) = this.WhereItRuns(call);

        foreach ((ISymbol member, LoopState state, MadeObject? one) in sets)
        {
            // The write is a write to the member through the expression, as an assignment is.
            if (written != null)
            {
                this.SetMemberOf(owner: written, member: member, state: state, replaces: always, turn: turn);

                // The callee knows the collection that it gave the member.
                if (always && this.function == null && this.TheOneCollectionOf(written) is { } self)
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
                replaces: always,
                turn: turn,
                member: member);

            // An owner that the store does not know keeps the state for the field.
            if (owner.Objects.IsEmpty || owner.Objects.Contains(MadeObject.Unknown))
            {
                this.SetField(field: member, state: inOwner);
            }
        }
    }

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
    public void SetMemberOf(
        ExpressionSyntax? owner,
        ISymbol member,
        LoopState state,
        bool replaces,
        SyntaxNode? turn,
        ExpressionSyntax? from = null)
    {
        // A write through a name with ref is a write through its location, read at this time.
        ExpressionSyntax? written = owner;

        if (owner != null && (this.TheLocationOf(owner) ?? this.TheFirstNameOf(owner)) is { } location)
        {
            this.EvalAgain(location);
            owner = location;
        }

        bool isThroughALocation = !ReferenceEquals(objA: written, objB: owner);
        LoopState has = this.Eval(owner);
        state = this.InAnObject(owner: has, state: state);
        this.region.AddAMemberWrite();

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

        if (owner != null
            && !isReadInTheCaller
            && this.NameUnder(owner) is { } aliased
            && !KeepsItsObject(aliased)
            && this.ALocationCanGetAValueBefore(name: aliased, use: owner))
        {
            replaces = false;
            turn = null;
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

        this.NotesAWriteThroughAnElement(owner: owner, at: written, member: member, state: state, replaces: replaces);

        if (!replaces
            || isThroughALocation
            || this.quiet
            || this.function != null
            || owner is not IdentifierNameSyntax
            || this.region.SymbolOf(owner) is not { } name
            || !KeepsItsObject(name)
            || !IsALocation(member)
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

        if (elements != null || values != null)
        {
            this.Frame.WritesThroughEach[loop] =
                new WritesThroughEach(
                    elements: elements ?? [],
                    values: values,
                    start: this.region.MemberWrites,
                    member: null,
                    state: LoopState.None,
                    same: true,
                    visits: 0,
                    last: 0);
        }
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
                    isALocation: IsALocation(member),
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

        foreach (MadeObject? made in noted.Values?.Select(this.TheOneObjectIn)
                                     ?? noted.Elements.Select(this.TheOneObjectOf))
        {
            if (made is not { Turn: null, IsOfTurnsBefore: false }
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

    // True for a member that is a location and no code: a field, or a property whose accessors
    // have no body. A read of it gives the value of the last write to it.
    private static bool IsALocation(ISymbol member) =>
        member is IFieldSymbol
        || (member is IPropertySymbol property
            && property.DeclaringSyntaxReferences.All(static declared =>
                declared.GetSyntax() is ParameterSyntax
                    or PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: null }
                || (declared.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } list }
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
        code.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(use =>
                use.Identifier.ValueText == name.Name
                && (FunctionOf(use) != code
                    ? orAFunctionUsesIt || GivesAValue(use)
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
            // that does not see the state of this name. Thus, the region keeps the write also.
            if (this.IsOneOfTwoNames(holder))
            {
                this.SetField(field: member, state: state);
            }

            return;
        }

        this.SetField(field: member, state: state);
    }

    // True for a name of a value with no object that has a second name, or had one.
    private bool IsOneOfTwoNames(ISymbol name) =>
        (this.Frame.WasOneOfTwoNames.Count > 0 || this.Frame.FirstNameOf.Count > 0)
        && (this.Frame.WasOneOfTwoNames.Contains(name)
            || this.Frame.FirstNameOf.ContainsKey(name)
            || this.Frame.FirstNameOf.Values.Any(first =>
                SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(first), y: name)));
}
