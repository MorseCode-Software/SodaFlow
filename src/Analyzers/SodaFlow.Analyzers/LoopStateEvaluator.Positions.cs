using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the positions of the elements of a collection.
internal sealed partial class LoopStateEvaluator
{
    // The expression of the element that a read by a constant index gives, for a collection that
    // keeps its positions. For an array with two dimensions, the first index gives a row, and the
    // row contains the element. A dictionary in a local gives the value of a constant key: see
    // ValuesByKeyOf.
    private ExpressionSyntax? PositionOf(ElementAccessExpressionSyntax access) =>
        this.ElementsOf(access.Expression) is { } all
            ? this.PositionIn(index: access.ArgumentList.Arguments[0].Expression, count: all.Count) is { } index
                ? all[index]
                : null
            : this.ValueOfAKey(access);

    // The state of the expression of an element. The expression can be in the code of a definer of
    // this function. That code read it, and a second read here makes a second object for a
    // creation. Thus, the state is the one that the definer gave it.
    private LoopState StateAtAPosition(ExpressionSyntax position)
    {
        for (LoopStateEvaluator? outer = this.definer; outer != null; outer = outer.definer)
        {
            if (outer.memo.TryGetValue(key: position, value: out LoopState? known))
            {
                return known;
            }
        }

        return this.Eval(position);
    }

    // The position that a constant index gives in a collection with a number of elements. An index
    // from the end, ^k, gives the position count - k. Null for an index that is not in the collection.
    private int? PositionIn(SyntaxNode index, int count)
    {
        if (index is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.IndexExpression } fromTheEnd)
        {
            return this.region.ModelFor(fromTheEnd).GetConstantValue(fromTheEnd.Operand) is { Value: int back and > 0 }
                   && back <= count
                ? count - back
                : null;
        }

