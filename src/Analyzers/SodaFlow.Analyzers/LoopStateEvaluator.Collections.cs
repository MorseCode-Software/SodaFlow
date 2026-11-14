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

        if (elements != null && elements.All(this.IsKnownWhenItGoesIn))
        {
            this.region.Objects.IsComplete(made);
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
    // name.
    private MadeObject? TheOneCollectionOf(ExpressionSyntax expression) =>
        this.function == null
        && Unwrapped(expression) is IdentifierNameSyntax name
        && this.region.SymbolOf(name) is { } local and (ILocalSymbol or IParameterSymbol)
        && this.Frame.TheOneCollectionOf.TryGetValue(key: local, value: out MadeObject? one)
            ? one
            : null;

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
        ExpressionSyntax creation,
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
    private MadeObject? TheCollectionInAMemberOfThis(ExpressionSyntax written)
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
