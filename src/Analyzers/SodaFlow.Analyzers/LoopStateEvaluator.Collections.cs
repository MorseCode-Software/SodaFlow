using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for collections. It has the mark for a collection that holds only what went
// in. It has the record of the one collection of a name or of a member also.
internal sealed partial class LoopStateEvaluator
{
    // The state of a new collection that got the given state: it holds the objects, and is not
    // one of them.
    private static LoopState HoldingItsObjects(LoopState state)
    {
        LoopState held = state.Holding(state.Objects).WithObjects(ImmutableArray<MadeObject>.Empty);

        return held is { IsContainer: true, HasMembersOfNoObject: true } ? held.HoldingAnUnknownObject : held;
    }

    /// <summary>
    ///     True for an argument that goes into the array of a params parameter.
    ///     An argument that is the array itself is the value of the parameter.
    /// </summary>
    public bool IsOneOfAParamsArray(IParameterSymbol parameter, ExpressionSyntax argument) =>
        parameter.IsParams
        && !SymbolEqualityComparer.Default.Equals(
            x: this.region.ModelFor(argument).GetTypeInfo(argument).ConvertedType,
            y: parameter.Type);

    // A new collection holds only objects that the walk saw go in when the expression gives it no
    // other value. That is an empty collection, an array, or a literal with no spread whose each
    // element the store knows.
    private void MarkIfComplete(MadeObject made, ExpressionSyntax place, bool exact)
    {
        if (!exact)
        {
            return;
        }

        IEnumerable<ExpressionSyntax>? elements =
            Unwrapped(place) switch
            {
                CollectionExpressionSyntax written
                    when written.Elements.All(static element => element is ExpressionElementSyntax) =>
                    written.Elements.OfType<ExpressionElementSyntax>().Select(static element => element.Expression),
                ArrayCreationExpressionSyntax array => array.Initializer?.Expressions ?? [],
                ImplicitArrayCreationExpressionSyntax implied => implied.Initializer.Expressions,
                BaseObjectCreationExpressionSyntax creation when this.GetsOnlyComparersAndNumbers(creation) =>
                    (creation.Initializer?.Expressions ?? [])
                    .Select(static element => element is AssignmentExpressionSyntax indexed ? indexed.Right : element),
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateBuilder" }
                } call when this.GetsOnlyComparersAndNumbers(call) => [],
                _ => null
            };

        if (elements == null || !elements.All(this.IsKnownWhenItGoesIn))
        {
            return;
        }

        this.region.Objects.IsComplete(made);