        return this.region.ModelFor(index).GetConstantValue(index) is { Value: int at and >= 0 } && at < count
            ? at
            : null;
    }

    // The value that a read by a constant key gives, for a dictionary in a local whose initializer
    // gave that key a value. Null for each other read.
    private ExpressionSyntax? ValueOfAKey(ElementAccessExpressionSyntax access)
    {
        if (access.ArgumentList.Arguments.Count != 1
            || this.region.SymbolOf(access.Expression) is not ILocalSymbol local)
        {
            return null;
        }

        if (!this.Frame.ValuesByKey.TryGetValue(
                key: local,
                value: out IReadOnlyDictionary<object, ExpressionSyntax>? known))
        {
            known = this.ValuesByKeyOf(local);
            this.Frame.ValuesByKey[local] = known;
        }

        return known != null
               && this.KeyOf(key: access.ArgumentList.Arguments[0].Expression, ofObjects: HasObjectKeys(local))
                   is { } key
               && known.TryGetValue(key: key, value: out ExpressionSyntax? value)
            ? value
            : null;
    }

    // True for a dictionary whose keys are objects. See KeyOf.
    private static bool HasObjectKeys(ISymbol local) =>
        local is ILocalSymbol { Type: INamedTypeSymbol { TypeArguments: { Length: 2 } keyAndValue } }
        && keyAndValue.First().SpecialType == SpecialType.System_Object;

    // The key that a constant gives in a dictionary. A key of the type object is a boxed value. Its
    // Equals is true only for a value of the same type, thus the key has the type of the constant.
    private object? KeyOf(ExpressionSyntax key, bool ofObjects)
    {
        SemanticModel model = this.region.ModelFor(key);
        Optional<object?> constant = model.GetConstantValue(key);

        return ofObjects
            ? constant is { HasValue: true, Value: { } value } && model.GetTypeInfo(key).Type is { } type
                ? (type.ToDisplayString(), value)
                : null
            : KeyOf(constant);
    }

    // The value expression for each constant key of the initializer that made a dictionary in a local,
    // when no code can change the dictionary. Each use of the local in its block must be a read: see
    // IsARead. The dictionary must be a Dictionary of the platform with the default comparer, and its
    // keys must have the type of a constant. Null for each other local.
    private IReadOnlyDictionary<object, ExpressionSyntax>? ValuesByKeyOf(ISymbol local)
    {
        if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax
                {
                    Initializer.Value: BaseObjectCreationExpressionSyntax
                    {
                        ArgumentList: null or { Arguments.Count: 0 }, Initializer: { } initializer
                    } made
                } declarator
            || this.region.TypeOf(made)
                is not INamedTypeSymbol { Name: "Dictionary", TypeArguments: { Length: 2 } types } dictionary
            || !SodaFlowSymbols.IsPlatform(dictionary)
            || !(IsATypeOfConstantKeys(types[0]) || HasObjectKeys(local))
            || declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } scope)
        {
            return null;
        }

        Dictionary<object, ExpressionSyntax> values = [];
        bool ofObjects = HasObjectKeys(local);

        foreach (ExpressionSyntax element in initializer.Expressions)
        {
            switch (element)
            {
                // A second value for a key replaces the first.
                case AssignmentExpressionSyntax
                    {
                        Left: ImplicitElementAccessSyntax { ArgumentList.Arguments: { Count: 1 } keys },
                        Right: not InitializerExpressionSyntax and var value
                    }
                    when this.KeyOf(key: keys[0].Expression, ofObjects: ofObjects) is { } key:
                    values[key] = value;

                    break;
                // Add throws an exception for a second value of a key.
                case InitializerExpressionSyntax { Expressions: { Count: 2 } pair }
                    when this.KeyOf(key: pair[0], ofObjects: ofObjects) is { } key && !values.ContainsKey(key):
                    values[key] = pair[1];

                    break;
                default:
                    return null;
            }
        }

        return scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == local.Name
                           && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(name), y: local))
            .All(this.IsARead)
            ? values
            : null;
    }

    // The key that a constant gives. Each integer and each char is a decimal. Thus, two constants of
    // two types give the same key when the conversion to the type of the key gives one value.
    private static object? KeyOf(Optional<object?> constant) =>
        constant switch
        {
            { HasValue: true, Value: string or bool } => constant.Value,
            { HasValue: true, Value: char letter } => (decimal)letter,
            { HasValue: true, Value: sbyte or byte or short or ushort or int or uint or long or ulong } =>
                Convert.ToDecimal(value: constant.Value, provider: CultureInfo.InvariantCulture),
            _ => null
        };

    // A type whose values the default comparer finds equal only when they are the same constant. An
    // object can have an int and a long that are not equal, and a double has more than one zero.
    private static bool IsATypeOfConstantKeys(ITypeSymbol key) =>
        key.TypeKind == TypeKind.Enum
        || key.SpecialType
            is SpecialType.System_String
            or SpecialType.System_Boolean
            or SpecialType.System_Char
            or SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64;

    // The positions that the store has for the one collection that an expression is. The code of
    // the block gives the positions of a local whose each use is a read: see ElementsOf. The
    // store has the positions of each other list and array that it knows. Each name of the
    // collection reads them: a second local, a parameter of a callee, and a member of an object.
    private (MadeObject Made, ImmutableArray<LoopState> All)? StorePositionsOf(ExpressionSyntax collection) =>
        this.TheCompleteCollection(collection) is { } made && this.region.Objects.PositionsOf(made) is { } all
            ? (made, all)
            : null;

    // The value of the element that a read by a constant index gives, from the positions of the
    // store. The value came into this code as its collection did, as each other element.
    private LoopState? AtAStorePosition(ElementAccessExpressionSyntax access) =>
        access.ArgumentList.Arguments.Count == 1
            ? this.AtAStorePosition(collection: access.Expression, index: access.ArgumentList.Arguments[0].Expression)
            : null;

    // The same for the collection and the index of an ElementAt.
    private LoopState? AtAStorePosition(ExpressionSyntax collection, SyntaxNode index)
    {
        if (this.StorePositionsOf(collection) is not (_, var all)
            || this.PositionIn(index: index, count: all.Length) is not { } at)
        {
            return null;
        }

        return this.Eval(collection).Origin is { } origin ? all[at].From(origin) : all[at];
    }

    /// <summary>
    ///     The state of the element that <c>ElementAt</c> or
    ///     <c>ElementAtOrDefault</c> of the platform gives for a constant index.
    ///     The walk must know the positions of the collection. Null for each
    ///     other call.
    /// </summary>
    /// <remarks>
    ///     An index that is not in the collection gives no state. Then, the call
    ///     throws an exception or gives the default value.
    /// </remarks>
    private LoopState? AtThePositionOfACall(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        invocation is
        {
            ArgumentList.Arguments: { Count: 1 } arguments,
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ElementAt" or "ElementAtOrDefault"
            } call
        }
        && method.ReducedFrom is { ContainingType: { } queries }
        && SodaFlowSymbols.IsLinqEnumerable(queries)
            ? this.AtAStorePosition(collection: call.Expression, index: arguments[0].Expression)
            : null;

    /// <summary>
    ///     The state of each element of a collection whose positions the walk
    ///     knows, in order. Null for each other collection.
    /// </summary>
    public IReadOnlyList<LoopState>? StatesAtThePositionsOf(ExpressionSyntax collection)
    {
        if (this.ElementsOf(collection) is { } elements)
        {
            return [.. elements.Select(this.StateAtAPosition)];
        }

        if (this.StorePositionsOf(collection) is not (_, var all))
        {
            return null;
        }

        IReadOnlyList<LoopState> kept = all;

        return this.Eval(collection).Origin is { } origin ? [.. kept.Select(state => state.From(origin))] : kept;
    }

    /// <summary>
    ///     The state of the first element of a collection, and the number of
    ///     elements, when the walk knows the positions. Null for each other
    ///     collection.
    /// </summary>
    public (LoopState First, int Count)? FirstElementOf(ExpressionSyntax collection)
    {
        if (this.ElementsOf(collection) is { Count: > 0 } elements)
        {
            return (this.Eval(elements[0]), elements.Count);
        }

        if (this.StorePositionsOf(collection) is not (_, { Length: > 0 } all))
        {
            return null;
        }

        return (this.Eval(collection).Origin is { } origin ? all[0].From(origin) : all[0], all.Length);
    }

    /// <summary>
    ///     The array of a foreach statement over a name of an array, as the loop
    ///     starts. Null for each other foreach statement.
    /// </summary>
    /// <param name="loop">The foreach statement.</param>
    /// <param name="state">The state of the collection at the start of the loop.</param>
    public ArrayOfAForeach? ArrayOf(ForEachStatementSyntax loop, LoopState state) =>
        Unwrapped(loop.Expression) is IdentifierNameSyntax name && this.region.TypeOf(name) is IArrayTypeSymbol
            ? new ArrayOfAForeach(name: name, state: state, one: this.TheOneCollectionOf(name))
            : null;

    /// <summary>
    ///     True when the body of a foreach statement over an array puts a value
    ///     into an element that a later turn can read.
    /// </summary>
    /// <remarks>
    ///     A foreach statement reads an element of an array at the start of its
    ///     turn. Thus, a later turn reads a value that a turn before put into the
    ///     array. A list stops the loop with an exception after such a change.
    /// </remarks>
    /// <param name="loop">The foreach statement.</param>
    /// <param name="array">The result of <see cref="ArrayOf" /> for the loop.</param>
    public bool PutsIntoALaterElement(ForEachStatementSyntax loop, ArrayOfAForeach array) =>
        loop.Statement.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => this.PutsIntoALaterElement(assignment: assignment, array: array));

    /// <summary>
    ///     The state of each element of the array of a foreach statement at this
    ///     time. See <see cref="PutsIntoALaterElement(ForEachStatementSyntax, ArrayOfAForeach)" />.
    /// </summary>
    /// <param name="loop">The foreach statement.</param>
    /// <param name="array">The result of <see cref="ArrayOf" /> for the loop.</param>
    /// <param name="type">The type of the element.</param>
    public LoopState ElementsNow(ForEachStatementSyntax loop, ArrayOfAForeach array, ITypeSymbol? type)
    {
        LoopState now = this.EvalAgain(loop.Expression);

        // The body can give the name a different array. The loop reads the array of the start,
        // and the store has the elements of that array.
        return ElementOf(
            collection: now.Objects.SequenceEqual(array.State.Objects) ? now : this.InTheStoreNow(array) ?? now,
            type: type);
    }

    // True for an assignment to an element of the array that a later turn can read. A second name
    // of the array of the start is the same array. No later turn reads the first element.
    private bool PutsIntoALaterElement(AssignmentExpressionSyntax assignment, ArrayOfAForeach array)
    {
        if (Unwrapped(assignment.Left)
                is not ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
            || Unwrapped(element.Expression) is not IdentifierNameSyntax written
            || this.ConstantIndexOf(indexes[0]) == 0)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(
                   x: this.region.SymbolOf(written),
                   y: this.region.SymbolOf(array.Name))
               || (array.One != null && ReferenceEquals(objA: this.TheOneCollectionOf(written), objB: array.One));
    }

    // The state of the array of the start of a foreach statement, as the store has it now. Null
    // when the store does not have each object of the array. See Refreshed.
    private LoopState? InTheStoreNow(ArrayOfAForeach array)
    {
        LoopState? stored = null;

        foreach (MadeObject made in array.State.Objects)
        {
            if (this.region.Objects.StateOf(made) is not { } one)
            {
                return null;
            }

            stored = stored == null ? one : stored.Join(one);
        }

        if (stored == null)
        {
            return null;
        }

        LoopState refreshed =
            array.State.RefreshedFrom(stored: stored.FromIfUnknown(array.State.AnyOrigin), all: true);

        LoopState parts =
            this.InAnObjectOf(
                turn: array.One is { } only ? only.Turn : TurnOfTheNewOne(refreshed),
                state: refreshed);

        return ReferenceEquals(objA: parts, objB: refreshed) ? refreshed : parts.WithObjects(refreshed.Objects);
    }

    // The number that an argument has in the code, when it is a constant that is not negative.
    private int? ConstantIndexOf(ArgumentSyntax argument) =>
        this.region.ModelFor(argument).GetConstantValue(argument.Expression) is { Value: int index and >= 0 }
            ? index
            : null;

    /// <summary>
    ///     The positions of a collection before a put that the walk reads now.
    ///     Null when the store has none, or when the put is not sure to run. The
    ///     put then gives the collection the new positions.
    /// </summary>
    /// <param name="collection">The expression of the collection.</param>
    /// <param name="replaces">True when the put always runs in its function.</param>
    /// <param name="turn">The body of the loop whose each turn always runs the put.</param>
    public (MadeObject Made, ImmutableArray<LoopState> All)? PositionsBefore(
        ExpressionSyntax collection,
        bool replaces,
        SyntaxNode? turn) =>
        !this.quiet
        && this.StorePositionsOf(collection) is (var made, var all)
        && this.IsSureFor(made: made, replaces: replaces, turn: turn)
            ? (made, all)
            : null;

    /// <summary>
    ///     The same as <see cref="PositionsBefore" />, for an assignment to an
    ///     element. Each turn of a loop can always run the assignment, on a
    ///     collection that is older than the turn. Then, the assignment is sure
    ///     until the end of the turn, as a write to a member is. After the loop,
    ///     each position can have its value from before the loop also.
    /// </summary>
    /// <param name="collection">The expression of the collection.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    /// <param name="turn">The body of the loop whose each turn always runs the assignment.</param>
    public (MadeObject Made, ImmutableArray<LoopState> All)? PositionsBeforeAnAssignment(
        ExpressionSyntax collection,
        bool replaces,
        SyntaxNode? turn)
    {
        if (this.PositionsBefore(collection: collection, replaces: replaces, turn: turn) is { } sure)
        {
            return sure;
        }

        if (this.quiet || this.StorePositionsOf(collection) is not (var made, var all) || made.IsOfTurnsBefore)
        {
            return null;
        }

        // The loop is in this code, or in the code of a caller for an assignment of a callee.
        (SyntaxNode Turn, LoopFrame Frame)? inItsTurn =
            made.IsFrom(this.Frame)
                ? turn != null && made.Turn != turn && IsTheBodyOfALoop(turn)
                    ? (turn, this.Frame)
                    : null
                : replaces
                    ? this.TurnOfACaller
                    : null;

        if (inItsTurn is not (var itsTurn, var itsFrame))
        {
            return null;
        }

        if (!itsFrame.PositionsBeforeATurn.Any(kept =>
                kept.Turn == itsTurn && ReferenceEquals(objA: kept.Made, objB: made)))
        {
            itsFrame.PositionsBeforeATurn.Add((itsTurn, made, all));
        }

        return (made, all);
    }

    /// <summary>
    ///     Records that an assignment gave an element of a collection a value. An
    ///     assignment that is sure to run, to a constant index, gives that
    ///     position the value.
    /// </summary>
    /// <param name="before">The result of <see cref="PositionsBefore" /> for the assignment.</param>
    /// <param name="indexes">The indexes of the element.</param>
    /// <param name="value">The expression of the value.</param>
    public void HasThePositionAfter(
        (MadeObject Made, ImmutableArray<LoopState> All)? before,
        BracketedArgumentListSyntax indexes,
        ExpressionSyntax value)
    {
        if (before is (var made, var all)
            && indexes.Arguments.Count == 1
            && this.ConstantIndexOf(indexes.Arguments[0]) is { } index
            && index < all.Length)
        {
            this.region.Objects.HasPositions(
                collection: made,
                values: all.SetItem(index: index, item: this.Eval(value)),
                givenBy: this.region.Objects.GivenBy(collection: made, values: all).SetItem(index: index, item: value));
        }
    }

    /// <summary>
    ///     The same as <see cref="HasThePositionAfter" />, for a value that has
    ///     no expression in this code.
    /// </summary>
    /// <param name="before">The result of <see cref="PositionsBefore" /> for the put.</param>
    /// <param name="element">The element.</param>
    /// <param name="state">The state of the value.</param>
    public void HasTheStateAtThePosition(
        (MadeObject Made, ImmutableArray<LoopState> All)? before,
        ElementAccessExpressionSyntax element,
        LoopState state)
    {
        if (before is (var made, var all)
            && element.ArgumentList.Arguments.Count == 1
            && this.ConstantIndexOf(element.ArgumentList.Arguments[0]) is { } index
            && index < all.Length)
        {
            this.region.Objects.HasPositions(collection: made, values: all.SetItem(index: index, item: state));
        }
    }

    /// <summary>
    ///     Records what a call of a method of the platform on a collection does
    ///     to its positions. <c>Add</c>, <c>Insert</c>, <c>RemoveAt</c>,
    ///     <c>Remove</c>, and <c>Reverse</c> of a list give the new positions. A
    ///     search or a copy changes nothing. Each other method can change the
    ///     order, and takes the positions away.
    /// </summary>
    /// <param name="before">The result of <see cref="PositionsBeforeACall" /> for the call.</param>
    /// <param name="collection">The owner of the call.</param>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public void MovesPositions(
        (MadeObject Made, ImmutableArray<LoopState> All)? before,
        ExpressionSyntax collection,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        // The walk gives the collection a new state for the call, and the positions stay.
        if (KeepsEachPosition(method))
        {
            if (before is (var same, var positions))
            {
                this.region.Objects.HasPositions(collection: same, values: positions);
            }

            return;
        }

        this.ForgetsThePositionsOf(collection);

        // The object with the positions is a list. The code can name it by an interface.
        if (before is not (var made, var all)
            || method.ContainingType is not { Name: "List" or "IList" or "ICollection" } owner
            || !SodaFlowSymbols.IsPlatform(owner))
        {
            return;
        }

        ImmutableArray<LoopState>? after =
            method.Name switch
            {
                "Add" when For("item") is { } item => all.Add(this.Eval(item.Expression)),
                "Insert" when For("index") is { } at
                              && this.ConstantIndexOf(at) is { } index
                              && index <= all.Length
                              && For("item") is { } item =>
                    all.Insert(index: index, item: this.Eval(item.Expression)),
                "RemoveAt" when For("index") is { } at
                                && this.ConstantIndexOf(at) is { } index
                                && index < all.Length =>
                    all.RemoveAt(index),
                "Remove" when For("item") is { } item =>
                    this.AfterARemove(all: all, item: item.Expression, list: owner),
                "Reverse" when arguments.Count == 0 =>
                    ImmutableArray.CreateRange(all.Reverse()),
                _ => null
            };

        if (after is { } known)
        {
            this.region.Objects.HasPositions(collection: made, values: known);
        }

        return;

        // The argument for the parameter with a name. An argument can have its name in the code.
        ArgumentSyntax? For(string name) =>
            arguments
                .Where((argument, position) =>
                    SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position)?.Name == name)
                .FirstOrDefault();
    }

    // The positions after a call of Remove with an object. Remove takes the first element that is
    // equal to that object away. Null when the walk does not know which element that is. An
    // identity of the turns before is more than one object, and tells nothing here.
    private ImmutableArray<LoopState>? AfterARemove(
        ImmutableArray<LoopState> all,
        ExpressionSyntax item,
        INamedTypeSymbol list)
    {
        if (list.TypeArguments.Length != 1
            || !IsEqualOnlyToItself(list.TypeArguments[0])
            || this.TheOneObjectOf(item) is not { IsOfTurnsBefore: false } taken)
        {
            return null;
        }

        for (int i = 0; i < all.Length; i++)
        {
            ImmutableArray<MadeObject> here = this.region.Objects.Now(all[i].Objects);

            // The first element that can be the object. A collection that keeps its positions
            // holds no object that the store does not know.
            if (here.Contains(taken))
            {
                return here.Length == 1 ? all.RemoveAt(i) : null;
            }
        }

        // The list does not have the object, and Remove changes nothing.
        return all;
    }

    // True for a class whose object is equal to itself and to no other object. The class has
    // the sealed modifier, it is not IEquatable, and it has no Equals of its own. A record has
    // one.
    private static bool IsEqualOnlyToItself(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true } sealedClass
            || sealedClass.AllInterfaces.Any(static contract => contract.Name == "IEquatable"))
        {
            return false;
        }

        for (INamedTypeSymbol? part = sealedClass;
             part is { SpecialType: not SpecialType.System_Object };
             part = part.BaseType)
        {
            if (part.GetMembers("Equals").Any(static member => member.IsOverride))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     True for an element at a constant index of a collection that keeps its
    ///     positions, where that position has no object. A coalescing assignment
    ///     to such an element always gives it the value.
    /// </summary>
    /// <remarks>
    ///     Such a collection holds only values that the walk knew when they went
    ///     in. Thus, a position with no object of the store has null.
    /// </remarks>
    public bool HasNoObjectAt(ExpressionSyntax target) =>
        Unwrapped(target) is ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
        && this.region.TypeOf(element) is { } type
        && this.CanBeAnObject(type)
        && this.StorePositionsOf(element.Expression) is (_, var all)
        && this.ConstantIndexOf(indexes[0]) is { } index
        && index < all.Length
        && all[index].Objects.IsEmpty;

    /// <summary>
    ///     True for an element at a constant index of a collection that keeps its
    ///     positions, where that position has an object for sure. A coalescing
    ///     assignment to such an element changes nothing.
    /// </summary>
    /// <remarks>
    ///     A state does not say that its value is not null. Thus, the walk reads
    ///     the expression that gave the position its value.
    /// </remarks>
    public bool HasAnObjectAt(ExpressionSyntax target) =>
        Unwrapped(target) is ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
        && this.StorePositionsOf(element.Expression) is (var made, var all)
        && this.ConstantIndexOf(indexes[0]) is { } index
        && index < all.Length
        && this.region.Objects.GivenBy(collection: made, values: all)[index] is ExpressionSyntax given
        && this.IsAnObjectForSure(given);

    // True for a creation of an object of a class, and for a local that only such a creation gave
    // a value. A local can get null from code after the creation. A creation of a struct with a
    // nullable type gives null.
    private bool IsAnObjectForSure(ExpressionSyntax value) =>
        Unwrapped(value) switch
        {
            IdentifierNameSyntax name =>
                this.region.SymbolOf(name) is ILocalSymbol { RefKind: RefKind.None } local
                && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                && IsACreation(made)
                && this.region.TypeOf(made) is { IsReferenceType: true }
                && FunctionOf(declarator) is { } code
                && !this.GetsAValue(
                    name: local,
                    code: code,
                    after: declarator.Span.End,
                    before: int.MaxValue,
                    orAFunctionUsesIt: false),
            var other => IsACreation(other) && this.region.TypeOf(other) is { IsReferenceType: true }
        };

    private static bool IsACreation(ExpressionSyntax value) =>
        Unwrapped(value)
            is BaseObjectCreationExpressionSyntax
            or ArrayCreationExpressionSyntax
            or ImplicitArrayCreationExpressionSyntax
            or AnonymousObjectCreationExpressionSyntax;

    /// <summary>
    ///     What a <c>CompareExchange</c> does to an element at a constant index
    ///     of a collection that keeps its positions. True when the call puts the
    ///     value, as an <c>Exchange</c> does. False when the call changes nothing.
    ///     Null when the walk does not know.
    /// </summary>
    /// <remarks>
    ///     The call compares the comparand and the element by identity. Two
    ///     objects of two creations are two objects. A different identity of the
    ///     store can be the same object, as the result of a call can be. An
    ///     identity of the turns before is more than one object.
    /// </remarks>
    /// <param name="location">The location of the call.</param>
    /// <param name="comparand">The comparand of the call.</param>
    public bool? ExchangesAt(ExpressionSyntax location, ExpressionSyntax comparand) =>
        Unwrapped(location) is ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
        && this.StorePositionsOf(element.Expression) is (_, var all)
        && this.ConstantIndexOf(indexes[0]) is { } index
        && index < all.Length
        && this.TheOneObjectIn(all[index]) is { IsNew: true, IsOfTurnsBefore: false } there
        && this.TheOneObjectOf(comparand) is { IsNew: true, IsOfTurnsBefore: false } compared
            ? there == compared
            : null;

    /// <summary>
    ///     The positions of a collection before a call of a method of the
    ///     platform on it. A method that changes no position keeps them also when
    ///     its call is not sure to run.
    /// </summary>
    /// <param name="collection">The owner of the call.</param>
    /// <param name="method">The method.</param>
    /// <param name="replaces">True when the call always runs in its function.</param>
    /// <param name="turn">The body of the loop whose each turn always runs the call.</param>
    public (MadeObject Made, ImmutableArray<LoopState> All)? PositionsBeforeACall(
        ExpressionSyntax collection,
        IMethodSymbol method,
        bool replaces,
        SyntaxNode? turn) =>
        KeepsEachPosition(method)
            ? this.StorePositionsOf(collection)
            : this.PositionsBefore(collection: collection, replaces: replaces, turn: turn);

    // A method of a list or an array that reads the collection and changes no element.
    private static bool KeepsEachPosition(ISymbol method) =>
        method.Name is "Contains" or "IndexOf" or "LastIndexOf" or "ToArray" or "AsReadOnly";

    /// <summary>
    ///     Records that the walk does not know the positions of each collection
    ///     that an expression can be. The code changed an element, or the order
    ///     of the elements, and the walk does not follow that change to a position.
    /// </summary>
    public void ForgetsThePositionsOf(ExpressionSyntax collection)
    {
        if (this.quiet)
        {
            return;
        }

        foreach (MadeObject made in this.Eval(collection).Objects)
        {
            this.region.Objects.HasNoPositions(made);
        }
    }

    /// <summary>
    ///     Adds to what a name holds the state that a callee gives back for its
    ///     parameter. The callee had the same object, and the store has each
    ///     change of the callee. Thus, the positions of the collection stay as
    ///     the callee left them.
    /// </summary>
    public void MergesWhatACalleePut(ExpressionSyntax target, LoopState state)
    {
        // The callee put objects into code with no body that the argument names. See
        // KeepsWhatItGets.
        if (state is { IsNone: true, HoldsAnObject: true } && IsOfNoObjectOfTheStore(this.Eval(target)))
        {
            this.KeepsInTheNameOf(owner: target, holding: state);

            return;
        }

        (MadeObject Made, ImmutableArray<LoopState> All)? kept = this.StorePositionsOf(target);
        this.Merge(target: target, state: state);

        if (kept is (var made, var all))
        {
            this.region.Objects.HasPositions(collection: made, values: all);
        }
    }

    /// <summary>
    ///     The expression of each element of the collection in a local, in
    ///     order, when the collection keeps its positions. Null for each other
    ///     expression.
    /// </summary>
    /// <remarks>
    ///     A collection keeps its positions when a literal made it and no code
    ///     can change it. Each use of the local in its block must be a read. A
    ///     read is an index, <c>Count</c> or <c>Length</c>, or the collection of a
    ///     foreach. <c>First</c> or <c>Last</c> of the platform with no argument
    ///     is a read also. Any other use can change the collection or give it a
    ///     second name. Then, the local has one state for all its elements.
    ///     <para>
    ///         <c>Add</c> of a list of the platform puts a value after each element
    ///         that the list has, and moves none of them. Thus, a statement at the
    ///         first level of the block of the local that is such a call is no
    ///         change to a position. A read has the elements of each such statement
    ///         before it. A function can run after the next such statement, thus a
    ///         read in a function has no positions for such a list.
    ///     </para>
    /// </remarks>
    private IReadOnlyList<ExpressionSyntax>? ElementsOf(SyntaxNode collection)
    {
        if (this.region.SymbolOf(collection) is not ILocalSymbol local)
        {
            return null;
        }

        if (!this.Frame.Positions.TryGetValue(key: local, value: out IReadOnlyList<ExpressionSyntax>? known))
        {
            known = this.LiteralAndAddsOf(local);
            this.Frame.Positions[local] = known;
        }

        // Each element of the literal is in the declaration, which is before each read.
        if (known == null
            || known.Count == 0
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } declared
            || known[known.Count - 1].SpanStart < declared.Span.End)
        {
            return known;
        }

        return FunctionOf(collection) == FunctionOf(declared)
            ? [.. known.TakeWhile(element => element.SpanStart < collection.SpanStart)]
            : null;
    }

    // The elements of the literal that made a local, and then the value of each Add of the local.
    // Each such Add is a statement at the first level of the block of the local. Null when a use of
    // the local is not such an Add and not a read.
    private IReadOnlyList<ExpressionSyntax>? LiteralAndAddsOf(ILocalSymbol local)
    {
        if (this.LiteralOf(local) is not (var written, var uses)
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().FirstAncestorOrSelf<BlockSyntax>()
                is not { } scope)
        {
            return null;
        }

        List<ExpressionSyntax> all = [.. written];

        foreach (IdentifierNameSyntax use in uses)
        {
            if (this.AddedBy(use: use, scope: scope) is { } added)
            {
                all.Add(added);
            }
            else if (!this.IsARead(use))
            {
                return null;
            }
        }

        return all;
    }

    // The value that a statement at the first level of a block adds to a list of the platform. The
    // name is the name of the list in that statement. Null for each other use of the name.
    private ExpressionSyntax? AddedBy(SyntaxNode use, BlockSyntax scope) =>
        this.AnAddOf(use) is (var added, var statement) && statement.Parent == scope ? added : null;

    // The same for a statement at each level, with the statement.
    private (ExpressionSyntax Added, ExpressionStatementSyntax Statement)? AnAddOf(SyntaxNode use) =>
        use.Parent is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "Add",
            Parent: InvocationExpressionSyntax
            {
                ArgumentList.Arguments: { Count: 1 } arguments, Parent: ExpressionStatementSyntax statement
            } call
        } access
        && access.Expression == use
        && this.region.SymbolOf(call) is IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" } owner }
        && SodaFlowSymbols.IsPlatform(owner)
            ? (arguments[0].Expression, statement)
            : null;

    // True for a local that a literal made, when each use of it is a read or a put: see APutOf.
    // Thus, no code can remove an element from the collection.
    private bool OnlyGetsAdds(ExpressionSyntax collection) =>
        Unwrapped(collection) is IdentifierNameSyntax name
        && this.region.SymbolOf(name) is ILocalSymbol local
        && this.LiteralOf(local) is (_, var uses)
        && uses.All(use => this.IsARead(use) || this.APutOf(use) != null);

    // The values that a statement puts into a list of the platform, with the statement. The name is
    // the name of the list in that statement. The statement is an Add, an Insert, or an AddRange, and
    // none of them removes an element. Only an AddRange of a literal gives its values. Null for
    // each other use of the name.
    private (IReadOnlyList<ExpressionSyntax> Put, ExpressionStatementSyntax Statement)? APutOf(SyntaxNode use)
    {
        if (this.AnAddOf(use) is (var added, var statement))
        {
            return ([added], statement);
        }

        if (use.Parent is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Insert" or "AddRange",
                Parent: InvocationExpressionSyntax { Parent: ExpressionStatementSyntax put } call
            } access
            || access.Expression != use
            || this.region.SymbolOf(call)
                is not IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" } owner } method
            || !SodaFlowSymbols.IsPlatform(owner))
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        if (method.Name == "Insert")
        {
            return (
                [
                    .. arguments
                        .Where((argument, position) =>
                            SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position)?.Name
                            == "item")
                        .Select(static argument => argument.Expression)
                ],
                put);
        }

        return arguments.Count == 1
            ? (Unwrapped(arguments[0].Expression) switch
                {
                    CollectionExpressionSyntax written
                        when written.Elements.All(static element => element is ExpressionElementSyntax) =>
                        [
                            .. written.Elements.Cast<ExpressionElementSyntax>()
                                .Select(static element => element.Expression)
                        ],
                    ArrayCreationExpressionSyntax { Initializer: { } initializer } => [.. initializer.Expressions],
                    ImplicitArrayCreationExpressionSyntax implied => [.. implied.Initializer.Expressions],
                    _ => []
                },
                put)
            : null;
    }

    // The elements of the literal that made a local, and each name of the local in its block.
    private (IReadOnlyList<ExpressionSyntax> Elements, IEnumerable<IdentifierNameSyntax> Uses)? LiteralOf(
        ILocalSymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
        && this.LiteralElements(made: made, type: local.Type) is { } written
        && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } scope
            ? (written,
                scope.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(name => name.Identifier.ValueText == local.Name
                                   && SymbolEqualityComparer.Default.Equals(
                                       x: this.region.SymbolOf(name),
                                       y: local)))
            : null;

    // The methods of the platform that put one value into their collection, and that a run on an
    // empty collection has a result for.
    private static readonly ImmutableHashSet<string> PutsOfOneValue =
        ImmutableHashSet.Create("Add", "TryAdd", "SetItem", "GetOrAdd", "AddOrUpdate", "Enqueue");

    /// <summary>
    ///     True for a collection that has no element when a put into it runs. The
    ///     expression makes an empty collection, or it is the first use of a local
    ///     that such an expression made.
    /// </summary>
    /// <remarks>
    ///     No code before the first use can put an object into the collection. A
    ///     use in a loop or in a function of the block can run after a second use.
    ///     An argument of the put runs before the put. Thus, the put has no second
    ///     use of the local.
    /// </remarks>
    /// <param name="collection">The expression for the collection.</param>
    /// <param name="put">The call or the assignment that puts a value into the collection.</param>
    public bool IsEmptyHere(ExpressionSyntax collection, SyntaxNode put)
    {
        ExpressionSyntax written = Unwrapped(collection);

        if (this.MakesAnEmptyCollection(written))
        {
            return true;
        }

        if (written is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
            || !this.MakesAnEmptyCollection(made)
            || declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } scope)
        {
            return false;
        }

        IdentifierNameSyntax[] firstTwo =
        [
            .. scope.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(use =>
                    use.Identifier.ValueText == local.Name
                    && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: local))
                .Take(2)
        ];

        return firstTwo[0] == name
               && (firstTwo.Length == 1 || !put.Span.Contains(firstTwo[1].Span))
               && !name.Ancestors()
                   .TakeWhile(ancestor => ancestor != scope)
                   .Any(static ancestor =>
                       LoopBodyOf(ancestor) != null
                       || ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }

    // True for an expression that makes a collection of the platform with no element. That is an
    // empty collection expression, or the Empty of an immutable collection. It is also a
    // constructor or a CreateBuilder that gets only comparers and numbers.
    private bool MakesAnEmptyCollection(ExpressionSyntax made) =>
        Unwrapped(made) switch
        {
            CollectionExpressionSyntax { Elements.Count: 0 } => true,
            BaseObjectCreationExpressionSyntax { Initializer: null or { Expressions.Count: 0 } } creation =>
                this.GetsOnlyComparersAndNumbers(creation),
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateBuilder" }
            } call => this.GetsOnlyComparersAndNumbers(call),
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Empty" } access =>
                this.region.SymbolOf(access) is IFieldSymbol { IsStatic: true, ContainingType: { } owner }
                && SodaFlowSymbols.IsPlatform(owner),
            _ => false
        };

    private bool GetsOnlyComparersAndNumbers(SyntaxNode call) =>
        this.region.SymbolOf(call) is IMethodSymbol callee
        && SodaFlowSymbols.IsPlatform(callee.ContainingType)
        && callee.Parameters.All(static parameter =>
            SodaFlowSymbols.IsComparer(parameter.Type) || parameter.Type.SpecialType == SpecialType.System_Int32);

    /// <summary>
    ///     True for a collection that has one element, as far as the code says.
    ///     The expression is a literal with one element, or a local that such a
    ///     literal made and whose number of elements no code can change.
    /// </summary>
    /// <remarks>
    ///     An array has the number of elements that it got. Thus, each use of an
    ///     array in a local keeps it, but an assignment to the local and a
    ///     <c>ref</c> to it. A list keeps it while each use is a read, a
    ///     <c>Sort</c>, or a <c>Reverse</c>.
    /// </remarks>
    private bool HasOneElement(ExpressionSyntax collection)
    {
        while (collection is ParenthesizedExpressionSyntax inner)
        {
            collection = inner.Expression;
        }

        switch (collection)
        {
            // A collection expression in a call has one element for each type that the compiler
            // makes from it. A spread has the number of elements of its collection.
            case CollectionExpressionSyntax { Elements.Count: 1 } written:
                return written.Elements[0] is not SpreadElementSyntax spread || this.HasOneElement(spread.Expression);
            case ConditionalExpressionSyntax either:
                return this.HasOneElement(either.WhenTrue) && this.HasOneElement(either.WhenFalse);
            case SwitchExpressionSyntax { Arms.Count: > 0 } chosen:
                return chosen.Arms.All(arm => this.HasOneElement(arm.Expression));
        }

        if (this.region.SymbolOf(collection) is not ILocalSymbol local)
        {
            return this.LiteralElements(made: collection, type: this.region.TypeOf(collection)) is { Count: 1 };
        }

        if (!this.Frame.HasOneElement.TryGetValue(key: local, value: out bool known))
        {
            known =
                this.LiteralOf(local) is ({ Count: 1 }, var uses)
                && uses.All(local.Type is IArrayTypeSymbol ? static name => !IsWritten(name) : this.KeepsTheNumber);

            this.Frame.HasOneElement[local] = known;
        }

        return known;
    }

    // A read of a list, or a call of its Sort or its Reverse, which change only the order.
    private bool KeepsTheNumber(IdentifierNameSyntax name) =>
        this.IsARead(name)
        || (name.Parent is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Sort" or "Reverse", Parent: InvocationExpressionSyntax call
            }
            && this.region.SymbolOf(call)
                is IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" or "ArrayList" } owner }
            && SodaFlowSymbols.IsPlatform(owner));

    // The elements of a literal whose object has them in the order of the code.
    private IReadOnlyList<ExpressionSyntax>? LiteralElements(ExpressionSyntax made, ITypeSymbol? type) =>
        made switch
        {
            CollectionExpressionSyntax collection when SodaFlowSymbols.KeepsTheOrder(type) =>
                this.ElementsOfACollectionExpression(collection),
            InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ArrayInitializerExpression } initializer =>
                [.. initializer.Expressions],
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => [.. initializer.Expressions],
            ImplicitArrayCreationExpressionSyntax implied => [.. implied.Initializer.Expressions],
            // A List with no argument for its constructor, and an Add for each element.
            ObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 }, Initializer: { } added }
                when this.IsAListOf(creation: made, added: added) => [.. added.Expressions],
            ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: { } added }
                when this.IsAListOf(creation: made, added: added) => [.. added.Expressions],
            // The same with no initializer: the list has no element.
            BaseObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 }, Initializer: null }
                when SodaFlowSymbols.KeepsTheOrder(this.region.TypeOf(made)) => [],
            _ => null
        };

    // The elements of a collection expression. A spread of a local whose positions the walk knows
    // gives those elements. A spread of each other value has a number of elements that the code
    // does not say.
    private IReadOnlyList<ExpressionSyntax>? ElementsOfACollectionExpression(CollectionExpressionSyntax collection)
    {
        List<ExpressionSyntax> all = [];

        foreach (CollectionElementSyntax element in collection.Elements)
        {
            switch (element)
            {
                case ExpressionElementSyntax one:
                    all.Add(one.Expression);

                    break;
                case SpreadElementSyntax spread when this.ElementsOf(spread.Expression) is { } spreadOnes:
                    all.AddRange(spreadOnes);

                    break;
                default:
                    return null;
            }
        }

        return all;
    }

    // The initializer calls Add one time for each element. An element with braces gives the
    // arguments of one Add, and is not the expression of an element.
    private bool IsAListOf(ExpressionSyntax creation, InitializerExpressionSyntax added) =>
        added.Expressions.All(static element => element is not InitializerExpressionSyntax)
        && SodaFlowSymbols.KeepsTheOrder(this.region.TypeOf(creation));

    // The name is a name of the local, thus it is the owner in an index or in a member access.
    private bool IsARead(SyntaxNode name) =>
        name.Parent switch
        {
            ElementAccessExpressionSyntax access => !IsWritten(access),
            // The property. A method with the name Count can be one of this compilation.
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "Length" } member =>
                member.Parent is not InvocationExpressionSyntax,
            // A method of this compilation with the name First can change the collection.
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "First" or "Last",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } call
            } =>
                this.region.SymbolOf(call) is IMethodSymbol { ReducedFrom.ContainingType: { } owner }
                && SodaFlowSymbols.IsLinqEnumerable(owner),
            CommonForEachStatementSyntax => true,
            // A spread puts a copy of each element into a second collection.
            SpreadElementSyntax => true,
            // A pattern only reads the value that it tests.
            IsPatternExpressionSyntax test => test.Expression == name,
            SwitchExpressionSyntax choice => choice.GoverningExpression == name,
            SwitchStatementSyntax choice => choice.Expression == name,
            _ => false
        };

    // True when the code can change the element that an element access names. The code changes
    // it with an assignment, an increment, a ref or out argument, or a ref to the element.
    private static bool IsWritten(SyntaxNode element)
    {
        // A target of a deconstruction is in a tuple on the left of an assignment.
        while (true)
        {
            switch (element.Parent)
            {
                case ParenthesizedExpressionSyntax around:
                    element = around;
                    continue;
                case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
                    element = tuple;
                    continue;
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left == element;
                case ArgumentSyntax argument:
                    return !argument.RefKindKeyword.IsKind(SyntaxKind.None);
                case RefExpressionSyntax:
                    return true;
                case PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax:
                    return element.Parent.Kind()
                        is SyntaxKind.PreIncrementExpression
                        or SyntaxKind.PreDecrementExpression
                        or SyntaxKind.PostIncrementExpression
                        or SyntaxKind.PostDecrementExpression;
                default:
                    return false;
            }
        }
    }
}