        // A literal whose object has its elements in the order of the code gives the positions.
        if (this.LiteralElements(made: Unwrapped(place), type: this.region.TypeOf(place)) is { } ordered)
        {
            this.region.Objects.HasPositions(
                collection: made,
                values: ordered.Select(this.Eval).ToImmutableArray(),
                givenBy: ordered.Select(static SyntaxNode? (element) => element).ToImmutableArray());
        }
    }

    // True for a value that puts no object that the store does not know into a collection. The
    // value is an object of the store and no other one, or it is no object.
    private bool IsKnownWhenItGoesIn(ExpressionSyntax value)
    {
        if (this.IsNoValue(value) || this.region.TypeOf(value) is not { } type || !this.CanBeAnObject(type))
        {
            return true;
        }

        ImmutableArray<MadeObject> objects = this.Eval(value).Objects;

        return !objects.IsEmpty && !objects.Contains(MadeObject.Unknown);
    }

    /// <summary>
    ///     Records a call of code that the walk cannot read. That code can put an
    ///     object into each collection that it can get to.
    /// </summary>
    public void RanCodeWithNoBody()
    {
        if (!this.quiet)
        {
            this.region.Objects.EachCanHoldMore();
        }
    }

    /// <summary>
    ///     Records that a method of the platform got its arguments, with a
    ///     collection as its owner. The method can put each value into the
    ///     collection. For a parameter that is a sequence, it can put each element
    ///     there, as <c>AddRange</c> does. The walk saw each of those elements go
    ///     in only when the sequence is a collection that holds only such objects.
    /// </summary>
    public void CanGet(ExpressionSyntax collection, IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax value = arguments[i].Expression;

            bool known =
                SodaFlowSymbols.IsSequence(ParameterFor(method: method, argument: arguments[i], position: i)?.Type)
                    ? this.TheCompleteCollection(value) != null
                    : this.IsKnownWhenItGoesIn(value);

            if (!known)
            {
                this.CanHoldMore(collection);

                return;
            }
        }
    }

    /// <summary>
    ///     Records that the owner of a method of a package got the arguments of
    ///     a call. The owner holds each argument that is an object of the store,
    ///     where the walk has a record for that. The owner then needs no copy of
    ///     the state of that argument.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A copy keeps a cell that the object had at the call, and the store
    ///         has the state of the object at each read. An owner that is an
    ///         object of the store holds its objects there, from
    ///         <see cref="Gets" />. It gets a copy of each argument when one of
    ///         them is no such object.
    ///     </para>
    ///     <para>
    ///         The store has no record of what each other owner holds. A static
    ///         field or a static property has one record of the region, and each
    ///         read of its name gets that record. A parameter gives what it holds
    ///         to its argument. A location that the walk can read again gives it
    ///         to each later read of that location. Thus, such a name holds the
    ///         objects, as in <see cref="KeepsWhatItGets" />, and keeps a copy of
    ///         only a value with no object. Each other owner keeps a copy of each
    ///         argument.
    ///     </para>
    /// </remarks>
    /// <param name="owner">The owner in the call.</param>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the package.</param>
    public void KeepsWhatAPackageGets(
        ExpressionSyntax owner,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        LoopState has = this.Eval(owner);
        ExpressionSyntax name = Unwrapped(owner);

        bool inItsName =
            IsOfNoObjectOfTheStore(has)
            && (this.region.SymbolOf(name) is { IsStatic: true } and (IFieldSymbol or IPropertySymbol)
                    or IParameterSymbol { RefKind: RefKind.None }
                || this.KeptKeyOf(name) != null);

        if (inItsName)
        {
            foreach (LoopState put in arguments
                         .Select(argument => this.Eval(argument.Expression))
                         .Where(IsOnlyKnownObjects))
            {
                this.KeepsInTheNameOf(owner: name, holding: LoopState.None.HoldingWhatCameWith(put));
            }
        }

        bool holdsItsObjects =
            inItsName
            || (IsOnlyKnownObjects(has)
                && arguments.All(argument => IsOnlyKnownObjects(this.Eval(argument.Expression))));

        this.Absorb(
            target: owner,
            state: this.Moved(
                invocation: invocation,
                method: method,
                withReceiver: false,
                withObjects: !holdsItsObjects));
    }

    private static bool IsOnlyKnownObjects(LoopState state) =>
        !state.Objects.IsEmpty && !state.Objects.Contains(MadeObject.Unknown);

    /// <summary>
    ///     Records that code with no body to read got the arguments of a call
    ///     on an owner that is no object of the store. The store has no record of
    ///     what such an owner holds. Thus, the name of the owner keeps it, as the
    ///     owner of a method of a package does.
    /// </summary>
    /// <remarks>
    ///     An argument that is an object of the store keeps its state in the
    ///     store. The owner then holds the object, and a later change to the
    ///     object is a change to what the owner gives back. A value with no object
    ///     has its members only in a copy, and the owner keeps that copy.
    /// </remarks>
    /// <param name="owner">The owner in the call.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public void KeepsWhatItGets(ExpressionSyntax owner, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (arguments.Count == 0)
        {
            return;
        }

        // An owner that is an object of the store holds each object that it gets, from Gets. A
        // value with no object has its state only in a copy, and the owner keeps that copy also. A
        // pending value gets SF1003 there, and the walk does not follow it.
        if (!IsOfNoObjectOfTheStore(this.Eval(owner)))
        {
            foreach (LoopState put in arguments
                         .Select(argument => this.Eval(argument.Expression))
                         .Where(static put => put is { IsNone: false, Pending: false } && IsOfNoObjectOfTheStore(put)))
            {
                this.Absorb(target: owner, state: put);
            }

            return;
        }

        foreach (LoopState put in arguments.Select(argument => this.Eval(argument.Expression)))
        {
            if (IsOfNoObjectOfTheStore(put))
            {
                this.Absorb(target: owner, state: put);
            }
            else
            {
                this.KeepsInTheNameOf(owner: owner, holding: LoopState.None.HoldingWhatCameWith(put));
            }
        }
    }

    /// <summary>
    ///     Gives a state that only holds objects to the name of an owner with no
    ///     object of the store. When the owner is a location that the walk can
    ///     read again, each later read of that location holds the objects also.
    /// </summary>
    /// <remarks>
    ///     <see cref="Merge(ExpressionSyntax, LoopState)" /> stops at such a
    ///     state. The store has each change to an object that a value holds, but
    ///     an owner with no object has no record in the store. An element or a
    ///     member of a location has no name of its own. The state of its
    ///     collection or its owner is of a different value. See
    ///     <see cref="KeptThrough" />.
    /// </remarks>
    /// <param name="owner">The owner, as the code names it.</param>
    /// <param name="holding">The state, which only holds objects.</param>
    private void KeepsInTheNameOf(ExpressionSyntax owner, LoopState holding)
    {
        ExpressionSyntax target = Unwrapped(owner);

        this.MergeInto(target: target, state: holding, level: 0);

        if (!this.quiet && this.function == null && this.KeptKeyOf(target) is { } key)
        {
            this.Frame.KeptAt.Add((key.Location, key.Name, key.Member, State: holding));
        }
    }

    /// <summary>
    ///     True for <c>Array.Fill</c>. It puts its value into each element of its
    ///     array, or into each element of a range.
    /// </summary>
    public static bool IsArrayFill(IMethodSymbol method) =>
        method is { Name: "Fill", ContainingType.SpecialType: SpecialType.System_Array };

    /// <summary>
    ///     Records a call of <c>Array.Fill</c>, which puts its value into
    ///     elements of its array, as an assignment to an element does. A call
    ///     that is sure to run gives each position that it fills the value.
    /// </summary>
    /// <remarks>
    ///     A range with an index or a count that is not a constant can be each
    ///     position. Then, the collection has no positions after the call.
    /// </remarks>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments of the call.</param>
    /// <param name="replaces">True when the call always runs in its function.</param>
    /// <param name="turn">The body of the loop whose each turn always runs the call.</param>
    public void Fills(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        bool replaces,
        SyntaxNode? turn)
    {
        if (ArgumentFor(method: method, arguments: arguments, parameter: "array") is not { } array
            || ArgumentFor(method: method, arguments: arguments, parameter: "value") is not { } value)
        {
            return;
        }

        (MadeObject Made, ImmutableArray<LoopState> All)? before =
            this.PositionsBeforeAnAssignment(collection: array, replaces: replaces, turn: turn);

        LoopState put = this.Eval(value);

        // Gets gives the array a new state, and a record of positions holds only for the state of
        // its time. Thus, the positions from before the call are gone after this.
        this.Gets(collection: array, value: value);
        this.Absorb(target: array, state: put);
        this.PutsIntoALocation(field: null, element: null, state: put, owner: array);

        if (before is not (var made, var all) || Filled() is not (var first, var count))
        {
            return;
        }

        ImmutableArray<SyntaxNode?> givenBy = this.region.Objects.GivenBy(collection: made, values: all);

        this.region.Objects.HasPositions(
            collection: made,
            values: all.Select((old, at) => at >= first && at - first < count ? put : old).ToImmutableArray(),
            givenBy: givenBy.Select((old, at) => at >= first && at - first < count ? value : old).ToImmutableArray());

        return;

        // The first position that the call fills, and the count of positions.
        (int First, int Count)? Filled()
        {
            if (method.Parameters.Length == 2)
            {
                return (0, all.Length);
            }

            return Constant("startIndex") is { } start
                   && Constant("count") is { } length
                   && length <= all.Length - start
                ? (start, length)
                : null;
        }

        int? Constant(string parameter) =>
            ArgumentFor(method: method, arguments: arguments, parameter: parameter) is { } given
            && this.region.ModelFor(given).GetConstantValue(given) is { Value: int number and >= 0 }
                ? number
                : null;
    }

    /// <summary>
    ///     Records that a method of the platform got its arguments. The method
    ///     can put a value into a collection that it gets, as <c>CopyTo</c> does to
    ///     an array. The parameter must have a type that the method can change.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments of the call.</param>
    /// <param name="receiver">The owner of the call. For an extension method, it is the first argument.</param>
    public void CanChangeItsArguments(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ExpressionSyntax? receiver)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (receiver != null
            && method.ReducedFrom != null
            && definition.Parameters.Length > 0
            && CanChangeAValueOf(definition.Parameters[0]))
        {
            this.CanHoldMore(receiver);
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (ParameterFor(method: method, argument: arguments[i], position: i) is { } parameter
                && CanChangeAValueOf(parameter))
            {
                this.CanHoldMore(arguments[i].Expression);
            }
        }
    }

    // A callee cannot put a value into an argument that it knows only as a sequence to read, as
    // object, or as a type parameter.
    private static bool CanChangeAValueOf(IParameterSymbol parameter) =>
        parameter.Type is IArrayTypeSymbol
            or INamedTypeSymbol
            {
                SpecialType: not SpecialType.System_Object,
                OriginalDefinition.SpecialType: not (SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T
                or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
                or SpecialType.System_Collections_Generic_IReadOnlyList_T)
            };

    // Records that a collection can hold an object that the walk did not see go in.
    private void CanHoldMore(ExpressionSyntax collection)
    {
        if (this.quiet)
        {
            return;
        }

        foreach (MadeObject made in this.Eval(collection).Objects)
        {
            this.region.Objects.CanHoldMore(made);
        }
    }

    // True for an expression that is one collection, and that collection holds only objects that
    // the walk saw go in. The expression is a local of this code that has one value, or one element
    // of such a collection. A function can run at a different time, thus a local that it reads from
    // the code around it is no such name.
    private bool HoldsOnlyWhatWentIn(ExpressionSyntax collection) => this.TheCompleteCollection(collection) != null;

    // The object of a local of this code that is one collection and no other value. A function
    // can run at a different time, thus a local that it reads from the code around it is no such
    // name. A function of this run that a call runs now, one time, reads the locals as they are
    // now. A static field with readonly is one object for each read, at each time, when the store
    // has an object for it. See WithItsInitializer.
    private MadeObject? TheOneCollectionOf(ExpressionSyntax expression)
    {
        ExpressionSyntax written = Unwrapped(expression);

        if (written is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return null;
        }

        return this.region.SymbolOf(written) switch
        {
            { } local and (ILocalSymbol or IParameterSymbol) =>
                (this.function == null || this.call is { RunsAFunctionOfThisRun: true, RunsOneTime: true })
                && written is IdentifierNameSyntax
                && this.Frame.TheOneCollectionOf.TryGetValue(key: local, value: out MadeObject? one)
                    ? one
                    : null,
            IFieldSymbol { IsStatic: true, IsReadOnly: true } field =>
                this.region.Objects.InAStaticMember(field.OriginalDefinition),
            _ => null
        };
    }

    private MadeObject? TheCompleteCollection(ExpressionSyntax collection)
    {
        MadeObject? made =
            Unwrapped(collection) switch
            {
                IdentifierNameSyntax name => this.TheOneCollectionOf(name) ?? this.TheCollectionInAMemberOfThis(name),
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } own =>
                    this.TheCollectionInAMemberOfThis(own),
                // A member of a local that is one object, from the record of the store.
                MemberAccessExpressionSyntax access
                    when this.region.SymbolOf(access) is { } member and (IFieldSymbol or IPropertySymbol)
                         && this.TheOneCollectionOf(access.Expression) is { } inALocal =>
                    this.region.Objects.TheCollectionIn(owner: inALocal, member: member.OriginalDefinition),
                // Such a collection holds what its state in the store says, and no other object.
                ElementAccessExpressionSyntax element
                    when this.TheCompleteCollection(element.Expression) is { } owner
                         && this.region.Objects.StateOf(owner)?.TheOneNewObjectOf(this.region.TypeOf(element))
                             is { } only => only,
                _ => null
            };

        return made != null && this.region.Objects.HoldsOnlyWhatWentIn(made) ? made : null;
    }

    // Records the one collection of each member that the constructor or the initializer of a
    // creation gave a collection.
    private void NotesTheCollectionsOf(
        SyntaxNode creation,
        LoopState made,
        InitializerExpressionSyntax? initializer)
    {
        if (this.quiet || TheOneNewObject(made) is not { } owner)
        {
            return;
        }

        if (this.Follow(creation) is { } constructed)
        {
            foreach ((ISymbol member, LoopState _, MadeObject? one) in constructed.Sets)
            {
                this.region.Objects.IsTheCollectionIn(owner: owner, member: member, collection: one);
            }
        }

        foreach (AssignmentExpressionSyntax assignment in
                 initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
        {
            if (assignment.Left is IdentifierNameSyntax name
                && this.region.SymbolOf(name) is { } member and (IPropertySymbol or IFieldSymbol))
            {
                this.region.Objects.IsTheCollectionIn(
                    owner: owner,
                    member: member.OriginalDefinition,
                    collection: this.TheOneCollectionThatIs(
                        from: assignment.Right,
                        state: this.Eval(assignment.Right)));
            }
        }
    }

    // The one collection that the value of an expression is, as a local has it at its
    // declaration. A name gives the record of its frame. Each other expression gives the one
    // new object of its state, when no collection gave the value.
    private MadeObject? TheOneCollectionThatIs(ExpressionSyntax from, LoopState state) =>
        Unwrapped(from) is IdentifierNameSyntax
            ? this.TheOneCollectionOf(from)
            : this.CanBeUnseen(from)
                ? null
                : TheOneNewObject(state);

    // The one collection in a member of the object that this code is in. The code set the member,
    // or the store has the record for the object of the call.
    private MadeObject? TheCollectionInAMemberOfThis(SyntaxNode written)
    {
        if (this.function != null
            || this.region.SymbolOf(written) is not { IsStatic: false } member
            || member is not (IFieldSymbol or IPropertySymbol))
        {
            return null;
        }

        ISymbol key = member.OriginalDefinition;

        return this.setOnThis.TryGetValue(key: key, value: out MadeObject? set)
            ? set
            : this.call?.Self is { } self
                ? this.region.Objects.TheCollectionIn(owner: self, member: key)
                : null;
    }
}
